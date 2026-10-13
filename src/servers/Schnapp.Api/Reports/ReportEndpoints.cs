using System.Globalization;
using System.Security.Claims;
using Schnapp.Api.Auth;
using Schnapp.Api.Journals;
using Schnapp.Api.Screenshots;
using Schnapp.Contracts;
using Schnapp.Data.Repositories;
using Schnapp.Domain;

using Medo;

namespace Schnapp.Api.Reports;

/// <summary>
/// Authenticated reporting endpoints (time-spent summaries from journal entries).
/// </summary>
public static class ReportEndpoints
{
    private const int MaxRangeDays = 366;

    /// <summary>
    /// Registers report routes under <c>/api/v1</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RequireAuthFilter>();
        api.MapGet("/reports/time-summary", TimeSummaryAsync);
        return app;
    }

    /// <summary>
    /// Returns journal time aggregated by local day, machine, and customer/project/task for the caller.
    /// Include and exclude C/P/T/M filters are combined with AND (include must match; exclude must not).
    /// Free-text filter values match code/name (contains); Id26/UUID values match by id.
    /// </summary>
    private static async Task<IResult> TimeSummaryAsync(
        string? fromDate,
        string? toDate,
        ClaimsPrincipal principal,
        JournalRepository journals,
        UserRepository users,
        CancellationToken ct,
        // Include row
        bool includeCustomer = false,
        string? includeCustomerValue = null,
        bool includeProject = false,
        string? includeProjectValue = null,
        bool includeTask = false,
        string? includeTaskValue = null,
        bool includeMachine = false,
        string? includeMachineValue = null,
        // Exclude row
        bool excludeCustomer = false,
        string? excludeCustomerValue = null,
        bool excludeProject = false,
        string? excludeProjectValue = null,
        bool excludeTask = false,
        string? excludeTaskValue = null,
        bool excludeMachine = false,
        string? excludeMachineValue = null)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null) return Results.Unauthorized();

        var tzId = string.IsNullOrWhiteSpace(user.TimeZoneId) ? "UTC" : user.TimeZoneId.Trim();
        var tz = SafeTz(tzId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));

        DateOnly to;
        if (string.IsNullOrWhiteSpace(toDate))
            to = todayLocal;
        else if (!DateOnly.TryParseExact(toDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
            return Results.BadRequest(new { error = "toDate must be yyyy-MM-dd." });

        DateOnly from;
        if (string.IsNullOrWhiteSpace(fromDate))
            from = to.AddDays(-6);
        else if (!DateOnly.TryParseExact(fromDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
            return Results.BadRequest(new { error = "fromDate must be yyyy-MM-dd." });

        if (from > to)
            return Results.BadRequest(new { error = "fromDate must be on or before toDate." });

        var daySpan = to.DayNumber - from.DayNumber + 1;
        if (daySpan > MaxRangeDays)
            return Results.BadRequest(new { error = $"Date range cannot exceed {MaxRangeDays} days." });

        var filters = new CptFilterSet
        {
            IncludeCustomer = ParseField(includeCustomer, includeCustomerValue),
            IncludeProject = ParseField(includeProject, includeProjectValue),
            IncludeTask = ParseField(includeTask, includeTaskValue),
            IncludeMachine = ParseField(includeMachine, includeMachineValue),
            ExcludeCustomer = ParseField(excludeCustomer, excludeCustomerValue),
            ExcludeProject = ParseField(excludeProject, excludeProjectValue),
            ExcludeTask = ParseField(excludeTask, excludeTaskValue),
            ExcludeMachine = ParseField(excludeMachine, excludeMachineValue)
        };

        await JournalDayBoundary.EnsureAsync(auth.Id, tzId, journals, ct);

        var (rangeStartUtc, _) = DayRange.ForLocalDate(from, tzId);
        var (_, rangeEndUtc) = DayRange.ForLocalDate(to, tzId);
        var nowUtc = DateTimeOffset.UtcNow;

        var rows = await journals.SummarizeTimeAsync(
            auth.Id,
            rangeStartUtc,
            rangeEndUtc,
            tzId,
            nowUtc,
            WellKnownIds.None,
            filters,
            ct);

        var mapped = rows.Select(r =>
        {
            long? span = null;
            if (r.FirstScreenshotUtc is { } first && r.LastScreenshotUtc is { } last)
                span = (long)Math.Max(0, (last - first).TotalSeconds);

            return new TimeSummaryRow
            {
                LocalDate = r.LocalDate,
                MachineId = r.MachineId,
                MachineCode = r.MachineCode,
                MachineName = r.MachineName,
                CustomerId = r.CustomerId,
                CustomerCode = r.CustomerCode,
                CustomerName = r.CustomerName,
                ProjectId = r.ProjectId,
                ProjectCode = r.ProjectCode,
                ProjectName = r.ProjectName,
                TaskId = r.TaskId,
                TaskCode = r.TaskCode,
                TaskName = r.TaskName,
                TotalSeconds = r.TotalSeconds,
                EntryCount = r.EntryCount,
                FirstScreenshotUtc = r.FirstScreenshotUtc,
                LastScreenshotUtc = r.LastScreenshotUtc,
                ScreenshotSpanSeconds = span
            };
        }).ToList();

        return Results.Ok(new TimeSummaryResponse
        {
            FromDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeZoneId = tzId,
            TotalSeconds = mapped.Sum(r => r.TotalSeconds),
            Rows = mapped
        });
    }

    /// <summary>
    /// Builds a field filter: Id26/UUID → id match; free text (or none aliases) → text/id.
    /// </summary>
    internal static CptFieldFilter ParseField(bool enabled, string? value)
    {
        if (!enabled) return new CptFieldFilter { Enabled = false };
        var raw = (value ?? "").Trim();
        if (raw.Length == 0) return new CptFieldFilter { Enabled = false };

        if (Uuid7Text.TryParse(raw, out var id))
            return new CptFieldFilter { Enabled = true, Id = id };

        // Common free-text aliases for the NONE sentinel.
        if (IsNoneAlias(raw))
            return new CptFieldFilter { Enabled = true, Id = WellKnownIds.None };

        return new CptFieldFilter { Enabled = true, Text = raw };
    }

    private static bool IsNoneAlias(string raw)
    {
        var s = raw.Trim();
        if (s.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("(none)", StringComparison.OrdinalIgnoreCase)) return true;
        if (s == "-") return true;
        if (s.Equals("NONE", StringComparison.Ordinal)) return true;
        // Label forms like "- — (none)" or "NONE — (none)"
        if (s.Contains("(none)", StringComparison.OrdinalIgnoreCase)
            && (s.Contains("NONE", StringComparison.OrdinalIgnoreCase) || s.StartsWith('-') || s.Contains('—')))
            return true;
        return false;
    }

    private static TimeZoneInfo SafeTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }
}
