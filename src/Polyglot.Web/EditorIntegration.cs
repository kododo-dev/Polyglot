using System.Security.Claims;
using Kododo.CultureWay.UI;
using Kododo.Polyglot.Web.Data;

namespace Kododo.Polyglot.Web;

/// <summary>
/// Makes the CultureWay editor part of Polyglot: its title, the menu links, the signed-in user, and
/// who may add or delete languages.
/// </summary>
public static class EditorIntegration
{
    public static void Configure(EditorOptions editor, PolyglotOptions polyglot, bool authEnabled)
    {
        editor.Title = "Polyglot";
        editor.HomeUrl = string.IsNullOrWhiteSpace(polyglot.PathBase) ? "/" : $"/{polyglot.PathBase.Trim('/')}/";
        if (!authEnabled)
            return;

        editor.Links = ctx => Links(ctx, polyglot);
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

    /// <summary>
    /// The side menu's links, shared by the editor and Polyglot's own pages so both list the same
    /// pages in the same order.
    /// </summary>
    public static IReadOnlyList<EditorLink> Links(HttpContext ctx, PolyglotOptions polyglot)
    {
        var isAdmin = ctx.User.IsInRole(nameof(UserRole.Admin));
        var isEditor = ctx.User.IsInRole(nameof(UserRole.Editor));
        var pathBase = ctx.Request.PathBase;
        var links = new List<EditorLink>();

        // Editors are sent from the overview straight to the editor, so only admins get the link.
        if (isAdmin)
            links.Add(new EditorLink("Overview", $"{pathBase}/") { Icon = EditorLinkIcons.Home });
        if (isAdmin || isEditor)
        {
            links.Add(new EditorLink("Translations", $"{pathBase}{polyglot.GetEditorPath()}/") { Icon = EditorLinkIcons.Translations });
            links.Add(new EditorLink("History", $"{pathBase}/history") { Icon = EditorLinkIcons.History });
        }
        if (isAdmin)
        {
            links.Add(new EditorLink("Users", $"{pathBase}/admin/users") { Icon = EditorLinkIcons.Users });
            links.Add(new EditorLink("API keys", $"{pathBase}/admin/api-keys") { Icon = EditorLinkIcons.Key });
        }

        return links;
    }
}
