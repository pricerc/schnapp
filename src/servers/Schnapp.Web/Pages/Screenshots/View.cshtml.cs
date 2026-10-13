using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Screenshots;

/// <summary>Zoom/detail page for one screenshot with notes/tags edit.</summary>
public sealed class ViewModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ILogger<ViewModel> _logger;

    public ViewModel(ApiClient api, ILogger<ViewModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    public ScreenshotResponse? Shot { get; private set; }
    public string? Error { get; private set; }
    public string? Message { get; private set; }
    public string? ReturnDate { get; private set; }

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public string? Tags { get; set; }

    public async Task<IActionResult> OnGetAsync(Uuid7Param id, string? date, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        ReturnDate = date;
        Shot = await _api.GetScreenshotAsync(id, ct);
        if (Shot is null)
        {
            _logger.LogWarning("View {Id}: screenshot not found via API", Uuid7Text.Format(id));
            Error = "Not found.";
            return Page();
        }
        Notes = Shot.Notes;
        Tags = Shot.Tags;
        return Page();
    }

    public Task<IActionResult> OnPostAsync(Uuid7Param id, string? date, CancellationToken ct) =>
        SaveCoreAsync(id, date, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(Uuid7Param id, string? date, CancellationToken ct) =>
        SaveCoreAsync(id, date, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(Uuid7Param id, string? date, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        ReturnDate = date;
        var (shot, error) = await _api.UpdateScreenshotAsync(id, new UpdateScreenshotRequest
        {
            // Empty clears; null is treated as "leave unchanged" by the API.
            Notes = Notes ?? "",
            Tags = Tags ?? ""
        }, ct);
        if (error is not null)
        {
            Error = error;
            Shot = await _api.GetScreenshotAsync(id, ct);
            return Page();
        }

        if (close)
        {
            var home = string.IsNullOrWhiteSpace(date) ? "/" : $"/?date={Uri.EscapeDataString(date)}";
            return LocalRedirect(home);
        }

        Shot = shot;
        Message = "Saved.";
        Notes = shot!.Notes;
        Tags = shot.Tags;
        return Page();
    }
}
