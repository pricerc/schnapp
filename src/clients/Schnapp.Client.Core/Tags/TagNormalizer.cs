namespace Schnapp.Client.Core.Tags;

using Schnapp.Contracts;

/// <summary>
/// Normalizes tag strings for API write: split on punctuation, store space-separated.
/// </summary>
/// <remarks>
/// Thin wrapper over <see cref="TagText"/> for existing client call sites.
/// Delimiters (initial): comma, semicolon, tab. Full list TBD per plan.
/// </remarks>
public static class TagNormalizer
{
    /// <summary>
    /// Splits and re-joins tags as single-space-separated tokens.
    /// </summary>
    /// <param name="input">Raw tags from CLI/UI (any mix of delimiters).</param>
    /// <returns>Space-separated tags, or empty string.</returns>
    public static string Normalize(string input) => TagText.Normalize(input);
}
