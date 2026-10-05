using System.Security.Claims;
using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Data;
using Kododo.Polyglot.Web.Demo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kododo.Polyglot.Web.Pages;

[Authorize]
[DemoReadOnly]
public class AccountModel(UserService users, DemoOptions demo) : PageModel
{
    public class PasswordInput
    {
        public string CurrentPassword { get; set; } = "";

        public string NewPassword { get; set; } = "";
    }

    [BindProperty] public PasswordInput Input { get; set; } = new();

    [TempData] public string? Message { get; set; }

    public string Username { get; private set; } = "";

    public UserRole Role { get; private set; }

    public bool HasPassword { get; private set; }

    public string[] Errors { get; private set; } = [];

    /// <summary>Everyone shares the demo account, so its password stays the published one.</summary>
    public bool IsDemo => demo.Enabled;

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : Forbid();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync() || !HasPassword)
            return Forbid();

        Errors = await users.ChangePasswordAsync(
            CurrentUserId(), Input.CurrentPassword ?? "", Input.NewPassword ?? "", HttpContext.RequestAborted);
        if (Errors.Length > 0)
            return Page();

        Message = "Password changed.";
        return RedirectToPage();
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> LoadAsync()
    {
        var user = await users.FindByIdAsync(CurrentUserId(), HttpContext.RequestAborted);
        if (user is null)
            return false;

        Username = user.Username;
        Role = user.Role;
        HasPassword = user.PasswordHash is not null;
        return true;
    }
}
