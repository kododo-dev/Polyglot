using Microsoft.Extensions.Primitives;

namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// Answers with a payload and its version as a strong ETag, or with an empty 304 when the caller
/// already holds that version. <c>no-cache</c> rather than a freshness window: a shared proxy has to
/// revalidate, and the cheap path is the 304, not a guess about how stale a translation may be.
/// </summary>
internal static class ConditionalResponse
{
    public static IResult Json<T>(HttpContext http, string version, T payload)
    {
        var etag = $"\"{version}\"";

        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.ETag = etag;

        return Matches(http.Request.Headers.IfNoneMatch, etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Json(payload);
    }

    // A weak validator (W/"...") never matches: the delivery API only ever issues strong tags.
    private static bool Matches(StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (string.IsNullOrWhiteSpace(header))
                continue;

            foreach (var candidate in header.Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || trimmed == etag)
                    return true;
            }
        }

        return false;
    }
}
