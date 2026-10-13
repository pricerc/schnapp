namespace Schnapp.Client.Core.Config;

/// <summary>
/// Settings for the local recent-screenshots cache: already-uploaded items are kept on disk
/// (not deleted after a successful upload) up to a total size cap, evicted oldest-first.
/// Pending/not-yet-uploaded items are never affected by this cap.
/// </summary>
public sealed class CacheSettings
{
    /// <summary>
    /// Max total bytes retained for already-uploaded (cached) items. Oldest-uploaded items are
    /// evicted first (FIFO) once the cap is exceeded. Default 2 GiB.
    /// </summary>
    public long MaxCacheBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}
