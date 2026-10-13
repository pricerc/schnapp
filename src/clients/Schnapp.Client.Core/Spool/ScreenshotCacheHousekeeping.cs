namespace Schnapp.Client.Core.Spool;

using System.Collections.Generic;

using Schnapp.Client.Core.Config;

using Medo;

/// <summary>
/// Enforces the recent-screenshots local cache size cap: once already-uploaded items exceed
/// <see cref="CacheSettings.MaxCacheBytes"/>, the oldest-uploaded ones are discarded first
/// (FIFO) until back under the cap. Deliberately kept separate from <see cref="ScreenshotSpool"/>
/// itself and from its own pending-item caps (max age/count/bytes) — this class only ever touches
/// already-uploaded (cached) items, never anything still waiting to be uploaded. Housekeeping
/// policy may get smarter later (e.g. thumbnail-only retention, per-day quotas); keeping it in its
/// own class with its own settings block means that can happen without touching the spool itself.
/// </summary>
public sealed class ScreenshotCacheHousekeeping
{
    private readonly ScreenshotSpool _spool;
    private readonly CacheSettings _settings;

    /// <summary>Creates housekeeping bound to a spool and the cache settings that govern it.</summary>
    /// <param name="spool">Spool whose already-uploaded (cached) items are managed.</param>
    /// <param name="settings">Cache settings (max bytes); read fresh on each <see cref="Enforce"/> call.</param>
    public ScreenshotCacheHousekeeping(ScreenshotSpool spool, CacheSettings settings)
    {
        _spool = spool;
        _settings = settings;
    }

    /// <summary>
    /// Evicts the oldest already-uploaded items until total cached bytes are within
    /// <see cref="CacheSettings.MaxCacheBytes"/>. Cheap no-op when already within the cap.
    /// </summary>
    /// <returns>Number of items evicted.</returns>
    public int Enforce()
    {
        var maxBytes = _settings.MaxCacheBytes > 0 ? _settings.MaxCacheBytes : long.MaxValue;
        var uploaded = _spool.ListUploadedItems(); // oldest-uploaded first
        long total = 0;
        foreach (var item in uploaded)
        {
            total += item.ByteSize;
        }

        if (total <= maxBytes)
        {
            return 0;
        }

        var evictIds = new List<Uuid7>();
        var index = 0;
        while (total > maxBytes && index < uploaded.Count)
        {
            var item = uploaded[index];
            evictIds.Add(item.Id);
            total -= item.ByteSize;
            index++;
        }

        return evictIds.Count == 0 ? 0 : _spool.DeleteItems(evictIds);
    }
}
