namespace Schnapp.Domain;

/// <summary>
/// Well-known status values for customer, project, task, and user list entities.
/// </summary>
public static class ListEntityStatus
{
    /// <summary>Entity is active and available for normal use.</summary>
    public const int Active = 0;

    /// <summary>Entity is inactive (hidden from default pickers; retained for history).</summary>
    public const int Inactive = 1;

    /// <summary>
    /// Returns a short English label for <paramref name="status"/>, or a fallback for unknown codes.
    /// </summary>
    public static string Label(int status) => status switch
    {
        Active => "Active",
        Inactive => "Inactive",
        _ => $"Status {status}"
    };

    /// <summary>All defined status values with labels, for dropdowns and filters.</summary>
    public static IReadOnlyList<(int Value, string Label)> All { get; } =
    [
        (Active, "Active"),
        (Inactive, "Inactive")
    ];
}
