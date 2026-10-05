using System.Security.Claims;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Kododo.Polyglot.Web.Demo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kododo.Polyglot.Web.Pages.Admin;

[Authorize(Policy = PolyglotPolicies.Admin)]
[DemoReadOnly]
public class UsersModel(UserService users, DemoOptions demo) : PageModel
{
    public class CreateInput
    {
        public string Username { get; set; } = "";

        public string? DisplayName { get; set; }

        public string Password { get; set; } = "";

        public UserRole Role { get; set; } = UserRole.Editor;
    }

    [BindProperty] public CreateInput Input { get; set; } = new();

    [TempData] public string? Message { get; set; }

    public IReadOnlyList<User> Users { get; private set; } = [];

    public string[] Errors { get; private set; } = [];

    /// <summary>In the demo the users can be looked at, not changed.</summary>
    public bool ReadOnly => demo.Enabled;

    /// <summary>Admins cannot lock themselves out: their own row has no role or disable controls.</summary>
    public Guid? CurrentUserId
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public async Task OnGetAsync() => Users = await users.ListAsync(HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var (user, errors) = await users.CreateLocalAsync(
            Input.Username ?? "", Input.DisplayName, Input.Password ?? "",
            Input.Role == UserRole.Admin ? UserRole.Admin : UserRole.Editor, HttpContext.RequestAborted);

        if (user is null)
            return await ShowErrorsAsync(errors);

        Message = $"Created user '{user.Username}'.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRoleAsync(Guid id, UserRole role)
        => id == CurrentUserId
            ? await ShowErrorsAsync(["You cannot change your own role. Ask another administrator."])
            : await ApplyAsync(await users.SetRoleAsync(id, role, HttpContext.RequestAborted), "Role updated.");

    public async Task<IActionResult> OnPostDisabledAsync(Guid id, bool disabled)
        => id == CurrentUserId
            ? await ShowErrorsAsync(["You cannot disable your own account. Ask another administrator."])
            : await ApplyAsync(
                await users.SetDisabledAsync(id, disabled, HttpContext.RequestAborted),
                disabled ? "User disabled." : "User enabled.");

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid id, string newPassword)
        => await ApplyAsync(
            await users.SetPasswordAsync(id, newPassword ?? "", HttpContext.RequestAborted), "Password reset.");

    private async Task<IActionResult> ApplyAsync(string[] errors, string success)
    {
        if (errors.Length > 0)
            return await ShowErrorsAsync(errors);

        Message = success;
        return RedirectToPage();
    }

    private async Task<IActionResult> ShowErrorsAsync(string[] errors)
    {
        Errors = errors;
        Users = await users.ListAsync(HttpContext.RequestAborted);
        return Page();
    }
}
