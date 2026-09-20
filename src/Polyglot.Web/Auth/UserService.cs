using System.Text.RegularExpressions;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.Auth;

public sealed partial class UserService(AppDbContext db, IPasswordHasher<User> hasher)
{
    public const int MinPasswordLength = 10;

    // Verified against when the user does not exist, so that response time does not reveal valid usernames.
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), "dummy-password-value");

    [GeneratedRegex("^[A-Za-z0-9._@+-]{3,64}$")]
    private static partial Regex UsernamePattern();

    public static string Normalize(string username) => username.Trim().ToUpperInvariant();

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.Users.CountAsync(ct);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default)
        => await db.Users.OrderBy(u => u.NormalizedUsername).ToListAsync(ct);

    public async Task<User?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, ct);

        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(new User(), DummyHash, password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed || user.IsDisabled)
            return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    public async Task<(User? User, string[] Errors)> CreateLocalAsync(
        string username, string? displayName, string password, UserRole role, CancellationToken ct = default)
    {
        username = username.Trim();
        var errors = new List<string>();

        if (!UsernamePattern().IsMatch(username))
            errors.Add("Username must be 3-64 characters: letters, digits and . _ @ + -");
        errors.AddRange(ValidatePassword(password));

        if (errors.Count == 0 && await db.Users.AnyAsync(u => u.NormalizedUsername == Normalize(username), ct))
            errors.Add("A user with this username already exists.");

        if (errors.Count > 0)
            return (null, [.. errors]);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = Normalize(username),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return (user, []);
    }

    public async Task<string[]> SetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        var errors = ValidatePassword(newPassword);
        if (errors.Length > 0)
            return errors;

        var user = await FindByIdAsync(id, ct);
        if (user is null)
            return ["User not found."];

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync(ct);
        return [];
    }

    public async Task<string[]> ChangePasswordAsync(
        Guid id, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await FindByIdAsync(id, ct);
        if (user?.PasswordHash is null)
            return ["This account has no local password."];

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return ["The current password is incorrect."];

        return await SetPasswordAsync(id, newPassword, ct);
    }

    public async Task<string[]> SetRoleAsync(Guid id, UserRole role, CancellationToken ct = default)
    {
        var user = await FindByIdAsync(id, ct);
        if (user is null)
            return ["User not found."];

        if (role != UserRole.Admin && await IsLastActiveAdminAsync(user, ct))
            return ["At least one active administrator is required."];

        user.Role = role;
        await db.SaveChangesAsync(ct);
        return [];
    }

    public async Task<string[]> SetDisabledAsync(Guid id, bool disabled, CancellationToken ct = default)
    {
        var user = await FindByIdAsync(id, ct);
        if (user is null)
            return ["User not found."];

        if (disabled && await IsLastActiveAdminAsync(user, ct))
            return ["At least one active administrator is required."];

        user.IsDisabled = disabled;
        await db.SaveChangesAsync(ct);
        return [];
    }

    /// <summary>
    /// Finds or creates the user behind an OpenID Connect identity and applies the role mapping.
    /// </summary>
    public async Task<User> ProvisionExternalAsync(
        string issuer, string subject, string? preferredUsername, string? displayName,
        IReadOnlyCollection<string> groups, OidcOptions options, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ExternalIssuer == issuer && u.ExternalSubject == subject, ct);

        var role = OidcRoleMapper.Resolve(options, groups, user?.Role);

        if (user is null)
        {
            var username = await PickUsernameAsync(preferredUsername, subject, ct);
            user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                NormalizedUsername = Normalize(username),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
                ExternalIssuer = issuer,
                ExternalSubject = subject,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Users.Add(user);
        }
        else if (!string.IsNullOrWhiteSpace(displayName))
        {
            user.DisplayName = displayName.Trim();
        }

        user.Role = role;
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<string> PickUsernameAsync(string? preferred, string subject, CancellationToken ct)
    {
        var candidate = string.IsNullOrWhiteSpace(preferred) ? subject : preferred.Trim();
        if (candidate.Length > 64)
            candidate = candidate[..64];

        if (!await db.Users.AnyAsync(u => u.NormalizedUsername == Normalize(candidate), ct))
            return candidate;

        var suffix = "-" + Guid.NewGuid().ToString("N")[..6];
        return candidate[..Math.Min(candidate.Length, 64 - suffix.Length)] + suffix;
    }

    private async Task<bool> IsLastActiveAdminAsync(User user, CancellationToken ct)
    {
        if (user.Role != UserRole.Admin || user.IsDisabled)
            return false;

        return !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && !u.IsDisabled, ct);
    }

    private static string[] ValidatePassword(string password)
        => password is { Length: >= MinPasswordLength }
            ? []
            : [$"Password must be at least {MinPasswordLength} characters."];
}
