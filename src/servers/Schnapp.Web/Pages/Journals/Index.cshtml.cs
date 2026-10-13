using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Models;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Journals;

/// <summary>
/// Journals list: start/stop and an in-place editable grid (times + customer/project/task),
/// with report-style filters/group/sort and multi-row bulk edit/delete.
/// </summary>
public sealed class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api) => _api = api;

    public List<JournalRow> Rows { get; private set; } = new();
    public List<JournalSection> Sections { get; private set; } = new();
    public List<MachineResponse> Machines { get; private set; } = new();
    public Dictionary<Uuid7, string> MachineNames { get; private set; } = new();
    public List<JournalOptionItem> JournalLabels { get; private set; } = new();
    public List<ListOptionItem> Customers { get; private set; } = new();
    public List<ListOptionItem> Projects { get; private set; } = new();
    public List<ListOptionItem> Tasks { get; private set; } = new();
    /// <summary>Combo-box options with C›P›T paths (filter headers).</summary>
    public List<FilterOption> FilterCustomers { get; private set; } = new();
    public List<FilterOption> FilterProjects { get; private set; } = new();
    public List<FilterOption> FilterTasks { get; private set; } = new();
    public List<FilterOption> FilterMachines { get; private set; } = new();
    public JournalEditorsViewModel JournalEditors { get; private set; } = new();
    public string ProfileTimeZoneId { get; private set; } = "UTC";
    public string FromDate { get; private set; } = "";
    public string ToDate { get; private set; } = "";
    public string GroupByNormalized { get; private set; } = "none";
    public string SortByNormalized { get; private set; } = "date";
    public string SortDirNormalized { get; private set; } = "desc";
    /// <summary>Default "now" for the Start journal dialog (profile-local).</summary>
    public string DefaultStartLocal { get; private set; } = "";
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public List<QuickRange> QuickRanges { get; private set; } = new();

    public static IReadOnlyList<FilterOption> GroupByOptions { get; } = new[]
    {
        new FilterOption("none", "None (flat)"),
        new FilterOption("machine", "Machine"),
        new FilterOption("customer", "Customer"),
        new FilterOption("project", "Project"),
        new FilterOption("task", "Task"),
        new FilterOption("customer-project", "Customer › Project"),
        new FilterOption("customer-project-task", "Customer › Project › Task")
    };

    public static IReadOnlyList<FilterOption> SortByOptions { get; } = new[]
    {
        new FilterOption("date", "Started"),
        new FilterOption("machine", "Machine"),
        new FilterOption("customer", "Customer"),
        new FilterOption("project", "Project"),
        new FilterOption("task", "Task")
    };
    private TimeZoneInfo? _profileTimeZone;
    private Dictionary<Uuid7, JournalTargetResponse> _targetsById = new();
    private Dictionary<Uuid7, ListItemResponse> _customersById = new();
    private Dictionary<Uuid7, ListItemResponse> _projectsById = new();
    private Dictionary<Uuid7, ListItemResponse> _tasksById = new();
    private List<ListItemResponse> _customers = new();
    private List<ListItemResponse> _projects = new();
    private List<ListItemResponse> _tasks = new();

    [BindProperty]
    public Uuid7Param MachineId { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public string? Tags { get; set; }

    [BindProperty]
    public string? RowId { get; set; }

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

    // --- Dialog editor binds (unchanged partials) ---

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

    // --- GET filters / group / sort ---
    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IncCustomer { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IncProject { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IncTask { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IncMachine { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ExcCustomer { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ExcProject { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ExcTask { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ExcMachine { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? GroupBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SortBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SortDir { get; set; }

    // --- Bulk ---
    [BindProperty]
    public List<string> SelectedIds { get; set; } = new();

    [BindProperty]
    public bool BulkSetCustomer { get; set; }

    [BindProperty]
    public bool BulkSetProject { get; set; }

    [BindProperty]
    public bool BulkSetTask { get; set; }

    [BindProperty]
    public string? BulkCustomerId { get; set; }

    [BindProperty]
    public string? BulkProjectId { get; set; }

    [BindProperty]
    public string? BulkTaskId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var error = await _api.DeleteJournalAsync(id, ct);
        if (error is not null) Error = error;
        else Message = "Journal deleted.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostBulkDeleteAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var ids = ParseSelectedIds();
        if (ids.Count == 0)
        {
            Error = "Select at least one journal.";
            await LoadAsync(ct);
            return Page();
        }

        var deleted = 0;
        foreach (var id in ids)
        {
            var error = await _api.DeleteJournalAsync(id, ct);
            if (error is not null)
            {
                Error = error;
                await LoadAsync(ct);
                return Page();
            }
            deleted++;
        }
        Message = $"Deleted {deleted} journal(s).";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostBulkUpdateAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var ids = ParseSelectedIds();
        if (ids.Count == 0)
        {
            Error = "Select at least one journal.";
            await LoadAsync(ct);
            return Page();
        }
        if (!BulkSetCustomer && !BulkSetProject && !BulkSetTask)
        {
            Error = "Choose at least one field to update.";
            await LoadAsync(ct);
            return Page();
        }

        // Resolve target for each row: merge bulk C/P/T over existing target triple.
        await EnsureProfileTimeZoneAsync(ct);
        var entries = await _api.ListJournalsAsync(ct) ?? new();
        var targets = (await _api.ListJournalTargetsAsync(ct) ?? new()).ToDictionary(t => t.Id, t => t);
        var updated = 0;
        foreach (var id in ids)
        {
            var entry = entries.FirstOrDefault(e => e.Id.Equals(id));
            if (entry is null) continue;
            targets.TryGetValue(entry.JournalTargetId, out var existing);
            var cust = existing?.CustomerId ?? WellKnownIds.None;
            var proj = existing?.ProjectId ?? WellKnownIds.None;
            var task = existing?.TaskId ?? WellKnownIds.None;
            if (BulkSetCustomer && TryParseUuid(BulkCustomerId, out var bc)) cust = bc;
            if (BulkSetProject && TryParseUuid(BulkProjectId, out var bp)) proj = bp;
            if (BulkSetTask && TryParseUuid(BulkTaskId, out var bt)) task = bt;

            var (target, targetError) = await _api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
            {
                CustomerId = cust,
                ProjectId = proj,
                TaskId = task
            }, ct);
            if (targetError is not null || target is null)
            {
                Error = targetError ?? "Could not resolve journal target.";
                await LoadAsync(ct);
                return Page();
            }
            var (_, error) = await _api.UpdateJournalAsync(id, new UpdateJournalRequest
            {
                JournalTargetId = target.Id
            }, ct);
            if (error is not null)
            {
                Error = error;
                await LoadAsync(ct);
                return Page();
            }
            updated++;
        }
        Message = $"Updated {updated} journal(s).";
        await LoadAsync(ct);
        return Page();
    }

    private List<Uuid7> ParseSelectedIds()
    {
        var list = new List<Uuid7>();
        foreach (var s in SelectedIds)
        {
            if (Uuid7Text.TryParse(s, out var id))
                list.Add(id);
        }
        return list;
    }

    /// <summary>Query string for quick-range links (preserves filters/group/sort).</summary>
    public string FilterQuery(string from, string to)
    {
        var parts = new List<string>
        {
            "From=" + Uri.EscapeDataString(from),
            "To=" + Uri.EscapeDataString(to)
        };
        void Cpt(string? value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(name + "=" + Uri.EscapeDataString(value.Trim()));
        }
        Cpt(IncCustomer, "IncCustomer");
        Cpt(IncProject, "IncProject");
        Cpt(IncTask, "IncTask");
        Cpt(IncMachine, "IncMachine");
        Cpt(ExcCustomer, "ExcCustomer");
        Cpt(ExcProject, "ExcProject");
        Cpt(ExcTask, "ExcTask");
        Cpt(ExcMachine, "ExcMachine");
        if (GroupByNormalized != "none")
            parts.Add("GroupBy=" + Uri.EscapeDataString(GroupByNormalized));
        if (SortByNormalized != "date")
            parts.Add("SortBy=" + Uri.EscapeDataString(SortByNormalized));
        parts.Add("SortDir=" + Uri.EscapeDataString(SortDirNormalized));
        return string.Join("&", parts);
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await EnsureProfileTimeZoneAsync(ct);

        Uuid7? targetId = null;
        if (TryParseUuid(CustomerId, out var customerId)
            && TryParseUuid(ProjectId, out var projectId)
            && TryParseUuid(TaskId, out var taskId))
        {
            var (target, targetError) = await _api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
            {
                CustomerId = customerId,
                ProjectId = projectId,
                TaskId = taskId
            }, ct);
            if (targetError is not null)
            {
                Error = targetError;
                await LoadAsync(ct);
                return Page();
            }
            targetId = target?.Id;
        }

        var startUtc = ParseProfileLocal(StartLocal);
        var (entry, error) = await _api.StartJournalAsync(new StartJournalRequest
        {
            MachineId = MachineId,
            JournalTargetId = targetId,
            Notes = Notes,
            Tags = Tags,
            StartUtc = startUtc
        }, ct);
        if (error is not null) Error = error;
        else Message = $"Started journal {Uuid7Text.Format(entry!.Id)}.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostStopAsync(Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        var (entry, error) = await _api.StopJournalAsync(id, ct);
        if (error is not null) Error = error;
        else Message = $"Stopped journal {Uuid7Text.Format(entry!.Id)}.";
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>In-place grid save: times + customer/project/task (get-or-create target).</summary>
    public async Task<IActionResult> OnPostSaveRowAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await EnsureProfileTimeZoneAsync(ct);
        if (string.IsNullOrWhiteSpace(RowId) || !Uuid7Text.TryParse(RowId, out var jid))
        {
            Error = "Missing journal id.";
            await LoadAsync(ct);
            return Page();
        }

        var startUtc = ParseProfileLocal(StartLocal);
        if (startUtc is null)
        {
            Error = "Started at is required.";
            await LoadAsync(ct);
            return Page();
        }

        if (!TryParseUuid(CustomerId, out var customerId)
            || !TryParseUuid(ProjectId, out var projectId)
            || !TryParseUuid(TaskId, out var taskId))
        {
            Error = "Customer, project, and task are required (use NONE if empty).";
            await LoadAsync(ct);
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
            await LoadAsync(ct);
            return Page();
        }

        var stopUtc = ParseProfileLocal(StopLocal);
        var (_, error) = await _api.UpdateJournalAsync(jid, new UpdateJournalRequest
        {
            StartUtc = startUtc,
            StopUtc = stopUtc,
            JournalTargetId = target.Id
        }, ct);
        if (error is not null) Error = error;
        else Message = "Journal saved.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveJournalAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await EnsureProfileTimeZoneAsync(ct);

        Uuid7? targetId = null;
        if (!string.IsNullOrWhiteSpace(EditJournalTargetId) && Uuid7Text.TryParse(EditJournalTargetId, out var tid))
            targetId = tid;

        var startUtc = ParseProfileLocal(EditJournalStartLocal);
        var stopUtc = ParseProfileLocal(EditJournalStopLocal);
        var isNew = string.Equals(EditJournalIsNew, "true", StringComparison.OrdinalIgnoreCase);

        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(EditJournalMachineId) || !Uuid7Text.TryParse(EditJournalMachineId, out var machineId))
            {
                Error = "Machine is required for a new journal.";
                await LoadAsync(ct);
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
                await LoadAsync(ct);
                return Page();
            }
            if (stopUtc is not null || targetId is not null || startUtc is not null)
            {
                var (_, updErr) = await _api.UpdateJournalAsync(created.Id, new UpdateJournalRequest
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
                    await LoadAsync(ct);
                    return Page();
                }
            }
            Message = "Journal created.";
            await LoadAsync(ct);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(EditJournalId) || !Uuid7Text.TryParse(EditJournalId, out var jid))
        {
            Error = "Select a journal.";
            await LoadAsync(ct);
            return Page();
        }

        if (startUtc is null)
        {
            Error = "Started at is required.";
            await LoadAsync(ct);
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
        if (error is not null) Error = error;
        else Message = "Journal saved.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveJournalTargetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!TryParseUuid(EditTargetCustomerId, out var customerId)
            || !TryParseUuid(EditTargetProjectId, out var projectId)
            || !TryParseUuid(EditTargetTaskId, out var taskId))
        {
            Error = "Customer, project, and task are required.";
            await LoadAsync(ct);
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
            await LoadAsync(ct);
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
                await LoadAsync(ct);
                return Page();
            }
        }

        Message = "Journal target saved.";
        await LoadAsync(ct);
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

    private async Task LoadAsync(CancellationToken ct)
    {
        await EnsureProfileTimeZoneAsync(ct);
        var todayLocal = DateOnly.FromDateTime(ToProfileLocal(DateTimeOffset.UtcNow));
        QuickRanges = BuildQuickRanges(todayLocal);
        GroupByNormalized = NormalizeGroupBy(GroupBy);
        SortByNormalized = NormalizeSortBy(SortBy);
        SortDirNormalized = NormalizeSortDir(SortDir, SortByNormalized);
        GroupBy = GroupByNormalized;
        SortBy = SortByNormalized;
        SortDir = SortDirNormalized;

        if (!TryParseDate(To, out var toDate)) toDate = todayLocal;
        if (!TryParseDate(From, out var fromDate)) fromDate = toDate.AddDays(-6);
        FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        From = FromDate;
        To = ToDate;

        Machines = await _api.ListMachinesAsync(ct) ?? new();
        MachineNames = Machines.ToDictionary(m => m.Id, m => m.Name);
        if (MachineId == Uuid7.Empty && Machines.Count > 0)
            MachineId = Machines[0].Id;

        _targetsById = (await _api.ListJournalTargetsAsync(ct) ?? new()).ToDictionary(t => t.Id, t => t);
        _customers = await _api.ListItemsAsync("customers", ct) ?? new();
        _projects = await _api.ListItemsAsync("projects", ct) ?? new();
        _tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        _customersById = _customers.ToDictionary(c => c.Id);
        _projectsById = _projects.ToDictionary(p => p.Id);
        _tasksById = _tasks.ToDictionary(t => t.Id);

        Customers = BuildCustomerOptions();
        Projects = BuildProjectOptions();
        Tasks = BuildTaskOptions();
        BuildFilterComboOptions();
        ApplyDefaultExcludeIfFirstVisit();

        // Date + C/P/T filters applied in the API/DB (not post-filtered in memory).
        var journalFilters = BuildJournalFilterQuery();
        var entries = await _api.ListJournalsAsync(
            ct, fromDate, toDate, journalFilters, limit: 1000) ?? new();

        Rows = entries.Select(e =>
        {
            _targetsById.TryGetValue(e.JournalTargetId, out var target);
            var custId = target is null ? Uuid7Text.Format(Uuid7.Empty) : Uuid7Text.Format(target.CustomerId);
            var projId = target is null ? Uuid7Text.Format(Uuid7.Empty) : Uuid7Text.Format(target.ProjectId);
            var taskId = target is null ? Uuid7Text.Format(Uuid7.Empty) : Uuid7Text.Format(target.TaskId);
            string Lab(Uuid7 id, Dictionary<Uuid7, ListItemResponse> map)
            {
                if (map.TryGetValue(id, out var item))
                    return FormatListLabel(item.Code, item.Name);
                return "(none)";
            }
            var cLab = target is null ? "(none)" : Lab(target.CustomerId, _customersById);
            var pLab = target is null ? "(none)" : Lab(target.ProjectId, _projectsById);
            var tLab = target is null ? "(none)" : Lab(target.TaskId, _tasksById);
            var pPath = FormatPath(cLab, pLab);
            var tPath = FormatPath(cLab, pLab, tLab);
            string? custColour = null;
            if (target is not null && _customersById.TryGetValue(target.CustomerId, out var custItem))
                custColour = NormalizeColour(custItem.ColourCode);
            var startLocal = ToProfileLocal(e.StartUtc);
            return new JournalRow
            {
                Id = Uuid7Text.Format(e.Id),
                MachineLabel = MachineNames.TryGetValue(e.MachineId, out var mn) ? mn : "—",
                StartLocal = startLocal.ToString("yyyy-MM-ddTHH:mm:ss"),
                StopLocal = e.StopUtc is { } s ? ToProfileLocal(s).ToString("yyyy-MM-ddTHH:mm:ss") : "",
                StartDate = DateOnly.FromDateTime(startLocal),
                CustomerId = custId,
                ProjectId = projId,
                TaskId = taskId,
                CustomerLabel = cLab,
                ProjectLabel = pLab,
                TaskLabel = tLab,
                ProjectPath = pPath,
                TaskPath = tPath,
                CustomerColour = custColour,
                IsOpen = e.IsOpen
            };
        }).ToList();

        // Group/sort only (presentation); filters already applied in the query.
        Sections = BuildSections(Rows, GroupByNormalized, SortByNormalized, SortDirNormalized);

        JournalLabels = entries
            .Select(j => new JournalOptionItem
            {
                Id = Uuid7Text.Format(j.Id),
                Label = FormatJournal(j)
            })
            .ToList();

        JournalEditors = BuildJournalEditors(entries);
        DefaultStartLocal = ToProfileLocal(DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss");
        ViewData["Wide"] = true;
    }

    private void BuildFilterComboOptions()
    {
        var customerLabelById = _customers.ToDictionary(c => c.Id, c => FormatListLabel(c.Code, c.Name));
        var projectLabelById = _projects.ToDictionary(p => p.Id, p => FormatListLabel(p.Code, p.Name));
        string CLab(Uuid7? id) =>
            id is { } cid && customerLabelById.TryGetValue(cid, out var l) ? l : "(unknown)";
        string PLab(Uuid7? id) =>
            id is { } pid && projectLabelById.TryGetValue(pid, out var l) ? l : "(unknown)";

        FilterCustomers = _customers
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => new FilterOption(Uuid7Text.Format(c.Id), FormatListLabel(c.Code, c.Name)))
            .ToList();
        FilterProjects = _projects
            .OrderBy(p => CLab(p.CustomerId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(p => new FilterOption(
                Uuid7Text.Format(p.Id),
                FormatPath(CLab(p.CustomerId), FormatListLabel(p.Code, p.Name))))
            .ToList();
        FilterTasks = _tasks
            .OrderBy(t => CLab(t.CustomerId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => PLab(t.ProjectId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .Select(t => new FilterOption(
                Uuid7Text.Format(t.Id),
                FormatPath(CLab(t.CustomerId), PLab(t.ProjectId), FormatListLabel(t.Code, t.Name))))
            .ToList();
        FilterMachines = Machines
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new FilterOption(
                Uuid7Text.Format(m.Id),
                FormatListLabel(m.Code ?? "", m.Name)))
            .ToList();
    }

    private void ApplyDefaultExcludeIfFirstVisit()
    {
        if (Request.Query.ContainsKey("From")) return;
        var noneLabel = FilterCustomers.FirstOrDefault(c => c.Id == Uuid7Text.Format(WellKnownIds.None))?.Label
            ?? "(none)";
        if (string.IsNullOrWhiteSpace(ExcCustomer)) ExcCustomer = noneLabel;
    }

    private static bool HasFilterValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private TimeSummaryFilterQuery BuildJournalFilterQuery() => new()
    {
        IncludeCustomer = HasFilterValue(IncCustomer),
        IncludeCustomerValue = IncCustomer,
        IncludeProject = HasFilterValue(IncProject),
        IncludeProjectValue = IncProject,
        IncludeTask = HasFilterValue(IncTask),
        IncludeTaskValue = IncTask,
        IncludeMachine = HasFilterValue(IncMachine),
        IncludeMachineValue = IncMachine,
        ExcludeCustomer = HasFilterValue(ExcCustomer),
        ExcludeCustomerValue = ExcCustomer,
        ExcludeProject = HasFilterValue(ExcProject),
        ExcludeProjectValue = ExcProject,
        ExcludeTask = HasFilterValue(ExcTask),
        ExcludeTaskValue = ExcTask,
        ExcludeMachine = HasFilterValue(ExcMachine),
        ExcludeMachineValue = ExcMachine
    };

    public static List<JournalSection> BuildSections(
        IReadOnlyList<JournalRow> rows, string groupBy, string sortBy, string sortDir)
    {
        var ascending = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var sorted = SortRows(rows, sortBy, ascending).ToList();
        if (groupBy is "none" or null or "")
        {
            return new List<JournalSection>
            {
                new() { ShowHeader = false, ShowSubtotal = false, Rows = sorted }
            };
        }

        var ordered = ascending
            ? sorted.OrderBy(r => GroupKey(r, groupBy), StringComparer.OrdinalIgnoreCase)
            : sorted.OrderByDescending(r => GroupKey(r, groupBy), StringComparer.OrdinalIgnoreCase);
        ordered = ApplyThenBy(ordered, sortBy, ascending);

        var sections = ordered
            .GroupBy(r => GroupKey(r, groupBy), StringComparer.Ordinal)
            .Select(g => new JournalSection
            {
                HeaderLabel = g.Key,
                ShowHeader = true,
                ShowSubtotal = true,
                Rows = g.ToList()
            })
            .ToList();

        return sortBy is "machine" or "customer" or "project" or "task"
            || groupBy is "machine" or "customer" or "project" or "task"
            || groupBy.StartsWith("customer", StringComparison.Ordinal)
            ? (ascending
                ? sections.OrderBy(s => s.HeaderLabel ?? "", StringComparer.OrdinalIgnoreCase)
                : sections.OrderByDescending(s => s.HeaderLabel ?? "", StringComparer.OrdinalIgnoreCase)).ToList()
            : (ascending
                ? sections.OrderBy(s => s.Rows.Min(r => r.StartLocal), StringComparer.Ordinal)
                : sections.OrderByDescending(s => s.Rows.Max(r => r.StartLocal), StringComparer.Ordinal)).ToList();
    }

    private static IEnumerable<JournalRow> SortRows(IEnumerable<JournalRow> rows, string sortBy, bool ascending)
    {
        IOrderedEnumerable<JournalRow> o = sortBy switch
        {
            "customer" => ascending
                ? rows.OrderBy(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase),
            "project" => ascending
                ? rows.OrderBy(r => r.ProjectPath, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.ProjectPath, StringComparer.OrdinalIgnoreCase),
            "task" => ascending
                ? rows.OrderBy(r => r.TaskPath, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.TaskPath, StringComparer.OrdinalIgnoreCase),
            "machine" => ascending
                ? rows.OrderBy(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase),
            _ => ascending
                ? rows.OrderBy(r => r.StartLocal, StringComparer.Ordinal)
                : rows.OrderByDescending(r => r.StartLocal, StringComparer.Ordinal)
        };
        return o.ThenBy(r => r.Id, StringComparer.Ordinal);
    }

    private static IOrderedEnumerable<JournalRow> ApplyThenBy(
        IOrderedEnumerable<JournalRow> ordered, string sortBy, bool ascending) =>
        sortBy switch
        {
            "customer" => ascending
                ? ordered.ThenBy(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase),
            "project" => ascending
                ? ordered.ThenBy(r => r.ProjectPath, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.ProjectPath, StringComparer.OrdinalIgnoreCase),
            "task" => ascending
                ? ordered.ThenBy(r => r.TaskPath, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.TaskPath, StringComparer.OrdinalIgnoreCase),
            "machine" => ascending
                ? ordered.ThenBy(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase),
            _ => ascending
                ? ordered.ThenBy(r => r.StartLocal, StringComparer.Ordinal)
                : ordered.ThenByDescending(r => r.StartLocal, StringComparer.Ordinal)
        };

    private static string GroupKey(JournalRow r, string groupBy) => groupBy switch
    {
        "machine" => r.MachineLabel,
        "customer" => r.CustomerLabel,
        "project" => r.ProjectPath,
        "task" => r.TaskPath,
        "customer-project" => r.CustomerLabel + " › " + r.ProjectLabel,
        "customer-project-task" => r.TaskPath,
        _ => ""
    };

    public static string NormalizeGroupBy(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "machine" => "machine",
            "customer" => "customer",
            "project" => "project",
            "task" => "task",
            "customer-project" or "customerproject" => "customer-project",
            "customer-project-task" or "customerprojecttask" => "customer-project-task",
            _ => "none"
        };

    public static string NormalizeSortBy(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "customer" => "customer",
            "project" => "project",
            "task" => "task",
            "machine" => "machine",
            _ => "date"
        };

    public static string NormalizeSortDir(string? value, string sortBy)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v is "asc" or "desc") return v;
        return sortBy == "date" ? "desc" : "asc";
    }

    public static List<QuickRange> BuildQuickRanges(DateOnly today)
    {
        var thisWeekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var lastWeekStart = thisWeekStart.AddDays(-7);
        var lastWeekEnd = thisWeekStart.AddDays(-1);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthEnd = thisMonthStart.AddDays(-1);
        var lastMonthStart = new DateOnly(lastMonthEnd.Year, lastMonthEnd.Month, 1);
        return new List<QuickRange>
        {
            new("This week", thisWeekStart, today),
            new("Last week", lastWeekStart, lastWeekEnd),
            new("This month", thisMonthStart, today),
            new("Last month", lastMonthStart, lastMonthEnd)
        };
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value?.Trim() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string FormatListLabel(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) return "(none)";
        if (string.IsNullOrWhiteSpace(code)) return name;
        if (string.IsNullOrWhiteSpace(name)) return code;
        if (string.Equals(code, name, StringComparison.Ordinal)) return name;
        return $"{code} — {name}";
    }

    /// <summary>Normalize customer colour for CSS (hex with leading #, or named colour).</summary>
    public static string? NormalizeColour(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour)) return null;
        var c = colour.Trim();
        if (c.StartsWith('#')) return c.Length is 4 or 7 ? c : null;
        if (c.Length is 3 or 6 && c.All(ch => Uri.IsHexDigit(ch)))
            return "#" + c;
        if (c.All(ch => char.IsLetter(ch)) && c.Length is >= 3 and <= 20)
            return c.ToLowerInvariant();
        return null;
    }

    private static string FormatPath(params string[] segments) =>
        string.Join(" › ", segments.Where(s => !string.IsNullOrWhiteSpace(s)));

    private List<ListOptionItem> BuildCustomerOptions()
    {
        var list = _customers
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => JournalLabelFormatter.FromListItem(c))
            .ToList();
        if (!list.Any(c => c.IsNone))
            list.Insert(0, JournalLabelFormatter.NoneOption());
        return list;
    }

    private List<ListOptionItem> BuildProjectOptions()
    {
        var list = _projects
            .Where(p => p.Id != Uuid7.Empty
                && !string.Equals(p.Code, "-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => JournalLabelFormatter.FromListItem(
                p, p.CustomerId is { } cid ? Uuid7Text.Format(cid) : null))
            .ToList();
        list.Insert(0, JournalLabelFormatter.NoneOption());
        return list;
    }

    private List<ListOptionItem> BuildTaskOptions()
    {
        var list = _tasks
            .Where(t => t.Id != Uuid7.Empty
                && !string.Equals(t.Code, "-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => JournalLabelFormatter.FromListItem(
                t, t.ProjectId is { } pid ? Uuid7Text.Format(pid) : null))
            .ToList();
        list.Insert(0, JournalLabelFormatter.NoneOption());
        return list;
    }

    private string FormatJournal(JournalEntryResponse j) =>
        JournalLabelFormatter.Format(
            j, MachineNames, _targetsById, _customersById, _projectsById, _tasksById, FormatProfileDateTime);

    private DateTime ToProfileLocal(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, _profileTimeZone ?? TimeZoneInfo.Utc);

    private string FormatProfileDateTime(DateTimeOffset utc) =>
        ToProfileLocal(utc).ToString("yyyy-MM-dd HH:mm:ss");

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

    private JournalEditorsViewModel BuildJournalEditors(List<JournalEntryResponse> entries)
    {
        var journalPayload = new Dictionary<string, object?>();
        foreach (var j in entries)
        {
            var id = Uuid7Text.Format(j.Id);
            journalPayload[id] = new
            {
                targetId = Uuid7Text.Format(j.JournalTargetId),
                startLocal = ToProfileLocal(j.StartUtc).ToString("yyyy-MM-ddTHH:mm:ss"),
                stopLocal = j.StopUtc is { } s ? ToProfileLocal(s).ToString("yyyy-MM-ddTHH:mm:ss") : "",
                tags = j.Tags ?? "",
                notes = j.Notes ?? "",
                label = FormatJournal(j)
            };
        }

        var targetPayload = new Dictionary<string, object?>();
        foreach (var t in _targetsById.Values)
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
                    t.Id, _targetsById, _customersById, _projectsById, _tasksById),
                tags = t.Tags ?? "",
                notes = t.Notes ?? "",
                label = JournalLabelFormatter.FormatTarget(
                    t.Id, _targetsById, _customersById, _projectsById, _tasksById)
            };
        }

        return new JournalEditorsViewModel
        {
            PagePath = "/Journals/Index",
            ProfileTimeZoneId = ProfileTimeZoneId,
            Journals = JournalLabels,
            Targets = _targetsById.Values
                .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new ListOptionItem
                {
                    Id = Uuid7Text.Format(t.Id),
                    Code = t.Code ?? "",
                    Name = JournalLabelFormatter.FormatTargetName(
                        t.Id, _targetsById, _customersById, _projectsById, _tasksById),
                    Label = JournalLabelFormatter.FormatTarget(
                        t.Id, _targetsById, _customersById, _projectsById, _tasksById)
                }).ToList(),
            Customers = Customers,
            Projects = Projects,
            Tasks = Tasks,
            Machines = Machines
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new ListOptionItem
                {
                    Id = Uuid7Text.Format(m.Id),
                    Label = m.Name,
                    Name = m.Name,
                    Code = m.Code ?? ""
                }).ToList(),
            DataJson = JsonSerializer.Serialize(new { journals = journalPayload, targets = targetPayload }),
            HiddenFields = new Dictionary<string, string?>()
        };
    }

    public sealed class JournalRow
    {
        public string Id { get; init; } = "";
        public string MachineLabel { get; init; } = "—";
        public string StartLocal { get; init; } = "";
        public string StopLocal { get; init; } = "";
        public DateOnly StartDate { get; init; }
        public string CustomerId { get; init; } = "";
        public string ProjectId { get; init; } = "";
        public string TaskId { get; init; } = "";
        public string CustomerLabel { get; init; } = "";
        public string ProjectLabel { get; init; } = "";
        public string TaskLabel { get; init; } = "";
        public string ProjectPath { get; init; } = "";
        public string TaskPath { get; init; } = "";
        /// <summary>Customer <c>colour_code</c> for row highlight (normalized CSS colour).</summary>
        public string? CustomerColour { get; init; }
        public bool IsOpen { get; init; }
    }

    public sealed class JournalSection
    {
        public string? HeaderLabel { get; init; }
        public bool ShowHeader { get; init; }
        public bool ShowSubtotal { get; init; }
        public List<JournalRow> Rows { get; init; } = new();
    }

    public sealed record FilterOption(string Id, string Label);

    public sealed record QuickRange(string Label, DateOnly From, DateOnly To)
    {
        public string FromText => From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string ToText => To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
