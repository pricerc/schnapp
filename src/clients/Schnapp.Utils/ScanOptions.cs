using Schnapp.Data;

namespace Schnapp.Utils;

/// <summary>CLI options for idle-scan (local spool or server blobs).</summary>
internal sealed record ScanOptions
{
    public string SpoolDirectory { get; init; } = "";
    public string MonitorName { get; init; } = "desktop";
    public bool AllMonitors { get; init; }
    public int ThumbWidth { get; init; } = 160;
    public int MinIdleMinutes { get; init; } = 15;
    public int MaxGapMinutes { get; init; } = 20;
    public TimeOnly ExpectedStart { get; init; } = new TimeOnly(1, 0);
    public TimeOnly ExpectedEnd { get; init; } = new TimeOnly(8, 0);
    public int? Limit { get; init; }

    /// <summary>
    /// Optional method name fragments (pixel, grid, chrome, hash, black).
    /// Null/empty = all methods.
    /// </summary>
    public string[]? Methods { get; init; }

    /// <summary>
    /// When true (default), duplicate detectors skip all-black lock-screen frames
    /// so they are judged on images that still have desktop content.
    /// The all-black method always sees the full set.
    /// </summary>
    public bool ExcludeBlack { get; init; } = true;

    /// <summary>If set, only frames at or after this UTC timestamp are loaded.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>If set, only frames before this UTC timestamp are loaded.</summary>
    public DateTimeOffset? UntilUtc { get; init; }

    /// <summary>When true, catalog + blob storage is the frame source instead of the local spool.</summary>
    public bool UseBlobs { get; init; }

    public string Root { get; init; } = ServerPaths.DefaultRoot;
    public string BlobRoot { get; init; } = "";
    public string? ConnectionString { get; init; }
    public string? ProviderName { get; init; }
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.PostgreSQL;

    /// <summary>Dupes: max dirty grid cells still counted as an idle duplicate (1-20).</summary>
    public int MaxDirtyCells { get; init; } = 2;

    /// <summary>Dupes: write newly computed fingerprints back to the catalog.</summary>
    public bool SaveFingerprints { get; init; } = true;

    /// <summary>Show percent, rate, and ETA while decoding / fingerprinting.</summary>
    public bool Progress { get; init; }
}
