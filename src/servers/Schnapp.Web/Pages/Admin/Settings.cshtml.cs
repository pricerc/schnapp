using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

namespace Schnapp.Web.Pages.Admin;

/// <summary>Admin settings grid with inline value save.</summary>
public sealed class SettingsModel : PageModel
{
    private readonly ApiClient _api;
    public SettingsModel(ApiClient api) => _api = api;

    public List<SettingResponse> Settings { get; private set; } = new();
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public string Key { get; set; } = "";

    [BindProperty]
    public string Value { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(Key))
        {
            Error = "Key is required.";
            await LoadAsync(ct);
            return Page();
        }
        var error = await _api.UpdateSettingAsync(Key.Trim(), Value ?? "", ct);
        if (error is not null) Error = error;
        else Message = $"Updated {Key}.";
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct) =>
        Settings = await _api.ListSettingsAsync(ct) ?? new();
}
