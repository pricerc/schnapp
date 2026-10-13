namespace Schnapp.Client.Core.Time;

using System;
using System.Globalization;

/// <summary>
/// Parses CLI-style capture/journal times into UTC <see cref="DateTimeOffset"/>.
/// </summary>
public static class TimestampParser
{
    /// <summary>
    /// Parses <paramref name="value"/> as now, time-today, or full local datetime.
    /// </summary>
    /// <param name="value">
    /// <c>now</c>, <c>HH:mm</c> / <c>H:mm</c>, or a parseable local datetime string.
    /// </param>
    /// <param name="timeZoneId">IANA or Windows zone id; null uses local system zone.</param>
    /// <returns>UTC instant.</returns>
    /// <exception cref="FormatException">When the value cannot be parsed.</exception>
    public static DateTimeOffset Parse(string? value, string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {

            throw new FormatException("Timestamp value is empty.");
        }

#pragma warning disable CS8602 // null dereference: we check for null/whitespace
        var v = value.Trim();
#pragma warning restore CS8602 // null dereference: we check for null/whitespace        
        if (string.Equals(v, "now", StringComparison.OrdinalIgnoreCase))
        {

            return DateTimeOffset.UtcNow;
        }


        var tz = ResolveTz(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);

        // HH:mm or H:mm today
        if (TimeSpan.TryParseExact(v, new[] { @"h\:mm", @"hh\:mm", @"H\:mm", @"HH\:mm" },
                CultureInfo.InvariantCulture, out var tod)
            || TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out tod))
        {
            if (tod.TotalHours >= 24)
            {

                throw new FormatException("Time of day out of range: " + v);
            }


            var localDate = localNow.Date.Add(tod);
            var unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }

        if (DateTime.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            if (dt.Kind == DateTimeKind.Utc)
            {

                return new DateTimeOffset(dt, TimeSpan.Zero);
            }


            var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }

        throw new FormatException("Unrecognized timestamp: " + v);
    }

    private static TimeZoneInfo ResolveTz(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {

            return TimeZoneInfo.Local;
        }


        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Do not silently substitute a different zone for one the caller explicitly
            // configured (e.g. via `schnapp config set timezone`) — that would make
            // timestamps wrong in a way that's very hard to notice later.
            throw new FormatException(
                $"Unknown time zone id '{timeZoneId}'. Run 'schnapp config set timezone <iana-id>' with a valid id.", ex);
        }
    }
}
