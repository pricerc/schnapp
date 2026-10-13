namespace Schnapp.Contracts;

/// <summary>
/// Normalizes free-text tag fields to space-separated tokens for storage and dictionary collation.
/// </summary>
/// <remarks>
/// Split delimiters (initial): comma, semicolon, tab. Tokens are trimmed; empties dropped.
/// Persistence format is single-space-separated (see docs/mvp-tech-plan.md Tags section).
/// </remarks>
public static class TagText
{
    private static readonly char[] Delimiters = { ',', ';', '\t' };

    /// <summary>
    /// Splits input on punctuation delimiters and re-joins as single-space-separated tokens.
    /// </summary>
    /// <param name="input">Raw tags from CLI/UI (any mix of delimiters), or null.</param>
    /// <returns>Space-separated tags, or empty string when there are no tokens.</returns>
    public static string Normalize(string? input)
    {
        var tokens = Split(input);
        return tokens.Count == 0 ? "" : string.Join(" ", tokens);
    }

    /// <summary>
    /// Returns individual tag tokens from a raw or already-normalized tags string.
    /// </summary>
    /// <param name="input">Raw or space-separated tags text.</param>
    /// <returns>Non-empty trimmed tokens in order of first appearance (duplicates kept).</returns>
    public static IReadOnlyList<string> Split(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<string>();
        }

        // First split on punctuation, then on whitespace so already-normalized values work.
        var parts = input!.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();
        foreach (var part in parts)
        {
            // null separator = any whitespace (space, tab already handled as delimiter too).
            foreach (var word in part.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = word.Trim();
                if (t.Length > 0)
                {
                    tokens.Add(t);
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// Distinct tokens in first-seen order (case-sensitive), after <see cref="Split"/>.
    /// </summary>
    public static IReadOnlyList<string> DistinctTokens(string? input)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var t in Split(input))
        {
            if (seen.Add(t))
            {
                list.Add(t);
            }
        }
        return list;
    }
}
