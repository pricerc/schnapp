using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Schnapp.Web.Pages;

/// <summary>Legacy /Profile → /Account/Profile.</summary>
public sealed class ProfileRedirectModel : PageModel
{
    public IActionResult OnGet(string? machineStatus) =>
        RedirectToPagePermanent("/Account/Profile", new { machineStatus });
}
