using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Models;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Journals;

/// <summary>Full journal editor: times, C/P/T, tags, notes.</summary>
public sealed class EditModel : PageModel
{
    private readonly ApiClient _api;

    public EditModel(ApiClient api) => _api = api;

    public string JournalId { get; private set; } = "";
    public string MachineLabel { get; private set; } = "—";
    public string ProfileTimeZoneId { get; private set; } = "UTC";
    public bool IsOpen { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public List<ListOptionItem> Customers { get; private set; } = new();
    public List<ListOptionItem> Projects { get; private set; } = new();
    public List<ListOptionItem> Tasks { get; private set; } = new();

    [BindProperty]
    public string? StartLocal { get; set; }

    [BindProperty]
    public string? StopLocal { get; set; }

    [BindProperty]
    public string? CustomerId { get; set; }

    [BindProperty]
    public string? ProjectId { get; set; }

    [BindProperty]
    public string? TaskId { get; set; }

    [BindProperty]
    public string? Tags { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    private TimeZoneInfo? _profileTimeZone;
    private Dictionary<Uuid7, JournalTargetResponse> _targetsById = new();
    private Dictionary<Uuid7, ListItemResponse> _customersById = new();
    private Dictionary<Uuid7, ListItemResponse> _projectsById = new();
    private Dictionary<Uuid7, ListItemResponse> _tasksById = new();
    private Dictionary<Uuid7, string> _machineNames = new();

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(id, ct, populate: true)) return NotFound();
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(string id, CancellationToken ct) =>
        SaveCoreAsync(id, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(string id, CancellationToken ct) =>
        SaveCoreAsync(id, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(string id, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!Uuid7Text.TryParse(id, out var jid)) return NotFound();
        await EnsureProfileTimeZoneAsync(ct);

        var startUtc = ParseProfileLocal(StartLocal);
        if (startUtc is null)
        {
            Error = "Started at is required.";
            await LoadAsync(id, ct, populate: false);
            return Page();
        }

        if (!TryParseUuid(CustomerId, out var customerId)
            || !TryParseUuid(ProjectId, out var projectId)
            || !TryParseUuid(TaskId, out var taskId))
        {
            Error = "Customer, project, and task are required (use NONE if empty).";
            await LoadAsync(id, ct, populate: false);
            return Page();
        }

        var (target, targetError) = await _api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
        {
            CustomerId = customerId,
            ProjectId = projectId,
            TaskId = taskId
        }, ct);
        if (targetError is not null || target is null)
        {
            Error = targetError ?? "Could not resolve journal target.";
            await LoadAsync(id, ct, populate: false);
            return Page();
        }

        var stopUtc = ParseProfileLocal(StopLocal);
        var (_, error) = await _api.UpdateJournalAsync(jid, new UpdateJournalRequest
        {
            StartUtc = startUtc,
            StopUtc = stopUtc,
            JournalTargetId = target.Id,
            Tags = Tags ?? "",
            Notes = Notes ?? ""
        }, ct);
        if (error is not null)
        {
            Error = error;
            await LoadAsync(id, ct, populate: false);
            return Page();
        }

        if (close) return RedirectToPage("/Journals/Index");
        Message = "Saved.";
        await LoadAsync(id, ct, populate: true);
        return Page();
    }

    private async Task EnsureProfileTimeZoneAsync(CancellationToken ct)
    {
        if (_profileTimeZone is not null) return;
        var me = await _api.GetMeAsync(ct);
        if (me is not null)
            ProfileTimeZoneId = string.IsNullOrWhiteSpace(me.TimeZoneId) ? "UTC" : me.TimeZoneId;
        _profileTimeZone = ResolveTimeZone(ProfileTimeZoneId);
    }

    private async Task<bool> LoadAsync(string id, CancellationToken ct, bool populate)
    {
        if (!Uuid7Text.TryParse(id, out var jid)) return false;

        await EnsureProfileTimeZoneAsync(ct);

        var entries = await _api.ListJournalsAsync(ct) ?? new();
        var entry = entries.FirstOrDefault(e => e.Id == jid);
        if (entry is null) return false;

        JournalId = Uuid7Text.Format(entry.Id);
        IsOpen = entry.IsOpen;

        var machines = await _api.ListMachinesAsync(ct) ?? new();
        _machineNames = machines.ToDictionary(m => m.Id, m => m.Name);
        MachineLabel = _machineNames.TryGetValue(entry.MachineId, out var mn) ? mn : "—";

        _targetsById = (await _api.ListJournalTargetsAsync(ct) ?? new()).ToDictionary(t => t.Id, t => t);
        var customers = await _api.ListItemsAsync("customers", ct) ?? new();
        var projects = await _api.ListItemsAsync("projects", ct) ?? new();
        var tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        _customersById = customers.ToDictionary(c => c.Id);
        _projectsById = projects.ToDictionary(p => p.Id);
        _tasksById = tasks.ToDictionary(t => t.Id);

        Customers = BuildCustomerOptions(customers);
        Projects = BuildProjectOptions(projects);
        Tasks = BuildTaskOptions(tasks);

        if (populate)
        {
            StartLocal = ToProfileLocal(entry.StartUtc).ToString("yyyy-MM-ddTHH:mm:ss");
            StopLocal = entry.StopUtc is { } s ? ToProfileLocal(s).ToString("yyyy-MM-ddTHH:mm:ss") : "";
            Tags = entry.Tags ?? "";
            Notes = entry.Notes ?? "";
            if (_targetsById.TryGetValue(entry.JournalTargetId, out var target))
            {
                CustomerId = Uuid7Text.Format(target.CustomerId);
                ProjectId = Uuid7Text.Format(target.ProjectId);
                TaskId = Uuid7Text.Format(target.TaskId);
            }
            else
            {
                CustomerId = Uuid7Text.Format(Uuid7.Empty);
                ProjectId = Uuid7Text.Format(Uuid7.Empty);
                TaskId = Uuid7Text.Format(Uuid7.Empty);
            }
        }

        return true;
    }

    private static List<ListOptionItem> BuildCustomerOptions(List<ListItemResponse> customers)
    {
        var list = customers
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => JournalLabelFormatter.FromListItem(c))
            .ToList();
        if (!list.Any(c => c.IsNone))
            list.Insert(0, JournalLabelFormatter.NoneOption());
        return list;
    }

    private static List<ListOptionItem> BuildProjectOptions(List<ListItemResponse> projects)
    {
        var list = projects
            .Where(p => p.Id != Uuid7.Empty
                && !string.Equals(p.Code, "NONE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => JournalLabelFormatter.FromListItem(
                p, p.CustomerId is { } cid ? Uuid7Text.Format(cid) : null))
            .ToList();
        list.Insert(0, JournalLabelFormatter.NoneOption());
        return list;
    }

    private static List<ListOptionItem> BuildTaskOptions(List<ListItemResponse> tasks)
    {
        var list = tasks
            .Where(t => t.Id != Uuid7.Empty
                && !string.Equals(t.Code, "NONE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => JournalLabelFormatter.FromListItem(
                t, t.ProjectId is { } pid ? Uuid7Text.Format(pid) : null))
            .ToList();
        list.Insert(0, JournalLabelFormatter.NoneOption());
        return list;
    }

    private DateTime ToProfileLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, _profileTimeZone ?? TimeZoneInfo.Utc);

    private DateTimeOffset? ParseProfileLocal(string? local)
    {
        if (string.IsNullOrWhiteSpace(local)) return null;
        if (!DateTime.TryParse(local, out var dt)) return null;
        try
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(dt, DateTimeKind.Unspecified),
                _profileTimeZone ?? TimeZoneInfo.Utc);
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }
        catch
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        }
    }

    private static bool TryParseUuid(string? text, out Uuid7 id)
    {
        id = default;
        return !string.IsNullOrWhiteSpace(text) && Uuid7Text.TryParse(text, out id);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
