namespace Schnapp.Api.Screenshots;

/// <summary>
/// Converts a local calendar date in a user's IANA timezone to a UTC half-open range.
/// </summary>
public static class DayRange
{
    /// <summary>
    /// Returns [startUtc, endUtc) for local date <paramref name="date"/> in <paramref name="timeZoneId"/>.
    /// </summary>
    /// <param name="date">Local calendar date (year/month/day).</param>
    /// <param name="timeZoneId">IANA timezone id (e.g. Pacific/Auckland); falls back to UTC if invalid.</param>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ForLocalDate(DateOnly date, string timeZoneId)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);
        return (new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(endUtc, TimeSpan.Zero));
    }
}
