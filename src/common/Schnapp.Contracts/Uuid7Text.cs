namespace Schnapp.Contracts;

using System;

using Medo;

/// <summary>
/// Canonical text form for <see cref="Uuid7"/> values: Id26 (26-char Base-32 plus checksum).
/// Parsing also accepts legacy RFC UUID strings (D/N/B/P) for transition.
/// </summary>
public static class Uuid7Text
{
    /// <summary>Formats <paramref name="value"/> as Id26 text.</summary>
    public static string Format(Uuid7 value) => value.ToId26String();

    /// <summary>Formats a nullable id as Id26, or empty string when null.</summary>
    public static string Format(Uuid7? value) => value is null ? string.Empty : value.Value.ToId26String();

    /// <summary>Formats the underlying <see cref="Uuid7"/> of a route/query param as Id26.</summary>
    public static string Format(Uuid7Param value) => value.Value.ToId26String();

    /// <summary>
    /// Parses Id26 or legacy RFC UUID text into a <see cref="Uuid7"/>.
    /// </summary>
    /// <param name="s">Input text.</param>
    /// <returns>Parsed id.</returns>
    /// <exception cref="FormatException">When the text is not a valid Uuid7 form.</exception>
    public static Uuid7 Parse(string? s)
    {
        if (TryParse(s, out var id))
        {
            return id;
        }

        throw new FormatException("Invalid Uuid7 text: " + s);
    }

    /// <summary>
    /// Attempts to parse Id26 or legacy RFC UUID text.
    /// </summary>
    /// <param name="s">Input text.</param>
    /// <param name="id">Parsed id on success; <see cref="Uuid7.Empty"/> on failure.</param>
    /// <returns>True when parsing succeeds.</returns>
    public static bool TryParse(string? s, out Uuid7 id)
    {
        id = Uuid7.Empty;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        s = s!.Trim();

        if (Uuid7.TryParse(s, out id))
        {
            return true;
        }

        try
        {
            id = Uuid7.FromId26String(s);
            return true;
        }
        catch (FormatException)
        {
            id = Uuid7.Empty;
            return false;
        }
        catch (ArgumentException)
        {
            id = Uuid7.Empty;
            return false;
        }
        catch (IndexOutOfRangeException)
        {
            id = Uuid7.Empty;
            return false;
        }
    }
}
