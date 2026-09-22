using System.Globalization;
using System.Threading.RateLimiting;
using Kododo.CultureWay;
using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;

namespace Kododo.Polyglot.Web.Api;

public static class ApiPolicies
{
    /// <summary>Reading translations: an API key with the read scope, and nothing else.</summary>
    public const string Read = "ApiRead";
}

public static class ApiExtensions
{
    public const string ApiKeyScheme = "ApiKey";

    public const string RateLimitPolicy = "api";

    public const string BasePath = "/api/v1";

    public static IServiceCollection AddPolyglotApi(this IServiceCollection services, ApiOptions api)
    {
        services.AddProblemDetails();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyScheme, null);

        services.AddAuthorizationBuilder()
            .AddPolicy(ApiPolicies.Read, p => p
                .AddAuthenticationSchemes(ApiKeyScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ApiKeyAuthenticationHandler.ScopeClaimType, ApiKeyService.ReadScope));

        services.AddRateLimiter(o =>
        {
            o.AddPolicy(RateLimitPolicy, ctx => Partition(ctx, api.RequestsPerMinute));

            // Only the delivery API answers in problem+json; a rejected form post stays a bare 429.
            o.OnRejected = async (ctx, ct) =>
            {
                if (!ctx.HttpContext.Request.Path.StartsWithSegments(BasePath))
                    return;

                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

                await ApiProblems.WriteAsync(
                    ctx.HttpContext, StatusCodes.Status429TooManyRequests, ApiProblems.RateLimited,
                    "Too many requests", "Slow down, or ask an administrator to raise the limit for this key.");
            };
        });

        return services;
    }

    /// <summary>Maps the read-only delivery API. Requires <see cref="AuthExtensions.AddPolyglotAuth"/>.</summary>
    public static RouteGroupBuilder MapPolyglotApi(this WebApplication app)
    {
        var group = app.MapGroup(BasePath)
            .RequireAuthorization(ApiPolicies.Read)
            .RequireRateLimiting(RateLimitPolicy);

        group.MapGet("/cultures", (HttpContext http, CultureWayOptions cultures) =>
            {
                // The same source the editor reads, so the two can never disagree, and it already
                // carries the cultures persisted by the store plus any added while running.
                var supported = cultures.SupportedCultures
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var version = ContentVersion.Of([cultures.DefaultCulture, .. supported]);

                return ConditionalResponse.Json(
                    http, version, new CulturesResponse(version, cultures.DefaultCulture, supported));
            })
            .WithName("getCultures");

        return group;
    }

    // Partitioned by the key identifier taken straight from the header rather than from the
    // authenticated principal, so that a flood of invalid keys is bounded as well.
    private static RateLimitPartition<string> Partition(HttpContext ctx, int requestsPerMinute)
    {
        var token = ctx.Request.Headers[ApiKeyAuthenticationHandler.HeaderName].ToString();

        var partition = ApiKeyService.TryParse(token, out var keyId, out _)
            ? "key:" + keyId
            : "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = requestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
