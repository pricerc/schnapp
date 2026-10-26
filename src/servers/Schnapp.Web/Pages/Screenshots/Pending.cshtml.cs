using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Screenshots;

/// <summary>Screenshots hidden by soft-delete, waiting to be restored or purged.</summary>
public sealed class PendingModel : PageModel
{
    private readonly ApiClient _api;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    public PendingModel(ApiClient api) => _api = api;

    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string ProfileTimeZoneId { get; private set; } = "UTC";
    public List<PendingShot> Shots { get; private set; } = new();

    [BindProperty]
    public List<string> SelectedIds { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? restored, int? purged, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (restored is int restoredCount)
            Message = restoredCount > 0
                ? $"Restored {restoredCount} screenshot(s)."
                : "Nothing was restored.";
        if (purged is int purgedCount)
            Message = purgedCount > 0
                ? $"Permanently deleted {purgedCount} screenshot(s)."
                : "Nothing was permanently deleted.";
        await LoadAsync(ct);
        return Page();
    }

    public Task<IActionResult> OnPostRestoreAsync(string? id, CancellationToken ct) =>
        MutateAsync(id, restore: true, ct);

    public Task<IActionResult> OnPostPurgeAsync(string? id, CancellationToken ct) =>
        MutateAsync(id, restore: false, ct);

    private async Task<IActionResult> MutateAsync(string? id, bool restore, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var ids = ResolveIds(id);
        if (ids.Count == 0)
        {
            Error = "Select at least one screenshot.";
            await LoadAsync(ct);
            return Page();
        }

        var (count, error) = restore
            ? await _api.RestoreScreenshotsAsync(ids, ct)
            : await _api.PurgeScreenshotsAsync(ids, ct);
        if (error is not null)
        {
            Error = error;
            await LoadAsync(ct);
            return Page();
        }

        return restore
            ? RedirectToPage(new { restored = count })
            : RedirectToPage(new { purged = count });
    }

    private List<Uuid7> ResolveIds(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && Uuid7Text.TryParse(id, out var one))
            return new List<Uuid7> { one };

        return SelectedIds
            .Select(s => Uuid7Text.TryParse(s, out var parsed) ? parsed : (Uuid7?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var me = await _api.GetMeAsync(ct);
        if (me is not null && !string.IsNullOrWhiteSpace(me.TimeZoneId))
            ProfileTimeZoneId = me.TimeZoneId.Trim();
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(ProfileTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            _timeZone = TimeZoneInfo.Utc;
        }

        var machines = await _api.ListMachinesAsync(ct) ?? new List<MachineResponse>();
        var names = machines.ToDictionary(
            m => m.Id,
            m => string.IsNullOrWhiteSpace(m.Name) ? m.Code : m.Name);
        var shots = await _api.ListPendingScreenshotsAsync(ct) ?? new List<ScreenshotResponse>();
        Shots = shots.Select(s => new PendingShot
        {
            Id = Uuid7Text.Format(s.Id),
            When = Format(s.TimestampUtc),
            Deleted = s.DeletedAt is { } deletedAt ? Format(deletedAt) : "—",
            Machine = names.TryGetValue(s.MachineId, out var name) ? name : "—",
            Monitor = string.IsNullOrWhiteSpace(s.MonitorName) ? "—" : s.MonitorName!,
            Notes = s.Notes ?? ""
        }).ToList();
    }

    private string Format(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _timeZone).ToString("yyyy-MM-dd HH:mm");

    public sealed class PendingShot
    {
        public required string Id { get; init; }
        public required string When { get; init; }
        public required string Deleted { get; init; }
        public required string Machine { get; init; }
        public required string Monitor { get; init; }
        public required string Notes { get; init; }
    }
}
