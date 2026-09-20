using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Kododo.Polyglot.Web.Pages;

[AllowAnonymous]
[EnableRateLimiting(AuthExtensions.LoginRateLimitPolicy)]
public class LoginModel(UserService users, AuthOptions auth) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";

    [BindProperty] public string Password { get; set; } = "";

    public string? ReturnUrl { get; private set; }

    public string? Error { get; private set; }

    public bool LocalEnabled => auth.Local.Enabled;

    public string? OidcName => auth.Oidc.IsConfigured ? auth.Oidc.DisplayName : null;

    public IActionResult OnGet(string? returnUrl, string? error)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl(returnUrl));

        ReturnUrl = returnUrl;
        Error = error == "sso" ? "Single sign-on failed." : null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!auth.Local.Enabled)
            return BadRequest();

        ReturnUrl = returnUrl;

        var user = ModelState.IsValid
            ? await users.ValidateCredentialsAsync(Username, Password, HttpContext.RequestAborted)
            : null;

        if (user is null)
        {
            Error = "Invalid username or password.";
            return Page();
        }

        await HttpContext.SignInAsync(
            AuthExtensions.CookieScheme, PrincipalFactory.Create(user, AuthExtensions.CookieScheme));
        return LocalRedirect(SafeReturnUrl(returnUrl));
    }

    public IActionResult OnGetOidc(string? returnUrl)
    {
        if (!auth.Oidc.IsConfigured)
            return NotFound();

        return Challenge(
            new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
            AuthExtensions.OidcScheme);
    }

    private string SafeReturnUrl(string? returnUrl)
        => Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/");
}
