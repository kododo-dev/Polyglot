using System.Security.Claims;
using Kododo.CultureWay.UI;
using Kododo.Polyglot.Web.Data;

namespace Kododo.Polyglot.Web;

/// <summary>
/// Makes the CultureWay editor part of Polyglot: its title, links to the admin pages, the signed-in
/// user, and who may add or delete languages.
/// </summary>
public static class EditorIntegration
{
    public static void Configure(EditorOptions editor, PolyglotOptions polyglot, bool authEnabled)
    {
        editor.Title = "Polyglot";
        editor.HomeUrl = string.IsNullOrWhiteSpace(polyglot.PathBase) ? "/" : $"/{polyglot.PathBase.Trim('/')}/";
        if (!authEnabled)
            return;

        editor.Links = Links;
        editor.User = ctx => ctx.User.Identity?.IsAuthenticated == true
            ? new EditorUser(ctx.User.FindFirstValue("display_name") ?? ctx.User.Identity.Name ?? "")
            {
                AccountUrl = $"{ctx.Request.PathBase}/account",
                SignOutUrl = $"{ctx.Request.PathBase}/logout",
            }
            : null;
        // Languages decide what every app gets from the API, so only admins change them.
        editor.CanManageCultures = ctx => ctx.User.IsInRole(nameof(UserRole.Admin));
    }

    private static IEnumerable<EditorLink> Links(HttpContext ctx)
    {
        if (!ctx.User.IsInRole(nameof(UserRole.Admin)))
            yield break;

        var pathBase = ctx.Request.PathBase;
        yield return new EditorLink("Users", $"{pathBase}/admin/users");
        yield return new EditorLink("API keys", $"{pathBase}/admin/api-keys");
    }
}
