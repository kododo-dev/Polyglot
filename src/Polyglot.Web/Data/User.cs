namespace Kododo.Polyglot.Web.Data;

public enum UserRole
{
    /// <summary>Signed in but not allowed to do anything (e.g. an OIDC user outside the configured groups).</summary>
    None = 0,
    Editor = 1,
    Admin = 2,
}

public sealed class User
{
    public Guid Id { get; set; }

    public string Username { get; set; } = "";

    /// <summary>Upper-invariant <see cref="Username"/>, used for the unique index and lookups.</summary>
    public string NormalizedUsername { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Null for users who sign in only through OpenID Connect.</summary>
    public string? PasswordHash { get; set; }

    public UserRole Role { get; set; }

    public bool IsDisabled { get; set; }

    public string? ExternalIssuer { get; set; }

    public string? ExternalSubject { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
