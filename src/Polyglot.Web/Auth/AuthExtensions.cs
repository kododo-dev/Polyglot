using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.Auth;

public static class PolyglotPolicies
{
    public const string Editor = "Editor";
    public const string Admin = "Admin";
}

public static class PrincipalFactory
{
    public static ClaimsPrincipal Create(User user, string scheme)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("display_name", user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            scheme, ClaimTypes.Name, ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }
}

public static class AuthExtensions
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string OidcScheme = "oidc";
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddPolyglotAuth(
        this IServiceCollection services, string connectionString, AuthOptions auth, string editorPath)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(
            connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", AppDbContext.Schema)));

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<UserService>();

        // Keys live in the database so that cookies survive restarts and work across replicas.
        services.AddDataProtection()
            .SetApplicationName("Polyglot")
            .PersistKeysToDbContext<AppDbContext>();

        services.AddRazorPages();

        var authentication = services.AddAuthentication(CookieScheme);
        authentication.AddCookie(CookieScheme, o =>
        {
            o.LoginPath = "/login";
            o.AccessDeniedPath = "/forbidden";
            o.Cookie.Name = "polyglot.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            o.SlidingExpiration = true;

            // The editor's fetch calls need a status code, not a redirect to an HTML page.
            o.Events.OnRedirectToLogin = ctx => RedirectOrStatus(ctx, editorPath, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => RedirectOrStatus(ctx, editorPath, StatusCodes.Status403Forbidden);

            // Role changes and disabling a user take effect on the next request.
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var idClaim = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
                var user = Guid.TryParse(idClaim, out var id)
                    ? await users.FindByIdAsync(id, ctx.HttpContext.RequestAborted)
                    : null;

                if (user is null || user.IsDisabled)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieScheme);
                    return;
                }

                if (ctx.Principal?.FindFirstValue(ClaimTypes.Role) != user.Role.ToString())
                {
                    ctx.ReplacePrincipal(PrincipalFactory.Create(user, CookieScheme));
                    ctx.ShouldRenew = true;
                }
            };
        });

        if (auth.Oidc.IsConfigured)
            authentication.AddOpenIdConnect(OidcScheme, auth.Oidc.DisplayName, o => ConfigureOidc(o, auth.Oidc));

        services.AddAuthorizationBuilder()
            .AddPolicy(PolyglotPolicies.Editor, p => p.RequireRole(nameof(UserRole.Editor), nameof(UserRole.Admin)))
            .AddPolicy(PolyglotPolicies.Admin, p => p.RequireRole(nameof(UserRole.Admin)));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, ctx => ctx.Request.Method == HttpMethods.Post
                ? RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = auth.LoginAttemptsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter("get"));
        });

        return services;
    }

    /// <summary>Applies migrations and creates the first administrator when the instance has no users.</summary>
    public static async Task InitializeAuthAsync(this WebApplication app, AuthOptions auth)
    {
        await using var scope = app.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        if (!auth.Local.Enabled || await users.CountAsync() > 0)
            return;

        var generated = string.IsNullOrWhiteSpace(auth.Bootstrap.Password);
        var password = generated
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_')
            : auth.Bootstrap.Password!;

        var (admin, errors) = await users.CreateLocalAsync(
            auth.Bootstrap.Username, null, password, UserRole.Admin);

        if (admin is null)
            throw new InvalidOperationException(
                "Could not create the initial administrator: " + string.Join(" ", errors));

        if (generated)
            app.Logger.LogWarning(
                "Created the initial administrator '{Username}' with the generated password: {Password} " +
                "(shown once, change it after signing in)",
                admin.Username, password);
        else
            app.Logger.LogInformation("Created the initial administrator '{Username}'.", admin.Username);
    }

    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, string editorPath, int status)
    {
        if (ctx.Request.Path.StartsWithSegments($"{editorPath}/api"))
            ctx.Response.StatusCode = status;
        else
            ctx.Response.Redirect(ctx.RedirectUri);

        return Task.CompletedTask;
    }

    private static void ConfigureOidc(OpenIdConnectOptions o, OidcOptions oidc)
    {
        o.Authority = oidc.Authority;
        o.ClientId = oidc.ClientId;
        o.ClientSecret = oidc.ClientSecret;
        o.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
        o.ResponseType = "code";
        o.UsePkce = true;
        o.SaveTokens = false;
        o.MapInboundClaims = false;
        o.SignInScheme = CookieScheme;

        o.Scope.Clear();
        foreach (var scope in new[] { "openid", "profile", "email" }
                     .Concat(oidc.ExtraScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            o.Scope.Add(scope);

        o.Events.OnTokenValidated = async ctx =>
        {
            var principal = ctx.Principal!;
            var subject = principal.FindFirstValue("sub");
            if (subject is null)
            {
                ctx.Fail("The identity provider did not return a subject.");
                return;
            }

            var issuer = principal.FindFirstValue("iss") ?? oidc.Authority!;
            var groups = principal.FindAll(oidc.GroupsClaim).Select(c => c.Value).ToList();

            var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
            var user = await users.ProvisionExternalAsync(
                issuer, subject,
                principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue("email"),
                principal.FindFirstValue("name"),
                groups, oidc, ctx.HttpContext.RequestAborted);

            if (user.IsDisabled)
            {
                ctx.Fail("This account is disabled.");
                return;
            }

            // From here on the identity is the local user, not the raw token claims.
            ctx.Principal = PrincipalFactory.Create(user, CookieScheme);
        };

        o.Events.OnRemoteFailure = ctx =>
        {
            ctx.Response.Redirect($"{ctx.Request.PathBase}/login?error=sso");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    }
}
