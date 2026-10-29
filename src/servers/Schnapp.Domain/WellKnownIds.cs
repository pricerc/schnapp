namespace Schnapp.Domain;

using Medo;

/// <summary>
/// Reserved identifiers for seeded sentinel rows (Customer/Project/Task NONE).
/// </summary>
/// <remarks>
/// The nil UUID is shared across list tables by design (FKs are per-table).
/// Application code must not assign this id when creating normal entities.
/// </remarks>
public static class WellKnownIds
{
    /// <summary>Nil UUID used by seeded <c>NONE</c> list rows and default parent FKs.</summary>
    public static readonly Uuid7 None = Uuid7.Empty;
}
