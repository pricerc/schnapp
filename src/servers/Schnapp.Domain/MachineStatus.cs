namespace Schnapp.Domain;

/// <summary>
/// Well-known <see cref="Entities.Machine.Status"/> values.
/// </summary>
public static class MachineStatus
{
    /// <summary>Machine may capture and appear in normal client selection.</summary>
    public const int Active = 0;

    /// <summary>Machine is disabled for capture and selection.</summary>
    public const int Disabled = 1;

    /// <summary>
    /// Returns a short English label for <paramref name="status"/>, or a fallback for unknown codes.
    /// </summary>
    public static string Label(int status) => status switch
    {
        Active => "Active",
        Disabled => "Disabled",
        _ => $"Status {status}"
    };

    /// <summary>All defined status values with labels, for dropdowns and filters.</summary>
    public static IReadOnlyList<(int Value, string Label)> All { get; } =
    [
        (Active, "Active"),
        (Disabled, "Disabled")
    ];
}
