using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// The problem types the delivery API reports (RFC 9457). URNs rather than URLs: they are meant to be
/// compared by a client, not fetched, and a documentation link would rot.
/// </summary>
public static class ApiProblems
{
    public const string InvalidRequest = "urn:polyglot:error:invalid-request";
    public const string Unauthorized = "urn:polyglot:error:unauthorized";
    public const string Forbidden = "urn:polyglot:error:forbidden";
    public const string NotFound = "urn:polyglot:error:not-found";
    public const string RateLimited = "urn:polyglot:error:rate-limited";

    public static async Task WriteAsync(
        HttpContext http, int statusCode, string type, string title, string? detail = null)
    {
        http.Response.StatusCode = statusCode;

        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Type = type,
                Title = title,
                Detail = detail,
            },
        });
    }
}
