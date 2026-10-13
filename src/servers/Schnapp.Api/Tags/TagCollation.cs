using Medo;
using Schnapp.Contracts;
using Schnapp.Data.Repositories;
using Schnapp.Domain;

namespace Schnapp.Api.Tags;

/// <summary>
/// Shared normalize + dictionary collation for free-text <c>tags</c> fields.
/// </summary>
internal static class TagCollation
{
    /// <summary>
    /// Normalizes free-text tags and upserts each token into the tag dictionary.
    /// </summary>
    /// <param name="lists">List repository (tag table).</param>
    /// <param name="tags">Raw tags; null means leave unchanged (returns null).</param>
    /// <param name="customerId">Customer scope for dictionary rows; null/None → global.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Normalized space-separated string (empty when no tokens), or null when
    /// <paramref name="tags"/> was null (partial-update leave-unchanged).
    /// </returns>
    public static async Task<string?> NormalizeAndEnsureAsync(
        ListRepository lists,
        string? tags,
        Uuid7? customerId,
        CancellationToken ct)
    {
        if (tags is null)
            return null;

        var normalized = TagText.Normalize(tags);
        await lists.EnsureTagsAsync(normalized, Scope(customerId), ct);
        return normalized;
    }

    /// <summary>
    /// Like <see cref="NormalizeAndEnsureAsync"/> but always returns a non-null string
    /// (empty when input is null/blank) for create paths that always set tags.
    /// </summary>
    public static async Task<string> NormalizeAndEnsureRequiredAsync(
        ListRepository lists,
        string? tags,
        Uuid7? customerId,
        CancellationToken ct)
    {
        var normalized = TagText.Normalize(tags);
        await lists.EnsureTagsAsync(normalized, Scope(customerId), ct);
        return normalized;
    }

    private static Uuid7? Scope(Uuid7? customerId)
    {
        if (customerId is null || customerId.Value == WellKnownIds.None)
            return null;
        return customerId;
    }
}
