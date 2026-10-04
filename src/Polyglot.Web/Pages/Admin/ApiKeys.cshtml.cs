using System.Security.Claims;
using Kododo.Polyglot.Web.Api;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kododo.Polyglot.Web.Pages.Admin;

[Authorize(Policy = PolyglotPolicies.Admin)]
public class ApiKeysModel(ApiKeyService keys, ApiOptions api) : PageModel
{
    public class CreateInput
    {
        public string Name { get; set; } = "";

        /// <summary>The last day the key works, in UTC; null for a key that does not expire.</summary>
        public DateOnly? ValidThrough { get; set; }
    }

    [BindProperty] public CreateInput Input { get; set; } = new();

    [TempData] public string? Message { get; set; }

    public IReadOnlyList<ApiKey> Keys { get; private set; } = [];

    public string[] Errors { get; private set; } = [];

    /// <summary>The token just issued. Rendered once, in the response to the post that created it, never stored.</summary>
    public string? NewToken { get; private set; }

    public string? NewKeyName { get; private set; }

    public bool ApiEnabled => api.Enabled;

    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    public async Task OnGetAsync() => Keys = await keys.ListAsync(HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostCreateAsync()
    {
        // Through the end of the chosen day, so "valid through 31 Dec" still works on 31 Dec.
        DateTimeOffset? expiresAt = Input.ValidThrough is { } day
            ? new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

        var (key, token, errors) = await keys.CreateAsync(
            Input.Name ?? "", CurrentUserId(), expiresAt, HttpContext.RequestAborted);

        if (key is null)
            return await ShowAsync(errors);

        // Rendered rather than redirected: TempData would put the token in a cookie. And never cached,
        // so the back button cannot bring it back.
        Response.Headers.CacheControl = "no-store";
        NewToken = token;
        NewKeyName = key.Name;
        Input = new CreateInput();
        ModelState.Clear();
        return await ShowAsync([]);
    }

    public async Task<IActionResult> OnPostDisabledAsync(Guid id, bool disabled)
        => await ApplyAsync(
            await keys.SetDisabledAsync(id, disabled, HttpContext.RequestAborted),
            disabled ? "API key disabled." : "API key enabled.");

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        => await ApplyAsync(await keys.DeleteAsync(id, HttpContext.RequestAborted), "API key deleted.");

    private async Task<IActionResult> ApplyAsync(string[] errors, string success)
    {
        if (errors.Length > 0)
            return await ShowAsync(errors);

        Message = success;
        return RedirectToPage();
    }

    private async Task<IActionResult> ShowAsync(string[] errors)
    {
        Errors = errors;
        Keys = await keys.ListAsync(HttpContext.RequestAborted);
        return Page();
    }

    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string Status(ApiKey key)
        => key.IsDisabled ? "disabled" : key.IsActive(Now) ? "active" : "expired";
}
