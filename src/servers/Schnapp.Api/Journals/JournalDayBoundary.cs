using Schnapp.Api.Screenshots;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;

using Medo;

namespace Schnapp.Api.Journals;

/// <summary>
/// Closes open journals at local midnight and clones them for the new day (profile timezone).
/// </summary>
/// <remarks>
/// Journals are day-scoped segments. An open entry whose start falls on a previous local calendar
/// day is closed at that day's end (start of next local day, UTC) and a new open entry is inserted
/// with the same machine, target, notes, and tags. Multi-day gaps produce one closed segment per
/// intervening day so covering-by-time stays correct.
/// </remarks>
public static class JournalDayBoundary
{
    /// <summary>
    /// Ensures every open journal for <paramref name="userId"/> starts on the user's local today
    /// (or later). Returns a map from closed predecessor ids to the final open clone id for that
    /// chain (for remapping stale client journal links on upload).
    /// </summary>
    public static async Task<IReadOnlyDictionary<Uuid7, Uuid7>> EnsureAsync(
        Uuid7 userId,
        string timeZoneId,
        JournalRepository journals,
        CancellationToken ct = default)
    {
        var openList = await journals.ListOpenForUserAsync(userId, ct);
        if (openList.Count == 0)
            return new Dictionary<Uuid7, Uuid7>();

        var tz = SafeTz(timeZoneId);
        var nowUtc = DateTimeOffset.UtcNow;
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc.UtcDateTime, tz));
        var remap = new Dictionary<Uuid7, Uuid7>();

        foreach (var open in openList)
        {
            var current = open;
            var originId = open.Id;
            var startLocal = LocalDate(current.StartUtc, tz);

            while (startLocal < todayLocal)
            {
                var (_, dayEndUtc) = DayRange.ForLocalDate(startLocal, timeZoneId);
                var closeAt = dayEndUtc;
                // Never write a stop before start; never close into the future past "now".
                if (closeAt <= current.StartUtc)
                    closeAt = current.StartUtc;
                if (closeAt > nowUtc)
                    closeAt = nowUtc;

                await journals.CloseAsync(current.Id, userId, closeAt, ct);

                // Another request may already have opened today's clone.
                var existingOpen = await journals.GetOpenAsync(userId, current.MachineId, ct);
                if (existingOpen is not null)
                {
                    remap[originId] = existingOpen.Id;
                    if (current.Id != originId)
                        remap[current.Id] = existingOpen.Id;
                    break;
                }

                var clone = new JournalEntry
                {
                    Id = IdFactory.NewId(),
                    UserId = userId,
                    MachineId = current.MachineId,
                    StartUtc = closeAt,
                    StopUtc = null,
                    JournalTargetId = current.JournalTargetId,
                    Notes = current.Notes,
                    Tags = current.Tags,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                try
                {
                    await journals.InsertAsync(clone, ct);
                }
                catch
                {
                    existingOpen = await journals.GetOpenAsync(userId, current.MachineId, ct);
                    if (existingOpen is not null)
                    {
                        remap[originId] = existingOpen.Id;
                        break;
                    }
                    throw;
                }

                remap[originId] = clone.Id;
                if (current.Id != originId)
                    remap[current.Id] = clone.Id;

                current = clone;
                startLocal = LocalDate(current.StartUtc, tz);

                // If we had to close at "now" (clock / TZ edge), stop looping.
                if (closeAt >= nowUtc)
                    break;
            }
        }

        return remap;
    }

    /// <summary>
    /// After <see cref="EnsureAsync"/>, resolve a client-supplied journal id that may have been
    /// closed by day rollover to the current open clone. Does not rewrite unrelated historical links.
    /// </summary>
    public static async Task<Uuid7?> RemapJournalIdAsync(
        Uuid7 userId,
        Uuid7? journalEntryId,
        Uuid7 machineId,
        JournalRepository journals,
        IReadOnlyDictionary<Uuid7, Uuid7> remap,
        CancellationToken ct = default)
    {
        if (journalEntryId is null || journalEntryId == Uuid7.Empty)
            return journalEntryId;

        var id = journalEntryId.Value;
        // Prefer the explicit clone chain from this Ensure call (handles multi-day hops).
        if (remap.TryGetValue(id, out var mapped))
            return mapped;

        var entry = await journals.GetForUserAsync(id, userId, ct);
        if (entry is null)
            return journalEntryId;

        if (entry.StopUtc is null)
            return entry.Id;

        // Stale client id after a prior rollover: open clone starts exactly when this entry stopped
        // and shares machine + target (day-boundary chain). Unrelated closed journals stay as-is.
        var open = await journals.GetOpenAsync(userId, machineId, ct);
        if (open is not null
            && open.MachineId == entry.MachineId
            && open.JournalTargetId == entry.JournalTargetId
            && open.StartUtc == entry.StopUtc)
            return open.Id;

        return entry.Id;
    }

    private static DateOnly LocalDate(DateTimeOffset utc, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, tz));

    private static TimeZoneInfo SafeTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }
}
