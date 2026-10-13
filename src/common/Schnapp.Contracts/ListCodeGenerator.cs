namespace Schnapp.Contracts;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Short unique codes for customers, projects, and tasks.
/// Format: 2-4 character alphabetic stem from the name plus a 3-digit sequence
/// (e.g. <c>MI001</c>, <c>SCHN002</c>).
/// </summary>
public static class ListCodeGenerator
{
    /// <summary>Minimum alphabetic stem length.</summary>
    public const int MinStemLength = 2;

    /// <summary>Maximum alphabetic stem length.</summary>
    public const int MaxStemLength = 4;

    /// <summary>Zero-padded numeric suffix width.</summary>
    public const int SequenceDigits = 3;

    /// <summary>Highest sequence number allocated before falling back to a suffix with <c>X</c>.</summary>
    public const int MaxSequence = 999;

    /// <summary>
    /// Builds a 2-4 character uppercase stem from a display name.
    /// Multi-word names use initials; single words use a leading letter run.
    /// Reserved or empty stems become <c>XX</c>.
    /// </summary>
    /// <param name="name">Display name to derive a stem from.</param>
    /// <returns>Uppercase alphabetic stem of length 2-4.</returns>
    public static string StemFromName(string? name)
    {
        var words = SplitWords(name);
        string stem;
        if (words.Count == 0)
        {
            stem = "XX";
        }
        else if (words.Count == 1)
        {
            var w = words[0];
            var take = Math.Min(MaxStemLength, Math.Max(MinStemLength, w.Length));
            if (w.Length < MinStemLength)
            {
                stem = (w + "XX").Substring(0, MinStemLength);
            }
            else
            {
                stem = w.Substring(0, Math.Min(take, w.Length));
            }

            if (stem.Length > MaxStemLength)
            {
                stem = stem.Substring(0, MaxStemLength);
            }

            if (stem.Length < MinStemLength)
            {
                stem = (stem + "XX").Substring(0, MinStemLength);
            }
        }
        else
        {
            var sb = new StringBuilder(MaxStemLength);
            foreach (var w in words)
            {
                if (sb.Length >= MaxStemLength)
                {
                    break;
                }

                if (w.Length > 0)
                {
                    sb.Append(w[0]);
                }
            }
            // Pad from first word if we only got one initial somehow
            var first = words[0];
            var i = 1;
            while (sb.Length < MinStemLength && i < first.Length)
            {
                sb.Append(first[i]);
                i++;
            }
            while (sb.Length < MinStemLength)
            {
                sb.Append('X');
            }

            stem = sb.ToString();
            if (stem.Length > MaxStemLength)
            {
                stem = stem.Substring(0, MaxStemLength);
            }
        }

        stem = stem.ToUpperInvariant();
        if (string.Equals(stem, "NONE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stem, "NULL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stem, "-", StringComparison.Ordinal))
        {
            stem = "XX";
        }

        return stem;
    }

    /// <summary>
    /// Next free code for <paramref name="name"/> given already-used codes
    /// (case-insensitive). Always includes a zero-padded 3-digit suffix starting at 001.
    /// </summary>
    /// <param name="name">Display name used to derive the stem.</param>
    /// <param name="existingCodes">Codes already taken in the same list table.</param>
    /// <returns>A free code such as <c>ACME001</c>.</returns>
    public static string Allocate(string? name, IEnumerable<string?> existingCodes)
    {
        var stem = StemFromName(name);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingCodes != null)
        {
            foreach (var c in existingCodes)
            {
                if (string.IsNullOrWhiteSpace(c))
                {
                    continue;
                }

                taken.Add(c!.Trim());
            }
        }

        for (var n = 1; n <= MaxSequence; n++)
        {
            var code = stem + n.ToString("D" + SequenceDigits);
            if (!taken.Contains(code))
            {
                return code;
            }
        }

        // Pathological: all 001-999 taken for this stem
        return stem + MaxSequence.ToString("D" + SequenceDigits) + "X";
    }

    private static List<string> SplitWords(string? name)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return list;
        }

        var text = name!.Trim();
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                list.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list;
    }
}
