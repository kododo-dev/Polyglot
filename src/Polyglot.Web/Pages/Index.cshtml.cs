using Kododo.CultureWay;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kododo.Polyglot.Web.Pages;

[Authorize]
public class IndexModel(
    PolyglotOptions polyglot,
    CultureWayOptions cultureWay,
    ApiOptions api,
    UserService users,
    ApiKeyService keys) : PageModel
{
    public string EditorUrl => $"{Request.PathBase}{polyglot.GetEditorPath()}/";

    public IReadOnlyList<string> Cultures => cultureWay.SupportedCultures;

    public string DefaultCulture => cultureWay.DefaultCulture;

    public bool ApiEnabled => api.Enabled;

    public bool OpenApiEnabled => api.Enabled && api.OpenApi.Enabled;

    public int UserCount { get; private set; }

    public int DisabledUserCount { get; private set; }

    public int ActiveKeyCount { get; private set; }

    public int KeyCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Editors have one place to work, so the overview would only be a detour.
        if (!User.IsInRole(nameof(UserRole.Admin)))
            return User.IsInRole(nameof(UserRole.Editor)) ? Redirect(EditorUrl) : Page();

        var ct = HttpContext.RequestAborted;
        var allUsers = await users.ListAsync(ct);
        var allKeys = await keys.ListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        UserCount = allUsers.Count;
        DisabledUserCount = allUsers.Count(u => u.IsDisabled);
        KeyCount = allKeys.Count;
        ActiveKeyCount = allKeys.Count(k => k.IsActive(now));
        return Page();
    }
}
