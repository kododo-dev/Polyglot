using System.Security.Claims;
using Kododo.CultureWay.UI;
using Kododo.Polyglot.Web.Data;
using Kododo.Way.Shell;

namespace Kododo.Polyglot.Web;

/// <summary>
/// Makes the CultureWay editor part of Polyglot: the shell around it (title, menu links, signed-in user), who may add or
/// delete languages, and how large an import may be.
/// </summary>
public static class EditorIntegration
{
    /// <summary>The largest file a visitor of the public demo may import.</summary>
    public const long DemoMaxImportBytes = 100 * 1024;

    /// <summary>The editor runs in Way.Shell, which shows Polyglot's title, links and user around it.</summary>
    public static void ConfigureShell(ShellOptions shell, PolyglotOptions polyglot, bool authEnabled)
    {
        shell.Title = "Polyglot";
        shell.HomeUrl = "~/";
        if (!authEnabled)
            return;

        shell.DynamicLinks.Add(ctx => Links(ctx, polyglot));
        shell.User = ctx => ctx.User.Identity?.IsAuthenticated == true
            ? new ShellUser(ctx.User.FindFirstValue("display_name") ?? ctx.User.Identity.Name ?? "")
            {
                AccountUrl = "~/account",
                SignOutUrl = "~/logout",
            }
            : null;
    }

    public static void ConfigureEditor(EditorOptions editor, bool authEnabled, bool demoEnabled = false)
    {
        // Everyone who may open the editor (editors and admins) may import; the demo keeps files small.
        if (demoEnabled)
            editor.MaxImportBytes = DemoMaxImportBytes;
        if (!authEnabled)
            return;

        // Languages decide what every app gets from the API, so only admins change them.
        editor.CanManageCultures = ctx => ctx.User.IsInRole(nameof(UserRole.Admin));
    }

    /// <summary>
    /// The side menu's links, shared by the editor and Polyglot's own pages so both list the same
    /// pages in the same order.
    /// </summary>
    public static IReadOnlyList<ShellLink> Links(HttpContext ctx, PolyglotOptions polyglot)
    {
        var isAdmin = ctx.User.IsInRole(nameof(UserRole.Admin));
        var isEditor = ctx.User.IsInRole(nameof(UserRole.Editor));
        var pathBase = ctx.Request.PathBase;
        var links = new List<ShellLink>();

        // Editors are sent from the overview straight to the editor, so only admins get the link.
        if (isAdmin)
            links.Add(new ShellLink("Overview", $"{pathBase}/") { Icon = EditorLinkIcons.Home });
        if (isAdmin || isEditor)
        {
            links.Add(new ShellLink("Translations", $"{pathBase}{polyglot.GetEditorPath()}/") { Icon = EditorLinkIcons.Translations });
            links.Add(new ShellLink("History", $"{pathBase}/history") { Icon = EditorLinkIcons.History });
        }
        if (isAdmin)
        {
            links.Add(new ShellLink("Users", $"{pathBase}/admin/users") { Icon = EditorLinkIcons.Users });
            links.Add(new ShellLink("API keys", $"{pathBase}/admin/api-keys") { Icon = EditorLinkIcons.Key });
        }

        return links;
    }
}
