using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Models;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages;

/// <summary>
/// Home: combined day timeline of screenshots (by capture batch) with journal context.
/// </summary>
public sealed class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api) => _api = api;

    public string Date { get; private set; } = "";
    public string PrevDate { get; private set; } = "";
    public string NextDate { get; private set; } = "";
    public string? MonitorFilter { get; private set; }
    /// <summary>Selected machine id (Uuid7 text), or null for all machines.</summary>
    public string? MachineFilter { get; private set; }
    public string? FromTimeFilter { get; private set; }
    public string? ToTimeFilter { get; private set; }
    public string? JournalFilter { get; private set; }
    public string? TagsFilter { get; private set; }
    public string? CustomerFilter { get; private set; }
    public string? ProjectFilter { get; private set; }
    public string? TaskFilter { get; private set; }
    /// <summary>Timeline group-by key (default <c>journal</c> = covering journal).</summary>
    public string GroupByNormalized { get; private set; } = "journal";
    public string SortBy { get; private set; } = "time";
    public string SortDir { get; private set; } = "desc";

    /// <summary>
    /// Group options: covering journal (default) plus journals-style C/P/T/M groupings.
    /// </summary>
    public static IReadOnlyList<(string Id, string Label)> GroupByOptions { get; } = new[]
    {
        ("journal", "Journal (covering)"),
        ("none", "None (flat)"),
        ("machine", "Machine"),
        ("customer", "Customer"),
        ("project", "Project"),
        ("task", "Task"),
        ("customer-project", "Customer › Project"),
        ("customer-project-task", "Customer › Project › Task")
    };
    /// <summary>Grid column headers for the current result set (after monitor filter).</summary>
    public IReadOnlyList<string> MonitorColumns { get; private set; } = Array.Empty<string>();
    /// <summary>Monitor filter dropdown options for the day (before monitor filter).</summary>
    public IReadOnlyList<string> MonitorFilterOptions { get; private set; } = Array.Empty<string>();
    /// <summary>Machine filter dropdown options for the day (before machine filter).</summary>
    public IReadOnlyList<MachineFilterOption> MachineFilterOptions { get; private set; } = Array.Empty<MachineFilterOption>();
    /// <summary>Combo suggestions for C/P/T day filters.</summary>
    public IReadOnlyList<string> CustomerFilterOptions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> ProjectFilterOptions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> TaskFilterOptions { get; private set; } = Array.Empty<string>();
    public List<TimelineRow> Rows { get; private set; } = new();
    public List<MachineResponse> Machines { get; private set; } = new();
    public List<JournalEntryResponse> OpenJournals { get; private set; } = new();
    public Dictionary<Uuid7, string> MachineNames { get; private set; } = new();
    public Dictionary<Uuid7, JournalEntryResponse> JournalsById { get; private set; } = new();
    public Dictionary<Uuid7, JournalTargetResponse> TargetsById { get; private set; } = new();
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string ProfileTimeZoneId { get; private set; } = "UTC";
    private TimeZoneInfo? _profileTimeZone;

    [BindProperty(SupportsGet = true)]
    public string? FromTime { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ToTime { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Journal { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TagsQ { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Customer { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Project { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Task { get; set; }

    /// <summary>GET query machine id filter (distinct from upload <see cref="MachineId"/>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Machine { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? GroupBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    [BindProperty]
    public List<string> SelectedIds { get; set; } = new();

    [BindProperty]
    public List<string> SelectedRowKeys { get; set; } = new();

    [BindProperty]
    public Uuid7Param MachineId { get; set; }

    [BindProperty]
    public string? MonitorName { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public string? CaptureLocal { get; set; }

    [BindProperty]
    public string? ClientTimeZoneId { get; set; }

    [BindProperty]
    public Uuid7Param? JournalEntryId { get; set; }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty]
    public string? ViewerNotes { get; set; }

    [BindProperty]
    public string? ViewerTags { get; set; }

    [BindProperty]
    public string? ViewerId { get; set; }

    [BindProperty]
    public string? ViewerJournalEntryId { get; set; }

    [BindProperty]
    public string? RowKey { get; set; }

    [BindProperty]
    public string? RowJournalEntryId { get; set; }

    [BindProperty]
    public string? RowNotes { get; set; }

    [BindProperty]
    public string? RowTags { get; set; }

    [BindProperty]
    public string? BulkJournalEntryId { get; set; }

    [BindProperty]
    public string? BulkNotes { get; set; }

    [BindProperty]
    public string? BulkTags { get; set; }

    [BindProperty]
    public bool BulkSetJournal { get; set; }

    [BindProperty]
    public bool BulkSetNotes { get; set; }

    [BindProperty]
    public bool BulkSetTags { get; set; }

    [BindProperty]
    public string? EditJournalId { get; set; }

    [BindProperty]
    public string? EditJournalIsNew { get; set; }

    [BindProperty]
    public string? EditJournalMachineId { get; set; }

    [BindProperty]
    public string? EditJournalLinkShotId { get; set; }

    [BindProperty]
    public string? EditJournalTargetId { get; set; }

    [BindProperty]
    public string? EditJournalStartLocal { get; set; }

    [BindProperty]
    public string? EditJournalStopLocal { get; set; }

    [BindProperty]
    public string? EditJournalTags { get; set; }

    [BindProperty]
    public string? EditJournalNotes { get; set; }

    [BindProperty]
    public string? EditTargetId { get; set; }

    [BindProperty]
    public string? EditTargetLinkJournalId { get; set; }

    [BindProperty]
    public string? EditTargetCustomerId { get; set; }

    [BindProperty]
    public string? EditTargetProjectId { get; set; }

    [BindProperty]
    public string? EditTargetTaskId { get; set; }

    [BindProperty]
    public string? EditTargetCode { get; set; }

    [BindProperty]
    public string? EditTargetName { get; set; }

    [BindProperty]
    public string? EditTargetTags { get; set; }

    [BindProperty]
    public string? EditTargetNotes { get; set; }

    /// <summary>Journals offered in the viewer dropdown (recent + open).</summary>
    public List<JournalOption> JournalOptions { get; private set; } = new();

    public JournalEditorsViewModel JournalEditors { get; private set; } = new();

    private const int GapThresholdMinutes = 10;
    private Dictionary<string, List<Uuid7>> _rowShotIds = new(StringComparer.Ordinal);
    private Dictionary<Uuid7, ListItemResponse> _customersById = new();
    private Dictionary<Uuid7, ListItemResponse> _projectsById = new();
    private Dictionary<Uuid7, ListItemResponse> _tasksById = new();
    private List<ListItemResponse> _customers = new();
    private List<ListItemResponse> _projects = new();
    private List<ListItemResponse> _tasks = new();

    public async Task<IActionResult> OnGetAsync(string? date, string? monitor, string? open, string? saved, string? deleted, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await InitDayAsync(date, monitor, ct);
        if (!string.IsNullOrWhiteSpace(open))
        {
            OpenViewerId = open.Trim();
            ViewData["OpenViewerId"] = OpenViewerId;
        }
        if (string.Equals(saved, "1", StringComparison.Ordinal))
            Message = "Saved.";
        if (string.Equals(deleted, "1", StringComparison.Ordinal))
            Message = "Screenshot deleted.";
        return Page();
    }

    public async Task<IActionResult> OnPostBulkDeleteAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var ids = ParseSelectedIds();
        if (ids.Count == 0) Error = "Select at least one screenshot.";
        else
        {
            var (deleted, error) = await _api.DeleteScreenshotsAsync(ids, ct);
            if (error is not null) Error = error;
            else Message = $"Deleted {deleted} screenshot(s).";
        }
        await InitDayAsync(date, monitor, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostBulkDeleteRowsAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await InitDayAsync(date, monitor, ct);
        var ids = ResolveRowShotIds(SelectedRowKeys);
        if (ids.Count == 0)
        {
            Error = "Select at least one row.";
            return Page();
        }

        var (deleted, error) = await _api.DeleteScreenshotsAsync(ids, ct);
        if (error is not null) Error = error;
        else Message = $"Deleted {deleted} screenshot(s) from selected row(s).";
        await InitDayAsync(date, monitor, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostBulkUpdateRowsAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await InitDayAsync(date, monitor, ct);
        var ids = ResolveRowShotIds(SelectedRowKeys);
        if (ids.Count == 0)
        {
            Error = "Select at least one row.";
            return Page();
        }
        if (!BulkSetJournal && !BulkSetNotes && !BulkSetTags)
        {
            Error = "Choose at least one field to update.";
            return Page();
        }

        Uuid7? journalId = null;
        if (BulkSetJournal && !string.IsNullOrWhiteSpace(BulkJournalEntryId))
        {
            if (!Uuid7Text.TryParse(BulkJournalEntryId, out var jid))
            {
                Error = "Invalid journal id.";
                return Page();
            }
            journalId = jid;
        }

        var updated = 0;
        foreach (var id in ids)
        {
            var req = new UpdateScreenshotRequest();
            // Empty string clears; null would be ignored by the API patch semantics.
            if (BulkSetNotes) req.Notes = BulkNotes ?? "";
            if (BulkSetTags) req.Tags = BulkTags ?? "";
            if (BulkSetJournal)
            {
                req.SetJournalEntryId = true;
                req.JournalEntryId = journalId;
            }
            var (_, error) = await _api.UpdateScreenshotAsync(id, req, ct);
            if (error is not null)
            {
                Error = error;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            updated++;
        }

        Message = $"Updated {updated} screenshot(s).";
        return RedirectToDay(date, monitor, saved: true);
    }

    public async Task<IActionResult> OnPostSaveRowAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(RowKey))
        {
            await InitDayAsync(date, monitor, ct);
            Error = "Missing row.";
            return Page();
        }

        // Resolve the full capture batch (all monitors) so clearing notes/tags is not limited
        // to the current monitor filter. API treats null as "leave unchanged", so always send
        // strings — empty form fields often bind as null.
        await InitDayAsync(date, monitor: null, ct);
        var ids = ResolveRowShotIds(new[] { RowKey });
        if (ids.Count == 0)
        {
            await InitDayAsync(date, monitor, ct);
            Error = "Row has no screenshots.";
            return Page();
        }

        var notes = RowNotes ?? "";
        var tags = RowTags ?? "";
        foreach (var id in ids)
        {
            var (_, error) = await _api.UpdateScreenshotAsync(id, new UpdateScreenshotRequest
            {
                Notes = notes,
                Tags = tags
            }, ct);
            if (error is not null)
            {
                Error = error;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
        }

        Message = "Row saved.";
        return RedirectToDay(date, monitor, saved: true);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Uuid7Param id, string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var error = await _api.DeleteScreenshotAsync(id, ct);
        if (error is not null) Error = error;
        else Message = "Deleted.";
        await InitDayAsync(date, monitor, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveJournalAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");

        Uuid7? targetId = null;
        if (!string.IsNullOrWhiteSpace(EditJournalTargetId))
        {
            if (!Uuid7Text.TryParse(EditJournalTargetId, out var tid))
            {
                Error = "Invalid journal target.";
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            targetId = tid;
        }

        DateTimeOffset? startUtc = ParseProfileLocal(EditJournalStartLocal);
        DateTimeOffset? stopUtc = ParseProfileLocal(EditJournalStopLocal);
        var isNew = string.Equals(EditJournalIsNew, "true", StringComparison.OrdinalIgnoreCase);
        Uuid7 linkShotId = default;
        var hasLinkShot = !string.IsNullOrWhiteSpace(EditJournalLinkShotId)
            && Uuid7Text.TryParse(EditJournalLinkShotId, out linkShotId);

        // Selecting "none" while editing from the shot viewer only unlinks the journal.
        if (!isNew && string.IsNullOrWhiteSpace(EditJournalId))
        {
            if (!hasLinkShot)
            {
                Error = "Select a journal.";
                await InitDayAsync(date, monitor, ct);
                return Page();
            }

            var (_, unlinkErr) = await _api.UpdateScreenshotAsync(linkShotId, new UpdateScreenshotRequest
            {
                SetJournalEntryId = true,
                JournalEntryId = null
            }, ct);
            if (unlinkErr is not null)
            {
                Error = unlinkErr;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }

            var unlinkQs = BuildDayQuery(date, monitor, FromTimeFilter ?? FromTime, ToTimeFilter ?? ToTime);
            unlinkQs["saved"] = "1";
            unlinkQs["open"] = Uuid7Text.Format(linkShotId);
            return RedirectToPage("/Index", unlinkQs);
        }

        Uuid7 jid;
        if (isNew)
        {
            if (!TryParseUuid(EditJournalMachineId, out var machineId))
            {
                Error = "Machine is required for a new journal.";
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            var (created, createError) = await _api.StartJournalAsync(new StartJournalRequest
            {
                MachineId = machineId,
                JournalTargetId = targetId,
                Notes = EditJournalNotes,
                Tags = EditJournalTags,
                StartUtc = startUtc
            }, ct);
            if (createError is not null || created is null)
            {
                Error = createError ?? "Could not create journal.";
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            jid = created.Id;
            if (stopUtc is not null || targetId is not null || startUtc is not null)
            {
                var (_, updErr) = await _api.UpdateJournalAsync(jid, new UpdateJournalRequest
                {
                    StartUtc = startUtc,
                    StopUtc = stopUtc,
                    JournalTargetId = targetId,
                    Notes = EditJournalNotes ?? "",
                    Tags = EditJournalTags ?? ""
                }, ct);
                if (updErr is not null)
                {
                    Error = updErr;
                    await InitDayAsync(date, monitor, ct);
                    return Page();
                }
            }
            Message = "Journal created.";
        }
        else
        {
            if (!Uuid7Text.TryParse(EditJournalId, out jid))
            {
                Error = "Select a journal.";
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            if (startUtc is null)
            {
                Error = "Started at is required.";
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            var (_, error) = await _api.UpdateJournalAsync(jid, new UpdateJournalRequest
            {
                StartUtc = startUtc,
                StopUtc = stopUtc,
                JournalTargetId = targetId,
                Notes = EditJournalNotes ?? "",
                Tags = EditJournalTags ?? ""
            }, ct);
            if (error is not null)
            {
                Error = error;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            Message = "Journal saved.";
        }

        // Optionally assign journal to the open screenshot.
        if (hasLinkShot)
        {
            var (_, shotErr) = await _api.UpdateScreenshotAsync(linkShotId, new UpdateScreenshotRequest
            {
                SetJournalEntryId = true,
                JournalEntryId = jid
            }, ct);
            if (shotErr is not null)
            {
                Error = shotErr;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            var qs = BuildDayQuery(date, monitor, FromTimeFilter ?? FromTime, ToTimeFilter ?? ToTime);
            qs["saved"] = "1";
            qs["open"] = Uuid7Text.Format(linkShotId);
            return RedirectToPage("/Index", qs);
        }

        return RedirectToDay(date, monitor, saved: true);
    }

    public async Task<IActionResult> OnPostSaveJournalTargetAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!TryParseUuid(EditTargetCustomerId, out var customerId)
            || !TryParseUuid(EditTargetProjectId, out var projectId)
            || !TryParseUuid(EditTargetTaskId, out var taskId))
        {
            Error = "Customer, project, and task are required.";
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        var (target, error) = await _api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
        {
            CustomerId = customerId,
            ProjectId = projectId,
            TaskId = taskId,
            Code = EditTargetCode,
            Name = EditTargetName,
            Notes = EditTargetNotes,
            Tags = EditTargetTags
        }, ct);
        if (error is not null || target is null)
        {
            Error = error ?? "Could not save journal target.";
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(EditTargetLinkJournalId)
            && Uuid7Text.TryParse(EditTargetLinkJournalId, out var linkJid))
        {
            var (_, jerr) = await _api.UpdateJournalAsync(linkJid, new UpdateJournalRequest
            {
                JournalTargetId = target.Id
            }, ct);
            if (jerr is not null)
            {
                Error = jerr;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
        }

        Message = "Journal target saved.";
        return RedirectToDay(date, monitor, saved: true);
    }

    private static bool TryParseUuid(string? text, out Uuid7 id)
    {
        id = default;
        return !string.IsNullOrWhiteSpace(text) && Uuid7Text.TryParse(text, out id);
    }

    private DateTimeOffset? ParseProfileLocal(string? local)
    {
        if (string.IsNullOrWhiteSpace(local)) return null;
        if (!DateTime.TryParse(local, out var dt)) return null;
        try
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), ProfileTimeZone);
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }
        catch
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        }
    }

    /// <summary>When set, the shot viewer reopens on this id after a Save (not Save and close).</summary>
    public string? OpenViewerId { get; private set; }

    public Task<IActionResult> OnPostSaveViewerAsync(
        string? date, string? monitor, string? FromTime, string? ToTime, CancellationToken ct) =>
        SaveViewerCoreAsync(date, monitor, FromTime, ToTime, reopen: true, ct);

    public Task<IActionResult> OnPostSaveViewerAndCloseAsync(
        string? date, string? monitor, string? FromTime, string? ToTime, CancellationToken ct) =>
        SaveViewerCoreAsync(date, monitor, FromTime, ToTime, reopen: false, ct);

    public async Task<IActionResult> OnPostDeleteViewerAsync(
        string? date, string? monitor, string? FromTime, string? ToTime, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");

        this.FromTime = FromTime;
        this.ToTime = ToTime;

        if (string.IsNullOrWhiteSpace(ViewerId) || !Uuid7Text.TryParse(ViewerId, out var id))
        {
            Error = "Missing screenshot id.";
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        var error = await _api.DeleteScreenshotAsync(id, ct);
        if (error is not null)
        {
            Error = error;
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        var qs = BuildDayQuery(date, monitor, FromTime, ToTime);
        qs["deleted"] = "1";
        return RedirectToPage("/Index", qs);
    }

    private async Task<IActionResult> SaveViewerCoreAsync(
        string? date, string? monitor, string? FromTime, string? ToTime, bool reopen, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(ViewerId) || !Uuid7Text.TryParse(ViewerId, out var id))
        {
            Error = "Missing screenshot id.";
            this.FromTime = FromTime;
            this.ToTime = ToTime;
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        Uuid7? journalId = null;
        if (!string.IsNullOrWhiteSpace(ViewerJournalEntryId))
        {
            if (!Uuid7Text.TryParse(ViewerJournalEntryId, out var jid))
            {
                Error = "Invalid journal id.";
                this.FromTime = FromTime;
                this.ToTime = ToTime;
                await InitDayAsync(date, monitor, ct);
                return Page();
            }
            journalId = jid;
        }

        var (shot, error) = await _api.UpdateScreenshotAsync(id, new UpdateScreenshotRequest
        {
            Notes = ViewerNotes ?? "",
            Tags = ViewerTags ?? "",
            SetJournalEntryId = true,
            JournalEntryId = journalId
        }, ct);
        _ = shot;

        if (error is not null)
        {
            Error = error;
            this.FromTime = FromTime;
            this.ToTime = ToTime;
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        var qs = BuildDayQuery(date, monitor, FromTime, ToTime);
        qs["saved"] = "1";
        if (reopen)
            qs["open"] = Uuid7Text.Format(id);

        return RedirectToPage("/Index", qs);
    }

    public async Task<IActionResult> OnPostUploadAsync(string? date, string? monitor, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (UploadFile is null || UploadFile.Length == 0)
        {
            Error = "Choose an image file.";
            await InitDayAsync(date, monitor, ct);
            return Page();
        }

        DateTimeOffset? ts = null;
        var tzId = string.IsNullOrWhiteSpace(ClientTimeZoneId) ? ProfileTimeZoneId : ClientTimeZoneId.Trim();
        if (!string.IsNullOrWhiteSpace(CaptureLocal) && DateTime.TryParse(CaptureLocal, out var localDt))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), tz);
                ts = new DateTimeOffset(utc, TimeSpan.Zero);
            }
            catch
            {
                ts = new DateTimeOffset(DateTime.SpecifyKind(localDt, DateTimeKind.Utc));
            }
        }

        await using var stream = UploadFile.OpenReadStream();
        var (shot, error) = await _api.UploadScreenshotAsync(
            MachineId,
            stream,
            UploadFile.FileName,
            UploadFile.ContentType ?? "image/png",
            MonitorName,
            Notes,
            ts,
            tzId,
            JournalEntryId is { } jid && jid != Uuid7.Empty ? jid : null,
            ct);

        if (error is not null) Error = error;
        else if (shot is not null)
        {
            Message = "Screenshot uploaded.";
            // Day in profile timezone (not UTC calendar date).
            await EnsureProfileTimeZoneAsync(ct);
            date = FormatProfileDate(shot.TimestampUtc);
        }

        await InitDayAsync(date, monitor, ct);
        return Page();
    }

    private List<Uuid7> ParseSelectedIds() =>
        SelectedIds
            .Select(s => Uuid7Text.TryParse(s, out var id) ? id : (Uuid7?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

    private List<Uuid7> ResolveRowShotIds(IEnumerable<string> keys)
    {
        var ids = new List<Uuid7>();
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            if (_rowShotIds.TryGetValue(key.Trim(), out var list))
                ids.AddRange(list);
        }
        return ids.Distinct().ToList();
    }

    private IActionResult RedirectToDay(string? date, string? monitor, bool saved = false)
    {
        var qs = BuildDayQuery(date, monitor, FromTimeFilter ?? FromTime, ToTimeFilter ?? ToTime);
        if (saved) qs["saved"] = "1";
        return RedirectToPage("/Index", qs);
    }

    private Dictionary<string, string?> BuildDayQuery(string? date, string? monitor, string? fromTime, string? toTime)
    {
        var qs = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(date)) qs["date"] = date;
        if (!string.IsNullOrWhiteSpace(monitor)) qs["monitor"] = monitor;
        if (!string.IsNullOrWhiteSpace(fromTime)) qs["FromTime"] = fromTime;
        if (!string.IsNullOrWhiteSpace(toTime)) qs["ToTime"] = toTime;
        if (!string.IsNullOrWhiteSpace(MachineFilter ?? Machine)) qs["Machine"] = MachineFilter ?? Machine;
        if (!string.IsNullOrWhiteSpace(JournalFilter ?? Journal)) qs["Journal"] = JournalFilter ?? Journal;
        if (!string.IsNullOrWhiteSpace(TagsFilter ?? TagsQ)) qs["TagsQ"] = TagsFilter ?? TagsQ;
        if (!string.IsNullOrWhiteSpace(CustomerFilter ?? Customer)) qs["Customer"] = CustomerFilter ?? Customer;
        if (!string.IsNullOrWhiteSpace(ProjectFilter ?? Project)) qs["Project"] = ProjectFilter ?? Project;
        if (!string.IsNullOrWhiteSpace(TaskFilter ?? Task)) qs["Task"] = TaskFilter ?? Task;
        if (!string.IsNullOrWhiteSpace(GroupByNormalized) && GroupByNormalized != "journal")
            qs["GroupBy"] = GroupByNormalized;
        if (!string.IsNullOrWhiteSpace(SortBy) && !string.Equals(SortBy, "time", StringComparison.OrdinalIgnoreCase))
            qs["Sort"] = SortBy;
        // Default sort direction is descending; only emit when ascending.
        if (!string.IsNullOrWhiteSpace(SortDir) && string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase))
            qs["Dir"] = "asc";
        return qs;
    }

    private async Task InitDayAsync(string? date, string? monitor, CancellationToken ct)
    {
        // Profile TZ first so default "today" matches the user's calendar day.
        await EnsureProfileTimeZoneAsync(ct);

        if (!string.IsNullOrWhiteSpace(date)) Date = date.Trim();
        else Date = ""; // force SetNavDates to resolve profile-local today
        MonitorFilter = string.IsNullOrWhiteSpace(monitor) ? null : monitor.Trim();
        // Query machine filter (id text). Invalid values are ignored so a bad link still loads the day.
        if (!string.IsNullOrWhiteSpace(Machine) && Uuid7Text.TryParse(Machine.Trim(), out var mid))
            MachineFilter = Uuid7Text.Format(mid);
        else
            MachineFilter = null;
        FromTimeFilter = string.IsNullOrWhiteSpace(FromTime) ? null : FromTime;
        ToTimeFilter = string.IsNullOrWhiteSpace(ToTime) ? null : ToTime;
        JournalFilter = string.IsNullOrWhiteSpace(Journal) ? null : Journal.Trim();
        TagsFilter = string.IsNullOrWhiteSpace(TagsQ) ? null : TagsQ.Trim();
        CustomerFilter = string.IsNullOrWhiteSpace(Customer) ? null : Customer.Trim();
        ProjectFilter = string.IsNullOrWhiteSpace(Project) ? null : Project.Trim();
        TaskFilter = string.IsNullOrWhiteSpace(Task) ? null : Task.Trim();
        GroupByNormalized = NormalizeGroupBy(GroupBy);
        GroupBy = GroupByNormalized;
        SortBy = NormalizeSort(Sort);
        // Newest-first by default; only "asc" is explicit ascending.
        SortDir = string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
        SetNavDates();
        await LoadAsync(ct);
    }

    private static string NormalizeSort(string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "target" or "journaltarget" => "target",
            "tags" => "tags",
            "machine" => "machine",
            _ => "time"
        };

    /// <summary>Normalizes group-by; default is covering journal (existing home behaviour).</summary>
    public static string NormalizeGroupBy(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "none" or "flat" => "none",
            "machine" => "machine",
            "customer" => "customer",
            "project" => "project",
            "task" => "task",
            "customer-project" or "customerproject" or "cp" => "customer-project",
            "customer-project-task" or "customerprojecttask" or "cpt" => "customer-project-task",
            _ => "journal"
        };

    /// <summary>Calendar "today" in the user's profile timezone (not UTC).</summary>
    private DateOnly ProfileToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ProfileTimeZone));

    /// <summary>yyyy-MM-dd of an instant in the profile timezone.</summary>
    private string FormatProfileDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(ToProfileLocal(utc)).ToString("yyyy-MM-dd");

    private void SetNavDates()
    {
        if (!DateOnly.TryParse(Date, out var d))
            d = ProfileToday();
        Date = d.ToString("yyyy-MM-dd");
        PrevDate = d.AddDays(-1).ToString("yyyy-MM-dd");
        NextDate = d.AddDays(1).ToString("yyyy-MM-dd");
    }

    private async Task EnsureProfileTimeZoneAsync(CancellationToken ct)
    {
        if (_profileTimeZone is not null) return;
        var me = await _api.GetMeAsync(ct);
        if (me is not null)
            ProfileTimeZoneId = string.IsNullOrWhiteSpace(me.TimeZoneId) ? "UTC" : me.TimeZoneId;
        _profileTimeZone = ResolveTimeZone(ProfileTimeZoneId);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        await EnsureProfileTimeZoneAsync(ct);

        Machines = await _api.ListMachinesAsync(ct) ?? new();
        MachineNames = Machines.ToDictionary(m => m.Id, m => m.Name);

        var journals = await _api.ListJournalsAsync(ct) ?? new();
        JournalsById = journals.ToDictionary(j => j.Id, j => j);
        OpenJournals = journals.Where(j => j.IsOpen).ToList();

        var targets = await _api.ListJournalTargetsAsync(ct) ?? new();
        TargetsById = targets.ToDictionary(t => t.Id, t => t);

        _customers = await _api.ListItemsAsync("customers", ct) ?? new();
        _projects = await _api.ListItemsAsync("projects", ct) ?? new();
        _tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        _customersById = _customers.ToDictionary(c => c.Id);
        _projectsById = _projects.ToDictionary(p => p.Id);
        _tasksById = _tasks.ToDictionary(t => t.Id);

        JournalOptions = journals
            .OrderByDescending(j => j.IsOpen)
            .ThenByDescending(j => j.StartUtc)
            .Take(80)
            .Select(j => new JournalOption
            {
                Id = Uuid7Text.Format(j.Id),
                Label = FormatJournalOption(j)
            })
            .ToList();

        JournalEditors = BuildJournalEditors("/Index", dateHidden: true);

        // Load the full day unfiltered by monitor. Column headers (Window / Desktop /
        // Monitor N) are display buckets derived from ColumnDef — they often differ from
        // stored monitor_name (e.g. "Desktop" vs "desktop", "Monitor 0" vs EDID name).
        // Filtering must use ColumnDef, same as the grid columns.
        var shots = await _api.ListScreenshotsAsync(Date, monitor: null, ct) ?? new();
        if (!string.IsNullOrEmpty(FromTimeFilter) && TimeOnly.TryParse(FromTimeFilter, out var fromT))
            shots = shots.Where(s => TimeOnly.FromDateTime(ToProfileLocal(s.TimestampUtc)) >= fromT).ToList();
        if (!string.IsNullOrEmpty(ToTimeFilter) && TimeOnly.TryParse(ToTimeFilter, out var toT))
            shots = shots.Where(s => TimeOnly.FromDateTime(ToProfileLocal(s.TimestampUtc)) <= toT).ToList();

        var ordered = shots
            .OrderBy(s => s.TimestampUtc)
            .ThenBy(s => ColumnSortKey(ColumnDef(s).Key))
            .ToList();

        // Machine options from the day (after time filter) so switching machines stays possible.
        var machineOptions = ordered
            .Select(s => s.MachineId)
            .Distinct()
            .Select(id => new MachineFilterOption
            {
                Id = Uuid7Text.Format(id),
                Name = MachineLabel(id)
            })
            .ToList();
        // Keep a selected machine visible even if it has no shots in the current time window.
        if (!string.IsNullOrEmpty(MachineFilter)
            && machineOptions.All(m => !string.Equals(m.Id, MachineFilter, StringComparison.OrdinalIgnoreCase))
            && Uuid7Text.TryParse(MachineFilter, out var selectedMid))
        {
            machineOptions.Add(new MachineFilterOption
            {
                Id = Uuid7Text.Format(selectedMid),
                Name = MachineLabel(selectedMid)
            });
        }
        MachineFilterOptions = machineOptions
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrEmpty(MachineFilter)
            && Uuid7Text.TryParse(MachineFilter, out var filterMachineId))
        {
            ordered = ordered.Where(s => s.MachineId == filterMachineId).ToList();
        }

        // Dropdown options from the unfiltered day so switching monitors stays possible.
        // Built after machine filter so the list matches the selected machine when set.
        MonitorFilterOptions = ordered
            .Select(ColumnDef)
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => ColumnSortKey(d.Key))
            .Select(d => d.Header)
            .ToList();

        if (!string.IsNullOrEmpty(MonitorFilter))
        {
            ordered = ordered
                .Where(s => string.Equals(ColumnDef(s).Header, MonitorFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Grid columns follow the filtered set (typically one capture surface when filtered).
        MonitorColumns = ordered
            .Select(ColumnDef)
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => ColumnSortKey(d.Key))
            .Select(d => d.Header)
            .ToList();

        var groups = ordered
            .GroupBy(s => TruncateToSecondUtc(s.TimestampUtc))
            .OrderBy(g => g.Key)
            .ToList();

        var rows = new List<TimelineRow>();
        _rowShotIds = new Dictionary<string, List<Uuid7>>(StringComparer.Ordinal);
        DateTimeOffset? prevTs = null;
        foreach (var g in groups)
        {
            var list = g
                .OrderBy(s => ColumnSortKey(ColumnDef(s).Key))
                .ThenBy(s => s.TimestampUtc)
                .ThenBy(s => s.Id)
                .ToList();
            var ts = g.Key;
            var gap = prevTs is not null && (ts - prevTs.Value).TotalMinutes >= GapThresholdMinutes;

            var byCol = new Dictionary<string, List<ScreenshotResponse>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in list)
            {
                var header = ColumnDef(s).Header;
                if (!byCol.TryGetValue(header, out var cell))
                {
                    cell = new List<ScreenshotResponse>();
                    byCol[header] = cell;
                }
                cell.Add(s);
            }

            // Covering journal = time-span context for group headings (not a stored shot link).
            var covering = ResolveCoveringJournal(journals, ts);
            // Linked journals = explicit screenshot.JournalEntryId values in this batch.
            var linked = ResolveLinkedJournals(list);
            var shotTags = list.Select(s => s.Tags).ToList();
            var shotNotes = list.Select(s => s.Notes).ToList();
            var tags = FirstNonEmpty(shotTags);
            var notes = FirstNonEmpty(shotNotes);
            var rowKey = RowKeyFor(ts);
            var shotIds = list.Select(s => s.Id).ToList();
            _rowShotIds[rowKey] = shotIds;
            var machineLabel = string.Join(", ",
                list.Select(s => MachineLabel(s.MachineId)).Distinct(StringComparer.OrdinalIgnoreCase));

            // Full weekday of journal start (or batch time when none) — makes cross-day covers obvious.
            var coveringWeekday = FormatProfileWeekday(covering.entry?.StartUtc ?? ts);
            var coveringLabel = covering.entry is null
                ? covering.label
                : $"{coveringWeekday} · {covering.label}";

            var cpt = ResolveCptLabels(covering.entry, linked.primaryTargetId);
            rows.Add(new TimelineRow
            {
                Key = rowKey,
                Timestamp = ts,
                ShowGapBefore = gap,
                ShotsByColumn = byCol,
                ShotIds = shotIds.Select(Uuid7Text.Format).ToList(),
                MachineLabel = string.IsNullOrWhiteSpace(machineLabel) ? "—" : machineLabel,
                // Row "Journal target" column: only explicit per-image links
                JournalLabel = linked.label,
                JournalEntryId = linked.primaryId,
                JournalTargetId = linked.primaryTargetId,
                JournalTargetLabel = linked.targetLabel,
                JournalTargetSort = linked.targetLabel,
                CustomerLabel = cpt.customer,
                ProjectLabel = cpt.project,
                TaskLabel = cpt.task,
                CustomerColour = cpt.colour,
                // Group heading uses time-covering journal (work context)
                CoveringJournalId = covering.entry is null ? null : Uuid7Text.Format(covering.entry.Id),
                CoveringJournalLabel = coveringLabel,
                Tags = tags,
                Notes = notes,
                MixedJournal = linked.mixed,
                MixedTags = DistinctNonEmpty(shotTags).Count > 1,
                MixedNotes = DistinctNonEmpty(shotNotes).Count > 1
            });
            prevTs = ts;
        }

        // Combo options for C/P/T filters (from the day rows before C/P/T filtering).
        CustomerFilterOptions = rows
            .Select(r => r.CustomerLabel)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ProjectFilterOptions = rows
            .Select(r => r.ProjectLabel)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        TaskFilterOptions = rows
            .Select(r => r.TaskLabel)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrEmpty(JournalFilter))
        {
            rows = rows
                .Where(r => (r.CoveringJournalLabel?.Contains(JournalFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (r.JournalLabel?.Contains(JournalFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (r.JournalTargetLabel?.Contains(JournalFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }
        if (!string.IsNullOrEmpty(TagsFilter))
        {
            rows = rows
                .Where(r => r.Tags?.Contains(TagsFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                .ToList();
        }
        if (!string.IsNullOrEmpty(CustomerFilter))
        {
            rows = rows
                .Where(r => r.CustomerLabel.Contains(CustomerFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (!string.IsNullOrEmpty(ProjectFilter))
        {
            rows = rows
                .Where(r => r.ProjectLabel.Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (!string.IsNullOrEmpty(TaskFilter))
        {
            rows = rows
                .Where(r => r.TaskLabel.Contains(TaskFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Cluster rows by group key (all of one machine/customer/… together), then sort within group.
        rows = OrderRowsForDisplay(rows);
        ApplyGroupHeadings(rows);
        Rows = rows;

        if (MachineId == Uuid7.Empty && Machines.Count > 0)
            MachineId = Machines[0].Id;
        if (string.IsNullOrWhiteSpace(CaptureLocal))
            CaptureLocal = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
        if (string.IsNullOrWhiteSpace(ClientTimeZoneId))
            ClientTimeZoneId = ProfileTimeZoneId;

        ViewData["Wide"] = true;
    }

    /// <summary>
    /// Orders rows for display. When group-by is active (not flat), clusters every row with the
    /// same group key into one contiguous section (so Machine A is not split across the day),
    /// then sorts within each section. Group sections themselves are ordered by the earliest
    /// batch timestamp in that group (timeline sense).
    /// </summary>
    private List<TimelineRow> OrderRowsForDisplay(List<TimelineRow> rows)
    {
        if (rows.Count == 0)
            return rows;

        // Flat: pure sort, no clustering.
        if (GroupByNormalized is "none" or null or "")
            return ApplySort(rows).ToList();

        return rows
            .GroupBy(GroupKey, StringComparer.Ordinal)
            .OrderBy(g => g.Min(r => r.Timestamp))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => ApplySort(g.ToList()))
            .ToList();
    }

    private IEnumerable<TimelineRow> ApplySort(IEnumerable<TimelineRow> rows)
    {
        IOrderedEnumerable<TimelineRow> ordered = SortBy switch
        {
            "target" => SortDir == "desc"
                ? rows.OrderByDescending(r => r.JournalTargetSort, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.JournalTargetSort, StringComparer.OrdinalIgnoreCase),
            "tags" => SortDir == "desc"
                ? rows.OrderByDescending(r => r.Tags ?? "", StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.Tags ?? "", StringComparer.OrdinalIgnoreCase),
            "machine" => SortDir == "desc"
                ? rows.OrderByDescending(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase),
            _ => SortDir == "desc"
                ? rows.OrderByDescending(r => r.Timestamp)
                : rows.OrderBy(r => r.Timestamp)
        };

        if (SortBy != "time")
            ordered = ordered.ThenBy(r => r.Timestamp);

        return ordered.ThenBy(r => r.Key, StringComparer.Ordinal);
    }

    /// <summary>Stable column identity matching the offline-queue client.</summary>
    private static (string Key, string Header) ColumnDef(ScreenshotResponse s)
    {
        var hasWindow = !string.IsNullOrWhiteSpace(s.WindowTitle) || !string.IsNullOrWhiteSpace(s.ProgramName);
        if (hasWindow)
            return ("window", "Window");

        var mon = s.MonitorName?.Trim();
        if (string.Equals(mon, "desktop", StringComparison.OrdinalIgnoreCase))
            return ("desktop", "Desktop");
        if (string.Equals(mon, "window", StringComparison.OrdinalIgnoreCase))
            return ("window", "Window");

        var monId = s.MonitorId?.Trim();
        if (!string.IsNullOrEmpty(monId))
            return ("monitor:" + monId, "Monitor " + monId);

        if (!string.IsNullOrEmpty(mon))
            return ("monitor:" + mon, mon);

        return ("other", "Screenshot");
    }

    private static (int Group, int Index) ColumnSortKey(string key)
    {
        if (key == "window") return (0, 0);
        if (key == "desktop") return (1, 0);
        if (key.StartsWith("monitor:", StringComparison.Ordinal))
        {
            var suffix = key["monitor:".Length..];
            return (2, int.TryParse(suffix, out var n) ? n : int.MaxValue);
        }
        return (3, 0);
    }

    private static DateTimeOffset TruncateToSecondUtc(DateTimeOffset value)
    {
        var u = value.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, u.Minute, u.Second, TimeSpan.Zero);
    }

    private static string RowKeyFor(DateTimeOffset ts) =>
        ts.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss");

    /// <summary>
    /// Journal whose time range covers <paramref name="ts"/> (work context).
    /// Used for timeline group headings only — not the same as a screenshot link.
    /// </summary>
    private (string label, JournalEntryResponse? entry) ResolveCoveringJournal(
        List<JournalEntryResponse> journals, DateTimeOffset ts)
    {
        var covering = journals
            .Where(j => j.StartUtc <= ts && (j.StopUtc is null || j.StopUtc >= ts))
            .OrderByDescending(j => j.StartUtc)
            .FirstOrDefault();
        if (covering is not null)
            return (FormatJournalOption(covering), covering);
        return ("None", null);
    }

    /// <summary>
    /// Journals explicitly linked on screenshots in a capture batch (per-image FK).
    /// </summary>
    private (string label, string? primaryId, string? primaryTargetId, string targetLabel, bool mixed)
        ResolveLinkedJournals(List<ScreenshotResponse> shots)
    {
        var linkedIds = shots
            .Select(s => s.JournalEntryId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (linkedIds.Count == 0)
            return ("None", null, null, "—", false);

        var mixed = linkedIds.Count > 1;
        var first = linkedIds[0];
        if (!JournalsById.TryGetValue(first, out var entry))
        {
            var idText = Uuid7Text.Format(first);
            return (idText, idText, null, "—", mixed);
        }

        var label = FormatJournalOption(entry);
        if (mixed)
            label += " (+mixed)";
        var targetLabel = JournalLabelFormatter.FormatTarget(
            entry.JournalTargetId, TargetsById, _customersById, _projectsById, _tasksById);
        if (mixed)
            targetLabel += " (+mixed)";

        return (
            label,
            Uuid7Text.Format(entry.Id),
            Uuid7Text.Format(entry.JournalTargetId),
            targetLabel,
            mixed);
    }

    private string FormatJournalOption(JournalEntryResponse j) =>
        JournalLabelFormatter.Format(
            j, MachineNames, TargetsById, _customersById, _projectsById, _tasksById, FormatProfileDateTime);

    /// <summary>Display label for a screenshot's linked journal (empty if none).</summary>
    public string JournalLabelFor(Uuid7? journalEntryId)
    {
        if (journalEntryId is not { } jid) return "";
        if (JournalsById.TryGetValue(jid, out var j)) return FormatJournalOption(j);
        return JournalOptions.FirstOrDefault(o => o.Id == Uuid7Text.Format(jid))?.Label ?? "";
    }

    /// <summary>Time-covering journal label at an instant (for viewer context when shot has no link).</summary>
    public string CoveringJournalLabelAt(DateTimeOffset utc)
    {
        var (label, _) = ResolveCoveringJournal(JournalsById.Values.ToList(), utc);
        return label;
    }

    public string? CoveringJournalIdAt(DateTimeOffset utc)
    {
        var (_, entry) = ResolveCoveringJournal(JournalsById.Values.ToList(), utc);
        return entry is null ? null : Uuid7Text.Format(entry.Id);
    }

    private JournalEditorsViewModel BuildJournalEditors(string pagePath, bool dateHidden)
    {
        var hidden = new Dictionary<string, string?>();
        if (dateHidden)
        {
            hidden["date"] = Date;
            hidden["monitor"] = MonitorFilter;
            hidden["Machine"] = MachineFilter;
            hidden["FromTime"] = FromTimeFilter;
            hidden["ToTime"] = ToTimeFilter;
            hidden["Journal"] = JournalFilter;
            hidden["TagsQ"] = TagsFilter;
            hidden["Customer"] = CustomerFilter;
            hidden["Project"] = ProjectFilter;
            hidden["Task"] = TaskFilter;
            hidden["GroupBy"] = GroupByNormalized;
            hidden["Sort"] = SortBy;
            hidden["Dir"] = SortDir;
        }

        var journalPayload = new Dictionary<string, object?>();
        foreach (var j in JournalsById.Values)
        {
            var id = Uuid7Text.Format(j.Id);
            journalPayload[id] = new
            {
                targetId = Uuid7Text.Format(j.JournalTargetId),
                startLocal = ToProfileLocal(j.StartUtc).ToString("yyyy-MM-ddTHH:mm:ss"),
                stopLocal = j.StopUtc is { } s ? ToProfileLocal(s).ToString("yyyy-MM-ddTHH:mm:ss") : "",
                tags = j.Tags ?? "",
                notes = j.Notes ?? "",
                label = FormatJournalOption(j)
            };
        }

        var targetPayload = new Dictionary<string, object?>();
        foreach (var t in TargetsById.Values)
        {
            var id = Uuid7Text.Format(t.Id);
            targetPayload[id] = new
            {
                customerId = Uuid7Text.Format(t.CustomerId),
                projectId = Uuid7Text.Format(t.ProjectId),
                taskId = Uuid7Text.Format(t.TaskId),
                code = t.Code ?? "",
                name = t.Name ?? "",
                displayName = JournalLabelFormatter.FormatTargetName(
                    t.Id, TargetsById, _customersById, _projectsById, _tasksById),
                tags = t.Tags ?? "",
                notes = t.Notes ?? "",
                label = JournalLabelFormatter.FormatTarget(
                    t.Id, TargetsById, _customersById, _projectsById, _tasksById)
            };
        }

        var data = JsonSerializer.Serialize(new { journals = journalPayload, targets = targetPayload });

        var customers = _customers
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => JournalLabelFormatter.FromListItem(c))
            .ToList();
        if (!customers.Any(c => c.IsNone))
            customers.Insert(0, JournalLabelFormatter.NoneOption());

        // NONE is always offered for project/task (no parent filter).
        var projects = _projects
            .Where(p => p.Id != Uuid7.Empty
                && !string.Equals(p.Code, "-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => JournalLabelFormatter.FromListItem(
                p, p.CustomerId is { } cid ? Uuid7Text.Format(cid) : null))
            .ToList();
        projects.Insert(0, JournalLabelFormatter.NoneOption());

        var tasks = _tasks
            .Where(t => t.Id != Uuid7.Empty
                && !string.Equals(t.Code, "-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => JournalLabelFormatter.FromListItem(
                t, t.ProjectId is { } pid ? Uuid7Text.Format(pid) : null))
            .ToList();
        tasks.Insert(0, JournalLabelFormatter.NoneOption());

        return new JournalEditorsViewModel
        {
            PagePath = pagePath,
            ProfileTimeZoneId = ProfileTimeZoneId,
            Journals = JournalOptions.Select(j => new JournalOptionItem { Id = j.Id, Label = j.Label }).ToList(),
            Targets = TargetsById.Values
                .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new ListOptionItem
                {
                    Id = Uuid7Text.Format(t.Id),
                    Code = t.Code ?? "",
                    Name = JournalLabelFormatter.FormatTargetName(
                        t.Id, TargetsById, _customersById, _projectsById, _tasksById),
                    Label = JournalLabelFormatter.FormatTarget(
                        t.Id, TargetsById, _customersById, _projectsById, _tasksById)
                }).ToList(),
            Customers = customers,
            Projects = projects,
            Tasks = tasks,
            Machines = Machines
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new ListOptionItem
                {
                    Id = Uuid7Text.Format(m.Id),
                    Label = m.Name,
                    Name = m.Name,
                    Code = m.Code ?? ""
                }).ToList(),
            DataJson = data,
            HiddenFields = hidden
        };
    }

    public string MachineLabel(Uuid7 machineId) =>
        MachineNames.TryGetValue(machineId, out var n) ? n : Uuid7Text.Format(machineId)[..8] + "…";

    /// <summary>UTC → profile timezone wall clock.</summary>
    public DateTime ToProfileLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, ProfileTimeZone);

    /// <summary>24h time only in profile timezone (e.g. <c>14:30:00</c>) — day is shown in filters/nav.</summary>
    public string FormatProfileTime(DateTimeOffset utc) =>
        ToProfileLocal(utc).ToString("HH:mm:ss");

    /// <summary>Full weekday name in profile timezone (e.g. <c>Monday</c>).</summary>
    public string FormatProfileWeekday(DateTimeOffset utc) =>
        ToProfileLocal(utc).ToString("dddd");

    /// <summary>Date + 24h time in profile timezone.</summary>
    public string FormatProfileDateTime(DateTimeOffset utc) =>
        ToProfileLocal(utc).ToString("yyyy-MM-dd HH:mm:ss");

    private TimeZoneInfo ProfileTimeZone =>
        _profileTimeZone ??= ResolveTimeZone(ProfileTimeZoneId);

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// Customer / project / task labels + customer colour for a row from covering journal,
    /// else linked target id.
    /// </summary>
    private (string customer, string project, string task, string? colour) ResolveCptLabels(
        JournalEntryResponse? covering, string? linkedTargetId)
    {
        Uuid7? targetId = null;
        if (covering is not null)
            targetId = covering.JournalTargetId;
        else if (!string.IsNullOrWhiteSpace(linkedTargetId)
                 && Uuid7Text.TryParse(linkedTargetId, out var tid))
            targetId = tid;

        if (targetId is null || !TargetsById.TryGetValue(targetId.Value, out var target))
            return ("—", "—", "—", null);

        string Lab(Uuid7 id, Dictionary<Uuid7, ListItemResponse> map)
        {
            if (map.TryGetValue(id, out var item))
                return FormatListLabel(item.Code, item.Name);
            return "(none)";
        }

        string? colour = null;
        if (_customersById.TryGetValue(target.CustomerId, out var cust))
            colour = NormalizeColour(cust.ColourCode);

        return (
            Lab(target.CustomerId, _customersById),
            Lab(target.ProjectId, _projectsById),
            Lab(target.TaskId, _tasksById),
            colour);
    }

    /// <summary>Normalize customer colour for CSS (hex with leading #, or named colour).</summary>
    public static string? NormalizeColour(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour)) return null;
        var c = colour.Trim();
        if (c.StartsWith('#')) return c.Length is 4 or 7 ? c : null;
        if (c.Length is 3 or 6 && c.All(ch => Uri.IsHexDigit(ch)))
            return "#" + c;
        // Allow simple CSS colour names
        if (c.All(ch => char.IsLetter(ch)) && c.Length is >= 3 and <= 20)
            return c.ToLowerInvariant();
        return null;
    }

    private static string FormatListLabel(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) return "(none)";
        if (string.IsNullOrWhiteSpace(code)) return name;
        if (string.IsNullOrWhiteSpace(name)) return code;
        if (string.Equals(code, name, StringComparison.Ordinal)) return name;
        return $"{code} — {name}";
    }

    public string SortHref(string column)
    {
        // Toggle when re-clicking the active column; otherwise start descending (newest / Z-A first).
        var nextDir = string.Equals(SortBy, column, StringComparison.OrdinalIgnoreCase)
            ? (SortDir == "desc" ? "asc" : "desc")
            : "desc";
        var parts = new List<string> { $"date={Uri.EscapeDataString(Date)}" };
        if (!string.IsNullOrWhiteSpace(MonitorFilter))
            parts.Add($"monitor={Uri.EscapeDataString(MonitorFilter)}");
        if (!string.IsNullOrWhiteSpace(MachineFilter))
            parts.Add($"Machine={Uri.EscapeDataString(MachineFilter)}");
        if (!string.IsNullOrWhiteSpace(FromTimeFilter))
            parts.Add($"FromTime={Uri.EscapeDataString(FromTimeFilter)}");
        if (!string.IsNullOrWhiteSpace(ToTimeFilter))
            parts.Add($"ToTime={Uri.EscapeDataString(ToTimeFilter)}");
        if (!string.IsNullOrWhiteSpace(JournalFilter))
            parts.Add($"Journal={Uri.EscapeDataString(JournalFilter)}");
        if (!string.IsNullOrWhiteSpace(TagsFilter))
            parts.Add($"TagsQ={Uri.EscapeDataString(TagsFilter)}");
        if (!string.IsNullOrWhiteSpace(CustomerFilter))
            parts.Add($"Customer={Uri.EscapeDataString(CustomerFilter)}");
        if (!string.IsNullOrWhiteSpace(ProjectFilter))
            parts.Add($"Project={Uri.EscapeDataString(ProjectFilter)}");
        if (!string.IsNullOrWhiteSpace(TaskFilter))
            parts.Add($"Task={Uri.EscapeDataString(TaskFilter)}");
        if (!string.IsNullOrWhiteSpace(GroupByNormalized) && GroupByNormalized != "journal")
            parts.Add($"GroupBy={Uri.EscapeDataString(GroupByNormalized)}");
        parts.Add($"Sort={Uri.EscapeDataString(column)}");
        parts.Add($"Dir={nextDir}");
        return "/?" + string.Join("&", parts);
    }

    /// <summary>
    /// Sets gap markers and group headings. After <see cref="OrderRowsForDisplay"/>, each group key
    /// is contiguous, so a heading appears once at the start of each group.
    /// </summary>
    private void ApplyGroupHeadings(List<TimelineRow> rows)
    {
        var flat = GroupByNormalized is "none" or null or "";
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.GroupHeadingLabel = GroupHeadingLabelFor(row);
            row.GroupHeadingJournalId = GroupByNormalized == "journal" ? row.CoveringJournalId : null;

            if (flat)
            {
                row.ShowGroupHeading = false;
                // Gaps only make sense when walking the day in time order.
                if (i == 0 || SortBy != "time")
                {
                    row.ShowGapBefore = false;
                    continue;
                }
                var gapMinutes = Math.Abs((row.Timestamp - rows[i - 1].Timestamp).TotalMinutes);
                row.ShowGapBefore = gapMinutes >= GapThresholdMinutes;
                continue;
            }

            var curKey = GroupKey(row);
            if (i == 0)
            {
                row.ShowGapBefore = false;
                row.ShowGroupHeading = true;
                continue;
            }

            var prevKey = GroupKey(rows[i - 1]);
            var keyChanged = !string.Equals(prevKey, curKey, StringComparison.Ordinal);
            row.ShowGroupHeading = keyChanged;

            // Within a group, mark long time gaps; never between different groups.
            if (keyChanged || SortBy != "time")
            {
                row.ShowGapBefore = false;
            }
            else
            {
                var gapMinutes = Math.Abs((row.Timestamp - rows[i - 1].Timestamp).TotalMinutes);
                row.ShowGapBefore = gapMinutes >= GapThresholdMinutes;
            }
        }
    }

    private string GroupKey(TimelineRow r) => GroupByNormalized switch
    {
        "none" => "",
        "machine" => r.MachineLabel ?? "—",
        "customer" => r.CustomerLabel ?? "—",
        "project" or "customer-project" => r.CustomerLabel + " › " + r.ProjectLabel,
        "task" or "customer-project-task" => r.CustomerLabel + " › " + r.ProjectLabel + " › " + r.TaskLabel,
        // Default: covering journal id (stable key); empty when none covering.
        _ => r.CoveringJournalId ?? ""
    };

    private string GroupHeadingLabelFor(TimelineRow r) => GroupByNormalized switch
    {
        "none" => "",
        "machine" => string.IsNullOrWhiteSpace(r.MachineLabel) ? "—" : r.MachineLabel,
        "customer" => string.IsNullOrWhiteSpace(r.CustomerLabel) ? "—" : r.CustomerLabel,
        "project" or "customer-project" => r.CustomerLabel + " › " + r.ProjectLabel,
        "task" or "customer-project-task" => r.CustomerLabel + " › " + r.ProjectLabel + " › " + r.TaskLabel,
        _ => r.CoveringJournalLabel
    };

    public string SortIndicator(string column)
    {
        if (!string.Equals(SortBy, column, StringComparison.OrdinalIgnoreCase)) return "";
        return SortDir == "desc" ? " ↓" : " ↑";
    }

    private static string? FirstNonEmpty(IEnumerable<string?> values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static List<string> DistinctNonEmpty(IEnumerable<string?> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public sealed class MachineFilterOption
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
    }

    public sealed class TimelineRow
    {
        public string Key { get; init; } = "";
        public DateTimeOffset Timestamp { get; init; }
        public bool ShowGapBefore { get; set; }
        /// <summary>Start a group heading above this row (group key changed).</summary>
        public bool ShowGroupHeading { get; set; }
        /// <summary>Label shown in the group heading for the active group-by mode.</summary>
        public string GroupHeadingLabel { get; set; } = "";
        /// <summary>When grouping by covering journal, id for the open-journal link.</summary>
        public string? GroupHeadingJournalId { get; set; }
        /// <summary>Header → one or more shots in that column (same second, same capture kind).</summary>
        public Dictionary<string, List<ScreenshotResponse>> ShotsByColumn { get; init; } = new();
        public List<string> ShotIds { get; init; } = new();
        public string MachineLabel { get; init; } = "—";
        /// <summary>Aggregate of per-image linked journals (not time-covering).</summary>
        public string JournalLabel { get; init; } = "None";
        public string? JournalEntryId { get; init; }
        public string? JournalTargetId { get; init; }
        public string JournalTargetLabel { get; init; } = "—";
        public string JournalTargetSort { get; init; } = "";
        /// <summary>C/P/T labels from covering (preferred) or linked journal target.</summary>
        public string CustomerLabel { get; init; } = "—";
        public string ProjectLabel { get; init; } = "—";
        public string TaskLabel { get; init; } = "—";
        /// <summary>Customer <c>colour_code</c> for row highlight (normalized CSS colour).</summary>
        public string? CustomerColour { get; init; }
        /// <summary>Time-covering journal for group heading / work context.</summary>
        public string? CoveringJournalId { get; init; }
        public string CoveringJournalLabel { get; init; } = "None";
        public string? Tags { get; init; }
        public string? Notes { get; init; }
        public bool MixedJournal { get; init; }
        public bool MixedTags { get; init; }
        public bool MixedNotes { get; init; }
    }

    public sealed class JournalOption
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
    }
}
