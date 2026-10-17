using System.Security.Claims;
using Kododo.CultureWay.UI;
using Kododo.Polyglot.Web.Data;
using Kododo.Polyglot.Web.Demo;
using Kododo.Way.Shell;

namespace Kododo.Polyglot.Web;

/// <summary>
/// Makes the CultureWay editor part of Polyglot: the shell around every page (title, demo notice, signed-in user), who
/// may add or delete languages, and how large an import may be.
/// </summary>
public static class EditorIntegration
{
    /// <summary>The largest file a visitor of the public demo may import.</summary>
    public const long DemoMaxImportBytes = 100 * 1024;

    /// <summary>Polyglot's pages and the editor are Ways of one panel, which shows Polyglot's title and user around them.</summary>
    public static void ConfigureShell(ShellOptions shell, bool authEnabled, DemoOptions demo)
    {
        shell.Title = "Polyglot";
        shell.HomeUrl = "~/";
        if (demo.Enabled)
            shell.Notice = demo.Notice;
        if (!authEnabled)
            return;

        shell.User = ctx => ctx.User.Identity?.IsAuthenticated == true
            ? new ShellUser(ctx.User.FindFirstValue("display_name") ?? ctx.User.Identity.Name ?? "")
            {
                // The account page is a Way of the panel without an item in its menu.
                AccountUrl = "~/#/account/",
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
}
