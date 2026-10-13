using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Schnapp.Web.Pages.Lists;

/// <summary>Legacy Lists URL → Customers.</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Customers/Index");
}
