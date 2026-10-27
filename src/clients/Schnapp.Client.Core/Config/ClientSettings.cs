namespace Schnapp.Client.Core.Config;

using Medo;

/// <summary>
/// Persisted client preferences (not the org server Setting table).
/// </summary>
public sealed class ClientSettings
{
    /// <summary>API base URL, e.g. <c>http://127.0.0.1:5080</c>.</summary>
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5080";

    /// <summary>Last login identity (email, code, or name).</summary>
    public string? LoginIdentity { get; set; }

    /// <summary>Default machine id after register/use (link).</summary>
    public Uuid7? MachineId { get; set; }

    /// <summary>Default machine code (display).</summary>
    public string? MachineCode { get; set; }

    /// <summary>Default machine name (display).</summary>
    public string? MachineName { get; set; }

    /// <summary>Preferred IANA timezone for CLI timestamp parsing.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Last position/size of the offline queue window (pixels / DIPs).</summary>
    public SavedWindowBounds? OfflineQueueWindow { get; set; }

    /// <summary>Last position/size of the main recorder window.</summary>
    public SavedWindowBounds? MainWindow { get; set; }

    /// <summary>
    /// MRU journal target ids (most recent first), max ~20.
    /// </summary>
    public List<Uuid7> JournalTargetMru { get; set; } = [];

    /// <summary>Seconds between automatic captures (min 15).</summary>
    public int CaptureIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Snapshot the focused window once it has kept focus this many seconds.
    /// 0 = off. Independent of <see cref="CaptureActiveWindow"/> and the interval timer.
    /// </summary>
    public int WindowFocusDwellSeconds { get; set; }

    /// <summary>
    /// When true, the capture timer was running (not paused) when the app last saved settings.
    /// On startup the tray client auto-starts capture only if this is true.
    /// </summary>
    public bool CaptureRunning { get; set; }

    /// <summary>
    /// When true, automatic background pushes (mutation queue + screenshot spool) are paused —
    /// the client won't try to reach the API on its own. Manual pushes (CLI, offline queue
    /// window) still work regardless of this flag.
    /// </summary>
    public bool UploadsPaused { get; set; }

    /// <summary>
    /// Capture full root desktop (one stitched image). Combinable with other targets.
    /// Default on for first use (no monitors/window).
    /// </summary>
    public bool CaptureFullDesktop { get; set; } = true;

    /// <summary>Capture monitor(s) as separate images. Combinable with other targets.</summary>
    public bool CaptureMonitors { get; set; }

    /// <summary>Capture the focused window. Combinable with other targets.</summary>
    public bool CaptureActiveWindow { get; set; }

    /// <summary>
    /// Zero-based monitor indexes when <see cref="CaptureMonitors"/> is true.
    /// Empty = every connected monitor.
    /// </summary>
    public List<int> CaptureMonitorIndexes { get; set; } = [];

    /// <summary>
    /// Legacy exclusive mode (older settings.json). Read on load then cleared; omitted when null on save.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CaptureTargetMode? CaptureMode { get; set; }

    /// <summary>
    /// Applies <see cref="CaptureMode"/> if present, then ensures at least one target is enabled.
    /// </summary>
    public void NormalizeCaptureTargets()
    {
        if (CaptureMode is { } mode)
        {
            CaptureFullDesktop = mode == CaptureTargetMode.FullDesktop;
            CaptureActiveWindow = mode == CaptureTargetMode.ActiveWindow;
            CaptureMonitors = mode is CaptureTargetMode.AllMonitors or CaptureTargetMode.SpecifiedMonitors;
            CaptureMode = null;
        }

        if (!CaptureFullDesktop && !CaptureMonitors && !CaptureActiveWindow)
        {
            CaptureFullDesktop = true;
        }

        WindowFocusDwellSeconds = Clamp(WindowFocusDwellSeconds, 0, 3600);

        if (MonitorSettings != null)
        {
            foreach (var kv in MonitorSettings)
            {
                kv.Value?.Normalize();
            }
        }
        DesktopSettings?.Normalize();
    }

    /// <summary>Max edge length after resize before encode (manual/settings override; default 16384 = effectively off).</summary>
    public int CaptureMaxEdge { get; set; } = 16384;

    /// <summary>Output image format (default JPEG).</summary>
    public CaptureImageFormat CaptureFormat { get; set; } = CaptureImageFormat.Jpeg;

    /// <summary>
    /// Quality 1-100 for lossy formats (JPEG / WebP lossy). Ignored for PNG / WebP lossless.
    /// </summary>
    public int CaptureQuality { get; set; } = 80;

    /// <summary>Legacy settings.json key; maps onto <see cref="CaptureQuality"/>.</summary>
    public int CaptureJpegQuality
    {
        set => CaptureQuality = value;
    }

    /// <summary>
    /// Per-monitor capture overrides keyed by zero-based monitor index (string).
    /// Missing entry = auto scale and global quality.
    /// </summary>
    public Dictionary<string, MonitorCaptureSettings> MonitorSettings { get; set; } = [];

    /// <summary>
    /// When true, the timer skips capture while a screensaver is active.
    /// Manual “Capture now” still runs. Default on.
    /// </summary>
    public bool SkipWhenScreensaverActive { get; set; } = true;

    /// <summary>
    /// Minutes of no keyboard/mouse input before the timer skips capture.
    /// 0 = disabled. Manual “Capture now” ignores this. Default 0.
    /// Useful when screensaver detection is unavailable (RDP, VMs, etc.).
    /// </summary>
    public int IdlePauseMinutes { get; set; }

    /// <summary>
    /// When true, timer captures that are all-black (lock screen) are not spooled.
    /// Manual “Capture now” still saves them. Default on.
    /// </summary>
    public bool DiscardBlankDuplicates { get; set; } = true;

    /// <summary>
    /// When true, timer captures whose grid differs from the last kept frame by at most
    /// <see cref="IdleMaxDirtyCells"/> cells are not spooled. Manual “Capture now” still saves.
    /// Default on.
    /// </summary>
    public bool DiscardIdleDuplicates { get; set; } = true;

    /// <summary>
    /// How many source-grid cells may change and still count as idle (clock / toast).
    /// Clamped to 1-10 on 16x8 grids and 1-20 on larger desktops. Default 2.
    /// </summary>
    public int IdleMaxDirtyCells { get; set; } = 2;

    /// <summary>
    /// Full-desktop capture: optional X11 scale override + output size %.
    /// </summary>
    public DesktopCaptureSettings DesktopSettings { get; set; } = new();

    /// <summary>
    /// Combined post-capture multiply for a monitor: X11 scale x output %.
    /// <paramref name="detectedX11Scale"/> is auto RandR factor (≤1).
    /// </summary>
    public double ResolveMonitorScale(int monitorIndex, double detectedX11Scale)
    {
        MonitorSettings.TryGetValue(monitorIndex.ToString(), out var ms);
        return (ms ?? new MonitorCaptureSettings()).ResolveCombinedFactor(detectedX11Scale);
    }

    /// <summary>
    /// Full-desktop multiply: min auto scale across monitors (or override) x desktop output %.
    /// </summary>
    public double ResolveDesktopScale(double autoMinScale)
    {
        var ds = DesktopSettings ?? new DesktopCaptureSettings();
        return ds.ResolveCombinedFactor(autoMinScale);
    }

    /// <summary>
    /// Resolves encode quality for a monitor (or global when index null / no override).
    /// </summary>
    public int ResolveMonitorQuality(int? monitorIndex)
    {
        if (monitorIndex is int i
            && MonitorSettings != null
            && MonitorSettings.TryGetValue(i.ToString(), out var ms)
            && ms.Quality is int q)
        {
            return Clamp(q, 1, 100);
        }

        return Clamp(CaptureQuality, 1, 100);
    }

    public static double Clamp(double v, double min, double max) =>
        v < min ? min : (v > max ? max : v);

    public static int Clamp(int v, int min, int max) =>
        v < min ? min : (v > max ? max : v);

    /// <summary>
    /// Optional absolute path for the offline screenshot spool.
    /// Empty = <c>~/.local/share/schnapp/spool</c> (or <c>SCHNAPP_SPOOL_DIR</c>).
    /// </summary>
    public string? SpoolDirectory { get; set; }

    /// <summary>
    /// Optional absolute path for the offline mutation queue.
    /// Empty = <c>~/.local/share/schnapp/mutations</c> (or <c>SCHNAPP_MUTATIONS_DIR</c>).
    /// </summary>
    public string? MutationsDirectory { get; set; }

    /// <summary>Log severity levels. Higher value = more verbose.</summary>
    public enum ClientLogLevel
    {
        Off = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4
    }

    /// <summary>
    /// Minimum level for dated log files under <c>~/.local/share/schnapp/logs</c>.
    /// Off disables file logging entirely. Console logging uses separate CLI switches.
    /// </summary>
    public ClientLogLevel FileLogLevel { get; set; } = ClientLogLevel.Info;

    /// <summary>
    /// Settings for the recent-screenshots local cache (already-uploaded items kept on disk).
    /// See <c>ScreenshotCacheHousekeeping</c> for the eviction policy that reads these.
    /// </summary>
    public CacheSettings Cache { get; set; } = new();
}

/// <summary>Legacy enum kept for old settings.json only.</summary>
public enum XlibreScaleMode
{
    None = 0,
    ContentUp = 1,
    ContentDown = 2,
}

/// <summary>Encoded screenshot format for upload/spool.</summary>
public enum CaptureImageFormat
{
    Jpeg = 0,
    Png = 1,
    Webp = 2,
    WebpLossless = 3,
}

/// <summary>Legacy exclusive capture mode (settings.json migration only).</summary>
public enum CaptureTargetMode
{
    FullDesktop = 0,
    AllMonitors = 1,
    SpecifiedMonitors = 2,
    ActiveWindow = 3,
}
