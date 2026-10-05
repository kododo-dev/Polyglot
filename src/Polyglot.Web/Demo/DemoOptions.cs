using Kododo.Polyglot.Web.Auth;

namespace Kododo.Polyglot.Web.Demo;

/// <summary>
/// Public demo settings, read from <c>Polyglot:Demo</c> (env prefix <c>Polyglot__Demo__</c>). A demo
/// instance seeds sample data, lets anyone sign in as the demo administrator, and turns off what
/// would spoil the demo for the next visitor: password changes and user management.
/// </summary>
/// <remarks>Resetting the data is up to whoever runs the instance, e.g. a database that is recreated daily.</remarks>
public sealed class DemoOptions
{
    public const string SectionName = "Polyglot:Demo";

    /// <summary>
    /// A valid token, but public by design: it is shown on the demo's pages so visitors can call the
    /// delivery API.
    /// </summary>
    public const string DefaultApiKey = "pg_demo0key_PublicDemoKeyOfPolyglotDoNotUseInProduction";

    public bool Enabled { get; set; }

    /// <summary>The demo administrator, created as the instance's first user.</summary>
    public string Username { get; set; } = "demo";

    /// <summary>Shown on the sign-in page, so it is not a secret either.</summary>
    public string Password { get; set; } = "polyglot-demo";

    /// <summary>Token of the API key the demo seeds and shows.</summary>
    public string ApiKey { get; set; } = DefaultApiKey;

    /// <summary>Shown at the top of every page. Say here how often the data is reset.</summary>
    public string Notice { get; set; } =
        "This is a public demo. Everything you change is visible to other visitors, and the data is reset regularly.";

    /// <summary>Whether this is the key the demo shows, which visitors may not disable or delete.</summary>
    public bool IsDemoKey(Data.ApiKey key)
        => Enabled && ApiKeyService.TryParse(ApiKey, out var keyId, out _) && key.KeyId == keyId;

    /// <summary>Makes the demo administrator the user the instance creates first.</summary>
    public void Apply(AuthOptions auth)
    {
        if (!Enabled)
            return;

        auth.Bootstrap.Username = Username;
        auth.Bootstrap.Password = Password;
    }

    public void Validate(AuthOptions auth)
    {
        if (!Enabled)
            return;

        if (!auth.Enabled || !auth.Local.Enabled)
            throw new InvalidOperationException(
                "The demo needs authentication with local sign-in (Polyglot__Auth__Enabled and " +
                "Polyglot__Auth__Local__Enabled), since visitors sign in as the demo user.");

        if (Password.Length < UserService.MinPasswordLength)
            throw new InvalidOperationException(
                $"Polyglot__Demo__Password must be at least {UserService.MinPasswordLength} characters.");

        if (!ApiKeyService.TryParse(ApiKey, out _, out _))
            throw new InvalidOperationException(
                "Polyglot__Demo__ApiKey is not a valid API key token (pg_<8 lower-case letters or digits>_<43 letters or digits>).");
    }
}
