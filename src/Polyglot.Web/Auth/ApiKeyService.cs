using System.Security.Cryptography;
using System.Text;
using Kododo.Polyglot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.Auth;

/// <summary>
/// Issues and verifies the API keys that authenticate machine callers of the delivery API.
/// A token looks like <c>pg_&lt;keyId&gt;_&lt;secret&gt;</c> and is returned only once, from
/// <see cref="CreateAsync"/>; the database keeps nothing but the hash of its secret part.
/// </summary>
public sealed class ApiKeyService(AppDbContext db)
{
    /// <summary>Read every translation in the instance. The only scope so far.</summary>
    public const string ReadScope = "translations:read";

    public const string TokenPrefix = "pg_";

    public const int MaxNameLength = 200;

    private const int KeyIdLength = 8;

    /// <summary>43 characters out of 62 carry about 256 bits, the same as the 32 random bytes this replaced.</summary>
    private const int SecretLength = 43;

    /// <summary>Lower case only: a key identifier is read off a screen or out of a log, where case is easy to lose.</summary>
    private const string KeyIdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    // Letters and digits only. Base64url would have been the obvious encoding, but its '_' collides
    // with the separator in pg_<keyId>_<secret>, and every consumer splitting a token on '_' would
    // then break on half the keys.
    private const string SecretAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>How stale <see cref="ApiKey.LastUsedAt"/> may get before a request writes it again.</summary>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    public Task<ApiKey?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken ct = default)
        => await db.ApiKeys.OrderBy(k => k.Name).ThenBy(k => k.CreatedAt).ToListAsync(ct);

    /// <summary>
    /// Issues a key. The returned token is the only copy that will ever exist. Show it once and
    /// forget it.
    /// </summary>
    public async Task<(ApiKey? Key, string Token, string[] Errors)> CreateAsync(
        string name, Guid? createdByUserId = null, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        name = name.Trim();
        var now = DateTimeOffset.UtcNow;
        var errors = new List<string>();

        if (name.Length == 0)
            errors.Add("Name must not be empty.");
        else if (name.Length > MaxNameLength)
            errors.Add($"Name must be at most {MaxNameLength} characters.");

        if (expiresAt is not null && expiresAt <= now)
            errors.Add("The expiry date must be in the future.");

        if (errors.Count > 0)
            return (null, "", [.. errors]);

        var keyId = await PickKeyIdAsync(ct);
        var secret = new string(RandomNumberGenerator.GetItems<char>(SecretAlphabet, SecretLength));

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            SecretHash = HashSecret(secret),
            Name = name,
            Scopes = ReadScope,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return (key, TokenPrefix + keyId + "_" + secret, []);
    }

    /// <summary>
    /// Registers a key whose token the caller already has, such as the demo's published one. The
    /// secret was not generated here, so use this only for tokens that are meant to be public.
    /// </summary>
    public async Task<ApiKey> RegisterAsync(
        string name, string token, Guid? createdByUserId = null, CancellationToken ct = default)
    {
        if (!TryParse(token, out var keyId, out var secret))
            throw new ArgumentException("Not a valid API key token.", nameof(token));

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            SecretHash = HashSecret(secret),
            Name = name.Trim(),
            Scopes = ReadScope,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return key;
    }

    public Task<ApiKey?> ValidateAsync(string token, CancellationToken ct = default)
        => ValidateAsync(token, DateTimeOffset.UtcNow, ct);

    /// <summary>
    /// Returns the key the token belongs to, or null when the token is malformed, unknown, tampered
    /// with, disabled or expired. The caller is not told which.
    /// </summary>
    /// <param name="now">The instant the request counts as happening at, for expiry and last use.</param>
    public async Task<ApiKey?> ValidateAsync(string token, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!TryParse(token, out var keyId, out var secret))
            return null;

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyId == keyId, ct);
        if (key is null)
            return null;

        if (!CryptographicOperations.FixedTimeEquals(key.SecretHash, HashSecret(secret)))
            return null;

        if (!key.IsActive(now))
            return null;

        if (key.LastUsedAt is null || now - key.LastUsedAt.Value >= LastUsedPrecision)
        {
            key.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return key;
    }

    /// <summary>Splits a token into its public identifier and its secret, without touching the database.</summary>
    public static bool TryParse(string? token, out string keyId, out string secret)
    {
        keyId = "";
        secret = "";

        if (string.IsNullOrEmpty(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return false;

        // Parsed by position rather than by splitting, so the separator cannot be confused with the
        // token's own characters.
        var body = token[TokenPrefix.Length..];
        if (body.Length != KeyIdLength + 1 + SecretLength || body[KeyIdLength] != '_')
            return false;

        var candidateId = body[..KeyIdLength];
        var candidateSecret = body[(KeyIdLength + 1)..];

        if (!candidateId.All(KeyIdAlphabet.Contains) || !candidateSecret.All(char.IsAsciiLetterOrDigit))
            return false;

        keyId = candidateId;
        secret = candidateSecret;
        return true;
    }

    public async Task<string[]> SetDisabledAsync(Guid id, bool disabled, CancellationToken ct = default)
    {
        var key = await FindByIdAsync(id, ct);
        if (key is null)
            return ["API key not found."];

        key.IsDisabled = disabled;
        await db.SaveChangesAsync(ct);
        return [];
    }

    public async Task<string[]> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var key = await FindByIdAsync(id, ct);
        if (key is null)
            return ["API key not found."];

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync(ct);
        return [];
    }

    private async Task<string> PickKeyIdAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = new string(RandomNumberGenerator.GetItems<char>(KeyIdAlphabet, KeyIdLength));
            if (!await db.ApiKeys.AnyAsync(k => k.KeyId == candidate, ct))
                return candidate;
        }

        throw new InvalidOperationException("Could not generate a unique API key identifier.");
    }

    // The secret carries about 256 random bits, so a single SHA-256 is enough. There is nothing to
    // brute-force that a password KDF would slow down.
    private static byte[] HashSecret(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
