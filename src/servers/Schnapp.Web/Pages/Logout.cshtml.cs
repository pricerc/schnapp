using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Web.Services;

namespace Schnapp.Web.Pages;

/// <summary>
/// Clears the auth cookie and returns to the login page.
/// </summary>
public sealed class LogoutModel : PageModel
{
    /// <summary>GET or POST: delete cookie and redirect.</summary>
    public IActionResult OnGet()
    {
        AuthCookie.Clear(HttpContext);
        return RedirectToPage("/Login");
    }

    /// <inheritdoc cref="OnGet"/>
    public IActionResult OnPost() => OnGet();
}
