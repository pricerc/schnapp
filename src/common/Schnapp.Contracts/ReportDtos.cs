namespace Schnapp.Contracts;

using Medo;

/// <summary>
/// Include/exclude C/P/T filter query for time-summary (free text or id; enable flags).
/// </summary>
public sealed class TimeSummaryFilterQuery
{
    public bool IncludeCustomer { get; set; }
    public string? IncludeCustomerValue { get; set; }
    public bool IncludeProject { get; set; }
    public string? IncludeProjectValue { get; set; }
    public bool IncludeTask { get; set; }
    public string? IncludeTaskValue { get; set; }
    public bool IncludeMachine { get; set; }
    public string? IncludeMachineValue { get; set; }

    public bool ExcludeCustomer { get; set; }
    public string? ExcludeCustomerValue { get; set; }
    public bool ExcludeProject { get; set; }
    public string? ExcludeProjectValue { get; set; }
    public bool ExcludeTask { get; set; }
    public string? ExcludeTaskValue { get; set; }
    public bool ExcludeMachine { get; set; }
    public string? ExcludeMachineValue { get; set; }

    /// <summary>Appends non-default filter params as <c>&amp;name=value</c> fragments.</summary>
    public string ToQueryString()
    {
        var parts = new List<string>();
        void Add(bool on, string onName, string? value, string valueName)
        {
            if (!on)
            {
                return;
            }

            parts.Add(onName + "=true");
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(valueName + "=" + Uri.EscapeDataString(value.Trim()));
            }
        }
        Add(IncludeCustomer, "includeCustomer", IncludeCustomerValue, "includeCustomerValue");
        Add(IncludeProject, "includeProject", IncludeProjectValue, "includeProjectValue");
        Add(IncludeTask, "includeTask", IncludeTaskValue, "includeTaskValue");
        Add(IncludeMachine, "includeMachine", IncludeMachineValue, "includeMachineValue");
        Add(ExcludeCustomer, "excludeCustomer", ExcludeCustomerValue, "excludeCustomerValue");
        Add(ExcludeProject, "excludeProject", ExcludeProjectValue, "excludeProjectValue");
        Add(ExcludeTask, "excludeTask", ExcludeTaskValue, "excludeTaskValue");
        Add(ExcludeMachine, "excludeMachine", ExcludeMachineValue, "excludeMachineValue");
        return parts.Count == 0 ? "" : "&" + string.Join("&", parts);
    }
}

/// <summary>Time-spent summary for a local date range (profile timezone).</summary>
public sealed class TimeSummaryResponse
{
    /// <summary>Inclusive range start as <c>yyyy-MM-dd</c> in the profile timezone.</summary>
    public string FromDate { get; set; } = "";

    /// <summary>Inclusive range end as <c>yyyy-MM-dd</c> in the profile timezone.</summary>
    public string ToDate { get; set; } = "";

    /// <summary>IANA timezone id used for day boundaries and local dates.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Sum of <see cref="TimeSummaryRow.TotalSeconds"/> across all rows.</summary>
    public long TotalSeconds { get; set; }

    public List<TimeSummaryRow> Rows { get; set; } = new();
}

/// <summary>
/// Aggregated journal time for one local calendar day, machine, and customer/project/task triple.
/// </summary>
public sealed class TimeSummaryRow
{
    /// <summary>Local calendar date (<c>yyyy-MM-dd</c>) of the journal segment start.</summary>
    public string LocalDate { get; set; } = "";

    public Uuid7 MachineId { get; set; }
    public string MachineCode { get; set; } = "";
    public string MachineName { get; set; } = "";

    public Uuid7 CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";

    public Uuid7 ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";

    public Uuid7 TaskId { get; set; }
    public string TaskCode { get; set; } = "";
    public string TaskName { get; set; } = "";

    /// <summary>Total duration in whole seconds (non-negative).</summary>
    public long TotalSeconds { get; set; }

    /// <summary>Number of journal segments contributing to this row.</summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// Earliest screenshot capture time (UTC) within any journal segment in this row
    /// (<c>start_utc</c> … <c>stop_utc</c>/now); null when none.
    /// </summary>
    public DateTimeOffset? FirstScreenshotUtc { get; set; }

    /// <summary>
    /// Latest screenshot capture time (UTC) within any journal segment in this row; null when none.
    /// </summary>
    public DateTimeOffset? LastScreenshotUtc { get; set; }

    /// <summary>
    /// Seconds between <see cref="FirstScreenshotUtc"/> and <see cref="LastScreenshotUtc"/> when both set.
    /// </summary>
    public long? ScreenshotSpanSeconds { get; set; }
}
