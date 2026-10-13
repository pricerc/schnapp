using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Schnapp.Web.Pages;

/// <summary>Legacy /Status → /Account/Status.</summary>
public sealed class StatusRedirectModel : PageModel
{
    public IActionResult OnGet() => RedirectToPagePermanent("/Account/Status");
}
