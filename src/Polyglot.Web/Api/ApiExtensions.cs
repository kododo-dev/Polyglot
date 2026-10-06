using System.ComponentModel;
using System.Globalization;
using System.Threading.RateLimiting;
using Kododo.CultureWay;
using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TranslationSnapshots>();

        if (api.OpenApi.Enabled)
            services.AddPolyglotOpenApi();

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
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags(ApiOpenApi.Tag)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/cultures", (HttpContext http, CultureWayOptions cultures) =>
            {
                // The same source the editor reads, so the two can never disagree, and it already
                // carries the cultures persisted by the store plus any added while running.
                var supported = cultures.SupportedCultures
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var version = ContentVersion.Of([cultures.DefaultCulture, .. supported]);

                return ConditionalResponse.Json(
                    http, version, new CulturesResponse(cultures.DefaultCulture, supported));
            })
            .WithName("getCultures")
            .WithSummary("The cultures this instance serves")
            .Produces<CulturesResponse>()
            .Produces(StatusCodes.Status304NotModified);

        group.MapGet("/translations/{culture}", async (
                HttpContext http,
                [Description("A culture code such as pl or pl-PL.")] string culture,
                [FromQuery(Name = "namespace")]
                [Description("Narrows the response to keys in this namespace or under it; repeatable, the union is returned.")]
                string[]? namespaces,
                CultureWayOptions cultures,
                TranslationSnapshots snapshots) =>
            {
                if (!TryLineage(culture, out var lineage))
                    return Problem(
                        StatusCodes.Status400BadRequest, ApiProblems.InvalidRequest, "Invalid culture",
                        $"'{culture}' is not a culture code.");

                var filters = (namespaces ?? [])
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                var invalid = filters.FirstOrDefault(ns => !TranslationNamespaces.IsValid(ns));
                if (invalid is not null)
                    return Problem(
                        StatusCodes.Status400BadRequest, ApiProblems.InvalidRequest, "Invalid namespace",
                        $"'{invalid}' is not a namespace: it is empty or has an empty segment.");

                // Only the culture and its parents count, not the default culture that closes every
                // chain: answering an unknown culture with it would turn a typo into silently
                // default-language pages.
                var supported = new HashSet<string>(cultures.SupportedCultures, StringComparer.OrdinalIgnoreCase);
                if (!lineage.Any(supported.Contains))
                    return Problem(
                        StatusCodes.Status404NotFound, ApiProblems.NotFound, "Unsupported culture",
                        $"Neither '{culture}' nor any of its parent cultures is served by this instance.");

                var snapshot = await snapshots.GetAsync(http.RequestAborted);
                var resolved = snapshot.Resolve(FallbackChain(lineage, cultures.DefaultCulture), filters);

                return ConditionalResponse.Json(http, resolved.Version, resolved.Translations);
            })
            .WithName("getTranslations")
            .WithSummary("The translations of one culture")
            .WithDescription(
                "Every key resolved along the culture's fallback chain (the culture, its parents, then the " +
                "default culture), optionally narrowed to namespaces. A flat map of full keys to values.")
            .Produces<IReadOnlyDictionary<string, string>>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    // The culture and its parents, most specific first: pl-PL, pl.
    private static bool TryLineage(string culture, out string[] lineage)
    {
        lineage = [];

        CultureInfo current;
        try
        {
            current = CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        var names = new List<string>();
        while (!string.IsNullOrEmpty(current.Name))
        {
            names.Add(current.Name);
            current = current.Parent;
        }

        lineage = [.. names];
        return lineage.Length > 0;
    }

    // CultureWay's own fallback chain, as its IStringLocalizer walks it: the culture, its parents,
    // then the default culture.
    private static string[] FallbackChain(string[] lineage, string defaultCulture)
        => lineage.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase)
            ? lineage
            : [.. lineage, defaultCulture];

    private static IResult Problem(int statusCode, string type, string title, string detail)
        => Results.Problem(detail: detail, statusCode: statusCode, title: title, type: type);

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
