using Kododo.Polyglot.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kododo.Polyglot.Web.Pages;

// The editor signs out with a plain form POST, which has no antiforgery token. Skipping the check is
// safe: the auth cookie is SameSite=Lax, so a cross-site POST arrives without it and signs nobody out.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(AuthExtensions.CookieScheme);
        return RedirectToPage("/Login");
    }
}
