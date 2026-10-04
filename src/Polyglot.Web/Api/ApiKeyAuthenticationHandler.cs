using System.Security.Claims;
using System.Text.Encodings.Web;
using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// Authenticates machine callers by the <c>X-Api-Key</c> header. A scheme of its own on purpose: the
/// sign-in cookie must never open the delivery API, and an API key must never open the editor or the
/// admin pages.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyService keys) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string HeaderName = "X-Api-Key";

    public const string ScopeClaimType = "scope";

    public const string KeyIdClaimType = "api_key_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var key = await keys.ValidateAsync(token, Context.RequestAborted);
        if (key is null)
        {
            // Administrators get to see which key was refused; the caller is only told it failed.
            Logger.LogInformation(
                "Refused an API request: the key {KeyId} is unknown, disabled, expired or its secret does not match.",
                ApiKeyService.TryParse(token, out var keyId, out _) ? keyId : "(malformed)");

            return AuthenticateResult.Fail("The API key is not valid.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new(ClaimTypes.Name, key.Name),
            new(KeyIdClaimType, key.KeyId),
        };

        claims.AddRange(key.Scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(scope => new Claim(ScopeClaimType, scope)));

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => ApiProblems.WriteAsync(
            Context, StatusCodes.Status401Unauthorized, ApiProblems.Unauthorized, "Unauthorized",
            $"Send a valid API key in the {HeaderName} header.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => ApiProblems.WriteAsync(
            Context, StatusCodes.Status403Forbidden, ApiProblems.Forbidden, "Forbidden",
            "This API key does not have the scope this endpoint requires.");
}
