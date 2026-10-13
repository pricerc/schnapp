namespace Schnapp.Client.Core.Tags;

/// <summary>
/// Shared multi-tag (token-aware) autocomplete helpers for desktop editors.
/// </summary>
/// <remarks>
/// Tokens are delimited by space, comma, semicolon, or tab — matching free-text
/// entry conventions. The active token is the one under the caret.
/// </remarks>
public static class TagTokenAutocomplete
{
    private static readonly char[] TokenDelimiters = { ' ', ',', ';', '\t', '\r', '\n' };

    /// <summary>Bounds and text of the token containing <paramref name="caret"/>.</summary>
    public static TokenSpan TokenAt(string? text, int caret)
    {
        text ??= "";
        if (caret < 0)
        {
            caret = 0;
        }

        if (caret > text.Length)
        {
            caret = text.Length;
        }

        var start = caret;
        while (start > 0 && !IsDelimiter(text[start - 1]))
        {
            start--;
        }

        var end = caret;
        while (end < text.Length && !IsDelimiter(text[end]))
        {
            end++;
        }

        return new TokenSpan(start, end, text.Substring(start, end - start));
    }

    /// <summary>
    /// Prefix-filter suggestions (case-insensitive). Excludes an exact match of the
    /// current token so the dropdown does not re-offer what is already typed.
    /// </summary>
    public static IReadOnlyList<string> Filter(
        IEnumerable<string>? candidates,
        string? prefix,
        int max = 12)
    {
        if (candidates is null || max <= 0)
        {
            return Array.Empty<string>();
        }

        prefix ??= "";
        if (prefix.Length == 0)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>(Math.Min(max, 12));
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c))
            {
                continue;
            }

            if (string.Equals(c, prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(c);
                if (list.Count >= max)
                {
                    break;
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Replaces <paramref name="span"/> in <paramref name="text"/> with
    /// <paramref name="suggestion"/> and a trailing space when appropriate.
    /// </summary>
    /// <returns>New full text and the caret position after the inserted token.</returns>
    public static (string Text, int Caret) Apply(string? text, TokenSpan span, string suggestion)
    {
        text ??= "";
        suggestion ??= "";
        var before = text.Substring(0, span.Start);
        var after = text.Substring(span.End);
        var needsSpace = after.Length == 0 || !IsDelimiter(after[0]);
        var insert = needsSpace ? suggestion + " " : suggestion;
        var next = before + insert + after;
        return (next, before.Length + insert.Length);
    }

    private static bool IsDelimiter(char c)
    {
        for (var i = 0; i < TokenDelimiters.Length; i++)
        {
            if (TokenDelimiters[i] == c)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Inclusive start / exclusive end of a token within a tags field.</summary>
    public readonly struct TokenSpan
    {
        public TokenSpan(int start, int end, string token)
        {
            Start = start;
            End = end;
            Token = token ?? "";
        }

        public int Start { get; }
        public int End { get; }
        public string Token { get; }
        public int Length => End - Start;
    }
}
