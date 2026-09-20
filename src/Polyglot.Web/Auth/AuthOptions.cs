using Kododo.Polyglot.Web.Data;

namespace Kododo.Polyglot.Web.Auth;

/// <summary>Authentication settings, read from <c>Polyglot:Auth</c> (env prefix <c>Polyglot__Auth__</c>).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Polyglot:Auth";

    /// <summary>
    /// When false the editor is open to anyone who can reach it and no database is needed for auth.
    /// Only meant for setups protected some other way (private network, reverse-proxy auth).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Failed and successful sign-in attempts allowed per client address per minute.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    public LocalOptions Local { get; set; } = new();

    public OidcOptions Oidc { get; set; } = new();

    public BootstrapOptions Bootstrap { get; set; } = new();

    public void Validate(string? connectionString)
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Authentication needs a database. Set ConnectionStrings__Default, " +
                "or set Polyglot__Auth__Enabled=false to run without authentication.");

        if (!Local.Enabled && !Oidc.IsConfigured)
            throw new InvalidOperationException(
                "Local sign-in is disabled but OpenID Connect is not configured " +
                "(Polyglot__Auth__Oidc__Authority and Polyglot__Auth__Oidc__ClientId).");

        if (!Local.Enabled && string.IsNullOrWhiteSpace(Oidc.AdminGroup))
            throw new InvalidOperationException(
                "With local sign-in disabled, Polyglot__Auth__Oidc__AdminGroup must be set " +
                "so that at least one OpenID Connect user can administer the instance.");
    }
}

public sealed class LocalOptions
{
    /// <summary>Username and password sign-in.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class BootstrapOptions
{
    /// <summary>Username of the administrator created when the instance has no users yet.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>Password of that administrator. When empty a random one is generated and logged once.</summary>
    public string? Password { get; set; }
}

public sealed class OidcOptions
{
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Label of the sign-in button.</summary>
    public string DisplayName { get; set; } = "Single sign-on";

    /// <summary>Comma-separated scopes requested in addition to <c>openid profile email</c>.</summary>
    public string ExtraScopes { get; set; } = "";

    /// <summary>Set to false only for a local identity provider without TLS.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Claim that carries the user's groups.</summary>
    public string GroupsClaim { get; set; } = "groups";

    /// <summary>Members of this group become administrators.</summary>
    public string? AdminGroup { get; set; }

    /// <summary>Members of this group become editors.</summary>
    public string? EditorGroup { get; set; }

    /// <summary>
    /// Role of a newly seen user when no group mapping is configured. Afterwards the role is managed
    /// on the Users page.
    /// </summary>
    public UserRole DefaultRole { get; set; } = UserRole.Editor;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);

    public bool HasGroupMapping =>
        !string.IsNullOrWhiteSpace(AdminGroup) || !string.IsNullOrWhiteSpace(EditorGroup);
}
