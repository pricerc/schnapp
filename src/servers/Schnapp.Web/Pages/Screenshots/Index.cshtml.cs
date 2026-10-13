using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Schnapp.Web.Pages.Screenshots;

/// <summary>Legacy day-gallery URL → Home with date query.</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet(int? year, int? month, int? day, string? date, string? monitor, string? FromTime, string? ToTime)
    {
        string? d = null;
        if (year is not null && month is not null && day is not null)
            d = $"{year:D4}-{month:D2}-{day:D2}";
        else if (!string.IsNullOrWhiteSpace(date))
            d = date;

        return RedirectToPage("/Index", new { date = d, monitor, FromTime, ToTime });
    }
}
