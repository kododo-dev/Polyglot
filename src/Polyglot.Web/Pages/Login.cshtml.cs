using Kododo.Polyglot.Web.Auth;
using Kododo.Polyglot.Web.Demo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Kododo.Polyglot.Web.Pages;

[AllowAnonymous]
[EnableRateLimiting(AuthExtensions.LoginRateLimitPolicy)]
public class LoginModel(UserService users, AuthOptions auth, DemoOptions demo) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";

    [BindProperty] public string Password { get; set; } = "";

    public string? ReturnUrl { get; private set; }

    public string? Error { get; private set; }

    public bool LocalEnabled => auth.Local.Enabled;

    public string? OidcName => auth.Oidc.IsConfigured ? auth.Oidc.DisplayName : null;

    public DemoOptions Demo => demo;

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

        return await SignInAsync(ModelState.IsValid ? Username : null, Password, returnUrl);
    }

    /// <summary>The demo's one-click sign-in, with the credentials the page shows anyway.</summary>
    public async Task<IActionResult> OnPostDemoAsync(string? returnUrl)
    {
        if (!demo.Enabled)
            return NotFound();

        return await SignInAsync(demo.Username, demo.Password, returnUrl);
    }

    /// <param name="username">Null when the form did not validate, which fails like wrong credentials.</param>
    private async Task<IActionResult> SignInAsync(string? username, string password, string? returnUrl)
    {
        ReturnUrl = returnUrl;

        var user = username is not null
            ? await users.ValidateCredentialsAsync(username, password, HttpContext.RequestAborted)
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
