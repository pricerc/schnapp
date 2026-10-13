using Medo;

namespace Schnapp.Data.Repositories;

/// <summary>
/// Optional include or exclude constraint on a customer, project, or task field.
/// When <see cref="Enabled"/> is false, the filter is ignored.
/// When <see cref="Id"/> is set, match by id; otherwise match free text against code/name.
/// </summary>
public sealed class CptFieldFilter
{
    public bool Enabled { get; init; }

    /// <summary>Exact id match when set (takes precedence over text).</summary>
    public Uuid7? Id { get; init; }

    /// <summary>
    /// Free-text pattern for code/name (case-insensitive contains). Used when <see cref="Id"/> is null.
    /// Empty text with <see cref="Enabled"/> true matches nothing useful — treat as disabled in callers.
    /// </summary>
    public string? Text { get; init; }

    public bool IsActive => Enabled && (Id is not null || !string.IsNullOrWhiteSpace(Text));
}

/// <summary>Include + exclude filters for customer, project, task, and machine.</summary>
public sealed class CptFilterSet
{
    public CptFieldFilter IncludeCustomer { get; init; } = new();
    public CptFieldFilter IncludeProject { get; init; } = new();
    public CptFieldFilter IncludeTask { get; init; } = new();
    public CptFieldFilter IncludeMachine { get; init; } = new();
    public CptFieldFilter ExcludeCustomer { get; init; } = new();
    public CptFieldFilter ExcludeProject { get; init; } = new();
    public CptFieldFilter ExcludeTask { get; init; } = new();
    public CptFieldFilter ExcludeMachine { get; init; } = new();
}
