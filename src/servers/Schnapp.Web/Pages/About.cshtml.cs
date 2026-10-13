using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Schnapp.Web.Pages;

/// <summary>Legacy /About → /Account/About.</summary>
public sealed class AboutRedirectModel : PageModel
{
    public IActionResult OnGet() => RedirectToPagePermanent("/Account/About");
}
