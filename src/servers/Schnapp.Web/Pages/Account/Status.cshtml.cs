using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

namespace Schnapp.Web.Pages.Account;

/// <summary>Session status and API health (no token display).</summary>
public sealed class StatusModel : PageModel
{
    private readonly ApiClient _api;

    public StatusModel(ApiClient api) => _api = api;

    public UserProfileResponse? Profile { get; private set; }
    public HealthResponse? Health { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");

        try
        {
            Health = await _api.GetHealthAsync(ct);
            Profile = await _api.GetMeAsync(ct);
            if (Profile is null)
            {
                AuthCookie.Clear(HttpContext);
                return RedirectToPage("/Login");
            }
        }
        catch (Exception ex)
        {
            Error = "API error: " + ex.Message;
        }

        return Page();
    }
}
