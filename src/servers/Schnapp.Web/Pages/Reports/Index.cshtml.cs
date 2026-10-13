using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;

namespace Schnapp.Web.Pages.Reports;

/// <summary>
/// Time-spent report: day x machine x customer x project x task summary with CSV export,
/// optional C/P/T/M grouping (subtotals), and sorting.
/// </summary>
public sealed class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    // --- Include / exclude C/P/T/M (non-empty value = filter applied) ---
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

    /// <summary>Group key: none | machine | customer | project | task | customer-project | customer-project-task.</summary>
    [BindProperty(SupportsGet = true)]
    public string? GroupBy { get; set; }

    /// <summary>Sort key: date | machine | customer | project | task | duration.</summary>
    [BindProperty(SupportsGet = true)]
    public string? SortBy { get; set; }

    /// <summary>asc or desc (default depends on <see cref="SortBy"/>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? SortDir { get; set; }

    public string ProfileTimeZoneId { get; private set; } = "UTC";
    public string FromDate { get; private set; } = "";
    public string ToDate { get; private set; } = "";
    public long TotalSeconds { get; private set; }
    public string TotalDurationDisplay { get; private set; } = "0m";
    public int TotalEntryCount { get; private set; }
    public List<ReportRowView> Rows { get; private set; } = new();
    public List<ReportSection> Sections { get; private set; } = new();
    public List<FilterOption> Customers { get; private set; } = new();
    public List<FilterOption> Projects { get; private set; } = new();
    public List<FilterOption> Tasks { get; private set; } = new();
    public List<FilterOption> Machines { get; private set; } = new();
    public string GroupByNormalized { get; private set; } = "none";
    public string SortByNormalized { get; private set; } = "date";
    public string SortDirNormalized { get; private set; } = "desc";
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
        new FilterOption("date", "Date"),
        new FilterOption("machine", "Machine"),
        new FilterOption("customer", "Customer"),
        new FilterOption("project", "Project"),
        new FilterOption("task", "Task"),
        new FilterOption("duration", "Duration")
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");

        if (!await LoadSummaryAsync(ct))
            return Page();

        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");

        if (!await LoadSummaryAsync(ct) || Error is not null)
            return Page();

        var sb = new StringBuilder();
        sb.AppendLine(
            "Group,Date,MachineCode,MachineName,CustomerCode,CustomerName,ProjectCode,ProjectName,TaskCode,TaskName," +
            "Hours,TotalSeconds,EntryCount,FirstScreenshotLocal,LastScreenshotLocal," +
            "ScreenshotSpanHours,ScreenshotSpanSeconds,FirstScreenshotUtc,LastScreenshotUtc,RowKind");

        foreach (var section in Sections)
        {
            foreach (var row in section.Rows)
                AppendCsvDataRow(sb, section.HeaderLabel, row, "detail");

            if (section.ShowSubtotal)
            {
                var hours = (section.SubtotalSeconds / 3600.0).ToString("0.##", CultureInfo.InvariantCulture);
                sb.Append(Csv(section.HeaderLabel)).Append(',')
                    .Append(',').Append(',').Append(',').Append(',').Append(',').Append(',').Append(',').Append(',').Append(',')
                    .Append(hours).Append(',')
                    .Append(section.SubtotalSeconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(section.SubtotalEntryCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(section.FirstScreenshotLocal)).Append(',')
                    .Append(Csv(section.LastScreenshotLocal)).Append(',')
                    .Append(section.ScreenshotSpanSeconds is { } ss
                        ? (ss / 3600.0).ToString("0.##", CultureInfo.InvariantCulture)
                        : "").Append(',')
                    .Append(section.ScreenshotSpanSeconds?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                    .Append(Csv(section.FirstScreenshotUtc)).Append(',')
                    .Append(Csv(section.LastScreenshotUtc)).Append(',')
                    .Append("subtotal")
                    .AppendLine();
            }
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"time-summary-{FromDate}-{ToDate}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private void AppendCsvDataRow(StringBuilder sb, string? group, ReportRowView row, string kind)
    {
        var hours = (row.TotalSeconds / 3600.0).ToString("0.##", CultureInfo.InvariantCulture);
        var spanHours = row.ScreenshotSpanSeconds is { } ss
            ? (ss / 3600.0).ToString("0.##", CultureInfo.InvariantCulture)
            : "";
        sb.Append(Csv(group)).Append(',')
            .Append(Csv(row.LocalDate)).Append(',')
            .Append(Csv(row.MachineCode)).Append(',')
            .Append(Csv(row.MachineName)).Append(',')
            .Append(Csv(row.CustomerCode)).Append(',')
            .Append(Csv(row.CustomerName)).Append(',')
            .Append(Csv(row.ProjectCode)).Append(',')
            .Append(Csv(row.ProjectName)).Append(',')
            .Append(Csv(row.TaskCode)).Append(',')
            .Append(Csv(row.TaskName)).Append(',')
            .Append(hours).Append(',')
            .Append(row.TotalSeconds.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(row.EntryCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(row.FirstScreenshotLocal)).Append(',')
            .Append(Csv(row.LastScreenshotLocal)).Append(',')
            .Append(spanHours).Append(',')
            .Append(row.ScreenshotSpanSeconds?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
            .Append(Csv(row.FirstScreenshotUtc)).Append(',')
            .Append(Csv(row.LastScreenshotUtc)).Append(',')
            .Append(kind)
            .AppendLine();
    }

    /// <summary>Builds a query string preserving current non-date filters (for quick-range links).</summary>
    public string FilterQuery(string from, string to)
    {
        var parts = new List<string>
        {
            "From=" + Uri.EscapeDataString(from),
            "To=" + Uri.EscapeDataString(to)
        };
        AppendSharedFilters(parts);
        return string.Join("&", parts);
    }

    private void AppendSharedFilters(List<string> parts)
    {
        void Cpt(string? value, string valueName)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(valueName + "=" + Uri.EscapeDataString(value.Trim()));
        }
        Cpt(IncCustomer, "IncCustomer");
        Cpt(IncProject, "IncProject");
        Cpt(IncTask, "IncTask");
        Cpt(IncMachine, "IncMachine");
        Cpt(ExcCustomer, "ExcCustomer");
        Cpt(ExcProject, "ExcProject");
        Cpt(ExcTask, "ExcTask");
        Cpt(ExcMachine, "ExcMachine");
        if (!string.IsNullOrWhiteSpace(GroupByNormalized) && GroupByNormalized != "none")
            parts.Add("GroupBy=" + Uri.EscapeDataString(GroupByNormalized));
        if (!string.IsNullOrWhiteSpace(SortByNormalized) && SortByNormalized != "date")
            parts.Add("SortBy=" + Uri.EscapeDataString(SortByNormalized));
        if (!string.IsNullOrWhiteSpace(SortDirNormalized))
            parts.Add("SortDir=" + Uri.EscapeDataString(SortDirNormalized));
    }

    private async Task<bool> LoadSummaryAsync(CancellationToken ct)
    {
        var me = await _api.GetMeAsync(ct);
        if (me is null)
        {
            Error = "Could not load profile.";
            return true;
        }

        ProfileTimeZoneId = string.IsNullOrWhiteSpace(me.TimeZoneId) ? "UTC" : me.TimeZoneId.Trim();
        var tz = ResolveTimeZone(ProfileTimeZoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        QuickRanges = BuildQuickRanges(todayLocal);

        GroupByNormalized = NormalizeGroupBy(GroupBy);
        SortByNormalized = NormalizeSortBy(SortBy);
        SortDirNormalized = NormalizeSortDir(SortDir, SortByNormalized);
        GroupBy = GroupByNormalized;
        SortBy = SortByNormalized;
        SortDir = SortDirNormalized;

        await LoadFilterOptionsAsync(ct);
        ApplyDefaultExcludeIfFirstVisit();

        if (!TryParseDate(To, out var toDate))
            toDate = todayLocal;
        if (!TryParseDate(From, out var fromDate))
            fromDate = toDate.AddDays(-6);

        if (fromDate > toDate)
        {
            Error = "From date must be on or before To date.";
            FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        From = FromDate;
        To = ToDate;

        var filterQuery = BuildFilterQuery();
        var summary = await _api.GetTimeSummaryAsync(fromDate, toDate, filterQuery, ct);
        if (summary is null)
        {
            Error = "Could not load time summary from the API.";
            return true;
        }

        ProfileTimeZoneId = string.IsNullOrWhiteSpace(summary.TimeZoneId)
            ? ProfileTimeZoneId
            : summary.TimeZoneId;
        TotalSeconds = summary.TotalSeconds;
        TotalDurationDisplay = FormatDuration(summary.TotalSeconds);
        var profileTz = ResolveTimeZone(ProfileTimeZoneId);
        Rows = summary.Rows.Select(r => MapRow(r, profileTz)).ToList();
        TotalEntryCount = Rows.Sum(r => r.EntryCount);
        Sections = BuildSections(Rows, GroupByNormalized, SortByNormalized, SortDirNormalized, profileTz);
        return true;
    }

    private static ReportRowView MapRow(TimeSummaryRow r, TimeZoneInfo profileTz)
    {
        var span = r.ScreenshotSpanSeconds
            ?? (r.FirstScreenshotUtc is { } f && r.LastScreenshotUtc is { } l
                ? (long?)Math.Max(0, (l - f).TotalSeconds)
                : null);
        return new ReportRowView
        {
            LocalDate = r.LocalDate,
            MachineCode = r.MachineCode,
            MachineName = r.MachineName,
            CustomerCode = r.CustomerCode,
            CustomerName = r.CustomerName,
            ProjectCode = r.ProjectCode,
            ProjectName = r.ProjectName,
            TaskCode = r.TaskCode,
            TaskName = r.TaskName,
            TotalSeconds = r.TotalSeconds,
            EntryCount = r.EntryCount,
            DurationDisplay = FormatDuration(r.TotalSeconds),
            MachineLabel = FormatListLabel(r.MachineCode, r.MachineName),
            CustomerLabel = FormatListLabel(r.CustomerCode, r.CustomerName),
            ProjectLabel = FormatListLabel(r.ProjectCode, r.ProjectName),
            TaskLabel = FormatListLabel(r.TaskCode, r.TaskName),
            FirstScreenshotLocal = FormatLocal(r.FirstScreenshotUtc, profileTz),
            LastScreenshotLocal = FormatLocal(r.LastScreenshotUtc, profileTz),
            FirstScreenshotUtc = FormatUtc(r.FirstScreenshotUtc),
            LastScreenshotUtc = FormatUtc(r.LastScreenshotUtc),
            ScreenshotSpanSeconds = span,
            ScreenshotSpanDisplay = span is null ? null : FormatDuration(span.Value),
            FirstScreenshotUtcRaw = r.FirstScreenshotUtc,
            LastScreenshotUtcRaw = r.LastScreenshotUtc
        };
    }

    /// <summary>
    /// Sorts and optionally groups detail rows; emits a section with optional subtotal per group.
    /// </summary>
    public static List<ReportSection> BuildSections(
        IReadOnlyList<ReportRowView> rows,
        string groupBy,
        string sortBy,
        string sortDir,
        TimeZoneInfo? profileTz = null)
    {
        profileTz ??= TimeZoneInfo.Utc;
        var ascending = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

        if (groupBy is "none" or null or "")
        {
            var sorted = SortRows(rows, sortBy, ascending).ToList();
            return new List<ReportSection>
            {
                new()
                {
                    HeaderLabel = null,
                    ShowHeader = false,
                    ShowSubtotal = false,
                    Rows = sorted,
                    SubtotalSeconds = sorted.Sum(r => r.TotalSeconds),
                    SubtotalEntryCount = sorted.Sum(r => r.EntryCount),
                    SubtotalDurationDisplay = FormatDuration(sorted.Sum(r => r.TotalSeconds))
                }
            };
        }

        // Contiguous groups: order by group key, then within-group sort.
        var groupedOrder = GroupThenSort(rows, groupBy, sortBy, ascending);
        var sections = new List<ReportSection>();
        foreach (var g in groupedOrder.GroupBy(r => GroupKey(r, groupBy), StringComparer.Ordinal))
        {
            var list = g.ToList();
            var firstShot = list
                .Select(r => r.FirstScreenshotUtcRaw)
                .Where(t => t is not null)
                .DefaultIfEmpty()
                .Min();
            var lastShot = list
                .Select(r => r.LastScreenshotUtcRaw)
                .Where(t => t is not null)
                .DefaultIfEmpty()
                .Max();
            long? span = firstShot is { } f && lastShot is { } l
                ? (long)Math.Max(0, (l - f).TotalSeconds)
                : null;
            var subSeconds = list.Sum(r => r.TotalSeconds);

            sections.Add(new ReportSection
            {
                HeaderLabel = g.Key,
                ShowHeader = true,
                ShowSubtotal = true,
                Rows = list,
                SubtotalSeconds = subSeconds,
                SubtotalEntryCount = list.Sum(r => r.EntryCount),
                SubtotalDurationDisplay = FormatDuration(subSeconds),
                FirstScreenshotLocal = FormatLocal(firstShot, profileTz),
                LastScreenshotLocal = FormatLocal(lastShot, profileTz),
                FirstScreenshotUtc = FormatUtc(firstShot),
                LastScreenshotUtc = FormatUtc(lastShot),
                ScreenshotSpanSeconds = span,
                ScreenshotSpanDisplay = span is null ? null : FormatDuration(span.Value)
            });
        }

        // Section order follows SortBy when meaningful (duration or matching C/P/T label).
        return SortSections(sections, sortBy, groupBy, ascending).ToList();
    }

    private static IEnumerable<ReportRowView> GroupThenSort(
        IEnumerable<ReportRowView> rows, string groupBy, string sortBy, bool ascending)
    {
        // Contiguous groups: order by group key, then within-group sort fields.
        IOrderedEnumerable<ReportRowView> ordered = ascending
            ? rows.OrderBy(r => GroupKey(r, groupBy), StringComparer.OrdinalIgnoreCase)
            : rows.OrderByDescending(r => GroupKey(r, groupBy), StringComparer.OrdinalIgnoreCase);

        // Within group always apply sort (secondary).
        return ApplyThenBy(ordered, sortBy, ascending);
    }

    private static IEnumerable<ReportRowView> SortRows(
        IEnumerable<ReportRowView> rows, string sortBy, bool ascending)
    {
        IOrderedEnumerable<ReportRowView> ordered = sortBy switch
        {
            "machine" => ascending
                ? rows.OrderBy(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase),
            "customer" => ascending
                ? rows.OrderBy(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase),
            "project" => ascending
                ? rows.OrderBy(r => r.ProjectLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.ProjectLabel, StringComparer.OrdinalIgnoreCase),
            "task" => ascending
                ? rows.OrderBy(r => r.TaskLabel, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => r.TaskLabel, StringComparer.OrdinalIgnoreCase),
            "duration" => ascending
                ? rows.OrderBy(r => r.TotalSeconds)
                : rows.OrderByDescending(r => r.TotalSeconds),
            _ => ascending
                ? rows.OrderBy(r => r.LocalDate, StringComparer.Ordinal)
                : rows.OrderByDescending(r => r.LocalDate, StringComparer.Ordinal)
        };
        // Stable secondary keys for ties.
        return ordered
            .ThenBy(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ProjectLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TaskLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LocalDate, StringComparer.Ordinal);
    }

    private static IOrderedEnumerable<ReportRowView> ApplyThenBy(
        IOrderedEnumerable<ReportRowView> ordered, string sortBy, bool ascending) =>
        sortBy switch
        {
            "machine" => ascending
                ? ordered.ThenBy(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.MachineLabel, StringComparer.OrdinalIgnoreCase),
            "customer" => ascending
                ? ordered.ThenBy(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.CustomerLabel, StringComparer.OrdinalIgnoreCase),
            "project" => ascending
                ? ordered.ThenBy(r => r.ProjectLabel, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.ProjectLabel, StringComparer.OrdinalIgnoreCase),
            "task" => ascending
                ? ordered.ThenBy(r => r.TaskLabel, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(r => r.TaskLabel, StringComparer.OrdinalIgnoreCase),
            "duration" => ascending
                ? ordered.ThenBy(r => r.TotalSeconds)
                : ordered.ThenByDescending(r => r.TotalSeconds),
            _ => ascending
                ? ordered.ThenBy(r => r.LocalDate, StringComparer.Ordinal)
                : ordered.ThenByDescending(r => r.LocalDate, StringComparer.Ordinal)
        };

    private static IEnumerable<ReportSection> SortSections(
        IEnumerable<ReportSection> sections, string sortBy, string groupBy, bool ascending)
    {
        if (sortBy == "duration")
        {
            return ascending
                ? sections.OrderBy(s => s.SubtotalSeconds)
                : sections.OrderByDescending(s => s.SubtotalSeconds);
        }

        // When sorting by the same dimension as grouping (or by name fields), order sections by label.
        if (sortBy is "machine" or "customer" or "project" or "task"
            || groupBy is "machine" or "customer" or "project" or "task"
            || groupBy.StartsWith("customer", StringComparison.Ordinal))
        {
            return ascending
                ? sections.OrderBy(s => s.HeaderLabel ?? "", StringComparer.OrdinalIgnoreCase)
                : sections.OrderByDescending(s => s.HeaderLabel ?? "", StringComparer.OrdinalIgnoreCase);
        }

        // Date sort: keep groups in order of earliest/latest detail date within the group.
        return ascending
            ? sections.OrderBy(s => s.Rows.Min(r => r.LocalDate), StringComparer.Ordinal)
            : sections.OrderByDescending(s => s.Rows.Max(r => r.LocalDate), StringComparer.Ordinal);
    }

    private static string GroupKey(ReportRowView r, string groupBy) => groupBy switch
    {
        "machine" => r.MachineLabel,
        "customer" => r.CustomerLabel,
        "project" => r.ProjectLabel,
        "task" => r.TaskLabel,
        "customer-project" => r.CustomerLabel + " › " + r.ProjectLabel,
        "customer-project-task" => r.CustomerLabel + " › " + r.ProjectLabel + " › " + r.TaskLabel,
        _ => ""
    };

    public static string NormalizeGroupBy(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "machine" => "machine",
            "customer" => "customer",
            "project" => "project",
            "task" => "task",
            "customer-project" or "customerproject" or "cp" => "customer-project",
            "customer-project-task" or "customerprojecttask" or "cpt" => "customer-project-task",
            _ => "none"
        };

    public static string NormalizeSortBy(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "machine" => "machine",
            "customer" => "customer",
            "project" => "project",
            "task" => "task",
            "duration" or "time" => "duration",
            _ => "date"
        };

    public static string NormalizeSortDir(string? value, string sortBy)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v is "asc" or "desc") return v;
        // Defaults: newest / largest first for date & duration; A→Z for names.
        return sortBy is "date" or "duration" ? "desc" : "asc";
    }

    private async Task LoadFilterOptionsAsync(CancellationToken ct)
    {
        var customers = await _api.ListItemsAsync("customers", ct) ?? new();
        var projects = await _api.ListItemsAsync("projects", ct) ?? new();
        var tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        var machines = await _api.ListMachinesAsync(ct) ?? new();

        var customerLabelById = customers.ToDictionary(
            c => c.Id,
            c => FormatListLabel(c.Code, c.Name));
        var projectLabelById = projects.ToDictionary(
            p => p.Id,
            p => FormatListLabel(p.Code, p.Name));

        string CustomerLabel(Uuid7? id) =>
            id is { } cid && customerLabelById.TryGetValue(cid, out var lab) ? lab : "(unknown)";
        string ProjectLabel(Uuid7? id) =>
            id is { } pid && projectLabelById.TryGetValue(pid, out var lab) ? lab : "(unknown)";

        Customers = customers
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new FilterOption(Uuid7Text.Format(c.Id), FormatListLabel(c.Code, c.Name)))
            .ToList();

        // Projects: "Customer › Project" so names that repeat under different customers are unique.
        Projects = projects
            .OrderBy(p => CustomerLabel(p.CustomerId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new FilterOption(
                Uuid7Text.Format(p.Id),
                FormatPath(CustomerLabel(p.CustomerId), FormatListLabel(p.Code, p.Name))))
            .ToList();

        // Tasks: "Customer › Project › Task".
        Tasks = tasks
            .OrderBy(t => CustomerLabel(t.CustomerId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => ProjectLabel(t.ProjectId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new FilterOption(
                Uuid7Text.Format(t.Id),
                FormatPath(
                    CustomerLabel(t.CustomerId),
                    ProjectLabel(t.ProjectId),
                    FormatListLabel(t.Code, t.Name))))
            .ToList();

        Machines = machines
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new FilterOption(
                Uuid7Text.Format(m.Id),
                FormatListLabel(m.Code ?? "", m.Name)))
            .ToList();
    }

    /// <summary>Hierarchical display path for list combos (e.g. Customer › Project › Task).</summary>
    public static string FormatPath(params string[] segments) =>
        string.Join(" › ", segments.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>
    /// First visit (no From in query): exclude customer = none (value present ⇒ applied).
    /// </summary>
    private void ApplyDefaultExcludeIfFirstVisit()
    {
        if (Request.Query.ContainsKey("From"))
            return;

        var noneLabel = Customers.FirstOrDefault(c => c.Id == Uuid7Text.Format(WellKnownIds.None))?.Label
            ?? "(none)";

        if (string.IsNullOrWhiteSpace(ExcCustomer)) ExcCustomer = noneLabel;
    }

    private static bool HasFilterValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private TimeSummaryFilterQuery BuildFilterQuery() => new()
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

    public static List<QuickRange> BuildQuickRanges(DateOnly today)
    {
        var thisWeekStart = StartOfWeekMonday(today);
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

    private static DateOnly StartOfWeekMonday(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    public static string FormatDuration(long totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        var hours = (int)ts.TotalHours;
        var minutes = ts.Minutes;
        if (hours > 0 && minutes > 0) return $"{hours}h {minutes}m";
        if (hours > 0) return $"{hours}h";
        if (minutes > 0) return $"{minutes}m";
        return totalSeconds > 0 ? $"{totalSeconds}s" : "0m";
    }

    private static string FormatListLabel(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
            return "(none)";
        if (string.IsNullOrWhiteSpace(code)) return name;
        if (string.IsNullOrWhiteSpace(name)) return code;
        if (string.Equals(code, name, StringComparison.Ordinal)) return name;
        return $"{code} — {name}";
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value?.Trim() ?? "",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    private static string? FormatLocal(DateTimeOffset? utc, TimeZoneInfo tz)
    {
        if (utc is null) return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc.Value.UtcDateTime, tz);
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string? FormatUtc(DateTimeOffset? utc) =>
        utc?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        var s = value ?? "";
        if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@'))
            s = "'" + s;
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public sealed class ReportRowView
    {
        public string LocalDate { get; init; } = "";
        public string MachineCode { get; init; } = "";
        public string MachineName { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string ProjectCode { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public string TaskCode { get; init; } = "";
        public string TaskName { get; init; } = "";
        public string MachineLabel { get; init; } = "";
        public string CustomerLabel { get; init; } = "";
        public string ProjectLabel { get; init; } = "";
        public string TaskLabel { get; init; } = "";
        public long TotalSeconds { get; init; }
        public int EntryCount { get; init; }
        public string DurationDisplay { get; init; } = "";
        public string? FirstScreenshotLocal { get; init; }
        public string? LastScreenshotLocal { get; init; }
        public string? FirstScreenshotUtc { get; init; }
        public string? LastScreenshotUtc { get; init; }
        public long? ScreenshotSpanSeconds { get; init; }
        public string? ScreenshotSpanDisplay { get; init; }
        public DateTimeOffset? FirstScreenshotUtcRaw { get; init; }
        public DateTimeOffset? LastScreenshotUtcRaw { get; init; }
    }

    public sealed class ReportSection
    {
        public string? HeaderLabel { get; init; }
        public bool ShowHeader { get; init; }
        public bool ShowSubtotal { get; init; }
        public List<ReportRowView> Rows { get; init; } = new();
        public long SubtotalSeconds { get; init; }
        public int SubtotalEntryCount { get; init; }
        public string SubtotalDurationDisplay { get; init; } = "";
        public string? FirstScreenshotLocal { get; init; }
        public string? LastScreenshotLocal { get; init; }
        public string? FirstScreenshotUtc { get; init; }
        public string? LastScreenshotUtc { get; init; }
        public long? ScreenshotSpanSeconds { get; init; }
        public string? ScreenshotSpanDisplay { get; init; }
    }

    public sealed record FilterOption(string Id, string Label);

    public sealed record QuickRange(string Label, DateOnly From, DateOnly To)
    {
        public string FromText => From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string ToText => To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
