namespace Schnapp.Client.Linux.Services;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Imaging;
using Schnapp.Client.Core.Spool;
using Schnapp.Client.Core.Upload;
using Schnapp.Client.Linux.Capture;
using Schnapp.Contracts;
using Schnapp.Imaging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Timed screen capture using Core config/API/spool and a platform <see cref="IScreenCapture"/>.
/// Each tick always enqueues encoded frames into the offline spool; a background loop pushes them to the API.
/// </summary>
/// <remarks>
/// Publishes short <see cref="TrayStatus"/> values for the tray menu; full detail is for the status dialog.
/// Supports active window, per-monitor, full-desktop, and selected-monitor modes; JPEG/PNG/WebP encode.
/// </remarks>
public sealed class CaptureService : IDisposable
{
    private readonly IScreenCapture _capture;
    private readonly IScreensaverDetector _screensaver;
    private readonly IUserIdleDetector _idle;
    private readonly ClientSettingsStore _store = new();
    private readonly ScreenshotSpool _spool;
    private readonly ScreenshotCacheHousekeeping _cacheHousekeeping;
    private readonly UploadService _uploads;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private CancellationTokenSource? _uploadsCts;
    private Task? _uploadsLoop;
    private static readonly TimeSpan UploadsPushInterval = TimeSpan.FromSeconds(20);
    private int _pushInFlight; // 0 = idle, 1 = a push (manual or background) is running
    private TrayStatus _lastStatus = new()
    {
        Level = TrayStatusLevel.Info,
        ShortLabel = "Idle",
        Detail = "Capture has not started yet."
    };

    /// <summary>
    /// Optional provider for the currently open journal entry id (set by
    /// <see cref="JournalSessionService"/> wiring). When non-null, every capture
    /// attaches that id as <see cref="ScreenshotUploadMetadata.JournalEntryId"/>.
    /// </summary>
    public Func<Uuid7?>? ActiveJournalEntryIdProvider { get; set; }

    /// <summary>
    /// Optional hook run at the start of each capture tick (e.g. journal day-boundary refresh).
    /// </summary>
    public Func<Task>? BeforeCaptureAsync { get; set; }

    /// <summary>
    /// Creates a capture service bound to the given platform capture backend.
    /// </summary>
    /// <param name="capture">Screen capture implementation (X11 or fallback).</param>
    /// <param name="screensaver">Optional screensaver detector; defaults to <see cref="LinuxScreensaverDetector"/>.</param>
    /// <param name="idle">Optional idle detector; defaults to <see cref="LinuxUserIdleDetector"/>.</param>
    public CaptureService(
        IScreenCapture capture,
        IScreensaverDetector? screensaver = null,
        IUserIdleDetector? idle = null)
    {
        _capture = capture;
        _screensaver = screensaver ?? new LinuxScreensaverDetector();
        _idle = idle ?? new LinuxUserIdleDetector();
        var settings = _store.Load();
        var databasePath = ClientPaths.GetDatabasePath();
        _spool = new ScreenshotSpool(settingsSpoolDirectory: settings.SpoolDirectory, databasePath: databasePath);
        _cacheHousekeeping = new ScreenshotCacheHousekeeping(_spool, settings.Cache);
        _uploads = new UploadService(settingsMutationsDirectory: settings.MutationsDirectory, referenceDataDbPath: databasePath);
        _uploadsPaused = settings.UploadsPaused;
        AppLog.Debug($"CaptureService: created backend={capture.GetType().Name} spoolDir≈{settings.SpoolDirectory ?? "(def)"} mutDir≈{settings.MutationsDirectory ?? "(def)"}");
        StartUploadsLoop();
    }

    /// <summary>When true, the timer skips capture.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
            {
                return;
            }

            _paused = value;
            PersistCaptureRunning(!value);
        }
    }

    private bool _paused = true;

    /// <summary>
    /// When true, automatic background pushes (mutation queue + screenshot spool) are skipped —
    /// the client won't try to reach the API on its own. Manual pushes (CLI, offline queue
    /// window, <see cref="PushSpoolAsync"/>) still work regardless of this flag.
    /// </summary>
    public bool UploadsPaused
    {
        get => _uploadsPaused;
        set
        {
            if (_uploadsPaused == value)
            {
                return;
            }

            _uploadsPaused = value;
            PersistUploadsPaused(value);
        }
    }

    private bool _uploadsPaused;

    /// <summary>Last kept fingerprint per capture target (anchor for idle-duplicate skip).</summary>
    private readonly Dictionary<string, FrameFingerprint> _lastKept = [];

    /// <summary>Total items awaiting automatic push across both offline queues (mutations + screenshots).</summary>
    public int PendingUploadsCount => _uploads.PendingCount + _spool.GetStatus().PendingCount;

    /// <summary>
    /// Writes <see cref="ClientSettings.CaptureRunning"/> without re-entering <see cref="Paused"/>.
    /// </summary>
    private void PersistCaptureRunning(bool running)
    {
        try
        {
            var s = _store.Load();
            if (s.CaptureRunning == running)
            {
                return;
            }

            s.CaptureRunning = running;
            _store.Save(s);
            AppLog.Debug($"CaptureService: persisted CaptureRunning={running}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureService: failed to persist CaptureRunning: " + ex.Message);
        }
    }

    /// <summary>
    /// Writes <see cref="ClientSettings.UploadsPaused"/> without re-entering <see cref="UploadsPaused"/>.
    /// </summary>
    private void PersistUploadsPaused(bool paused)
    {
        try
        {
            var s = _store.Load();
            if (s.UploadsPaused == paused)
            {
                return;
            }

            s.UploadsPaused = paused;
            _store.Save(s);
            AppLog.Debug($"CaptureService: persisted UploadsPaused={paused}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureService: failed to persist UploadsPaused: " + ex.Message);
        }
    }

    /// <summary>Raised when status should update (may be from a background thread).</summary>
    public event EventHandler<TrayStatus>? StatusChanged;

    /// <summary>Interval seconds between captures (from settings; default 300).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>Access to the offline screenshot spool for the queue UI.</summary>
    public ScreenshotSpool Spool => _spool;

    /// <summary>Access to the offline upload service (mutation queue + reference-data cache).</summary>
    public UploadService Uploads => _uploads;

    /// <summary>Number of pending spool items (after prune).</summary>
    public int SpoolPendingCount
    {
        get
        {
            _spool.PruneExpired();
            return _spool.GetStatus().PendingCount;
        }
    }

    /// <summary>
    /// Reloads the capture interval from persisted settings.
    /// </summary>
    /// <param name="settings">Loaded client settings.</param>
    public void ApplySettings(ClientSettings settings)
    {
        var seconds = Math.Clamp(settings.CaptureIntervalSeconds, 15, 3600);
        Interval = TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Builds the current tray status, applying pause / spool overlays on the last event.
    /// </summary>
    /// <returns>Status for the tray menu and detail dialog.</returns>
    public TrayStatus GetStatus()
    {
        var settings = _store.Load();
        var spool = _spool.GetStatus();
        var tokenMissing = string.IsNullOrEmpty(_store.LoadToken())
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN"));

        if (tokenMissing)
        {
            return new TrayStatus
            {
                Level = TrayStatusLevel.Error,
                ShortLabel = "No login",
                Detail = "Not signed in. Open Login… from the tray menu.",
                At = _lastStatus.At
            };
        }

        if (!settings.MachineId.HasValue)
        {
            return new TrayStatus
            {
                Level = TrayStatusLevel.Error,
                ShortLabel = "No machine",
                Detail = "No default machine selected. Open Machines… to register or use one.",
                At = _lastStatus.At
            };
        }

        if (Paused)
        {
            return new TrayStatus
            {
                Level = TrayStatusLevel.Warning,
                ShortLabel = "Paused",
                Detail = "Capture is paused. Choose Resume capture to continue.\n\n"
                         + "Last: " + (_lastStatus.Detail ?? _lastStatus.ShortLabel),
                At = DateTimeOffset.Now
            };
        }

        if (spool.FailedCount > 0)
        {
            return new TrayStatus
            {
                Level = TrayStatusLevel.Warning,
                ShortLabel = "Spool errors",
                Detail = $"Offline spool has {spool.FailedCount} item(s) with errors "
                         + $"({spool.PendingCount} pending, {spool.TotalBytes} bytes).\n"
                         + "Use Push offline spool or: schnapp spool push\n\n"
                         + "Last: " + (_lastStatus.Detail ?? _lastStatus.ShortLabel),
                At = DateTimeOffset.Now
            };
        }

        var deadMutations = _uploads.DeadCount;
        if (deadMutations > 0)
        {
            return new TrayStatus
            {
                Level = TrayStatusLevel.Warning,
                ShortLabel = "Upload errors",
                Detail = $"{deadMutations} offline mutation(s) failed permanently (business-logic "
                         + "rejection, e.g. a referenced customer/project/task no longer exists) "
                         + "and won't be retried automatically.\n"
                         + "Use: schnapp spool status --verbose to inspect, or clear them.\n\n"
                         + "Last: " + (_lastStatus.Detail ?? _lastStatus.ShortLabel),
                At = DateTimeOffset.Now
            };
        }

        if (spool.PendingCount > 0 && _lastStatus.Level != TrayStatusLevel.Error)
        {
            if (_lastStatus.Level == TrayStatusLevel.Warning
                || _lastStatus.ShortLabel.Contains("Queued", StringComparison.OrdinalIgnoreCase))
            {
                return _lastStatus;
            }

            return new TrayStatus
            {
                Level = TrayStatusLevel.Warning,
                ShortLabel = "Queued",
                Detail = $"{spool.PendingCount} screenshot(s) waiting in offline spool "
                         + $"({spool.TotalBytes} bytes). Use Push offline spool when the API is up.\n\n"
                         + "Last: " + (_lastStatus.Detail ?? _lastStatus.ShortLabel),
                At = DateTimeOffset.Now
            };
        }

        return _lastStatus;
    }

    /// <summary>
    /// Starts the background capture timer loop.
    /// Restores pause state from <see cref="ClientSettings.CaptureRunning"/> unless
    /// <paramref name="forceRunning"/> is set.
    /// </summary>
    /// <param name="forceRunning">
    /// When true, clears pause and marks capture as running in settings.
    /// When false (default), starts the loop only if settings say capture was running;
    /// otherwise leaves the timer stopped/paused until the user starts it.
    /// </param>
    public Task StartAsync(bool forceRunning = false)
    {
        var settings = _store.Load();
        ApplySettings(settings);

        if (forceRunning)
        {
            _paused = false;
            PersistCaptureRunning(true);
        }
        else if (!settings.CaptureRunning)
        {
            _paused = true;
            AppLog.Info("Capture not auto-started (was paused/stopped last session)");
            SetStatus(TrayStatusLevel.Warning, "Paused",
                "Capture is paused. Choose Resume capture or Start to continue.");
            return Task.CompletedTask;
        }
        else
        {
            _paused = false;
        }

        if (_cts != null)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        AppLog.Info($"Capture timer started (every {Interval.TotalSeconds:0}s)");
        AppLog.Debug($"CaptureService: StartAsync interval={Interval} paused={Paused}");
        SetStatus(TrayStatusLevel.Ok, "Running",
            $"Capture timer started (every {Interval.TotalSeconds:0}s).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ensures the background timer loop is running (used when resuming from paused-at-startup).
    /// </summary>
    public void EnsureTimerRunning()
    {
        if (_cts != null)
        {
            return;
        }

        ApplySettings(_store.Load());
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        AppLog.Info($"Capture timer started (every {Interval.TotalSeconds:0}s)");
        SetStatus(TrayStatusLevel.Ok, "Running",
            $"Capture timer started (every {Interval.TotalSeconds:0}s).");
    }

    /// <summary>
    /// Stops the timer, waits briefly for the loop, and disposes capture resources.
    /// </summary>
    public void Dispose()
    {
        AppLog.Debug("CaptureService: Dispose");
        _cts?.Cancel();
        try { _loop?.Wait(2000); } catch { /* ignore */ }
        _cts?.Dispose();
        _uploadsCts?.Cancel();
        try { _uploadsLoop?.Wait(2000); } catch { /* ignore */ }
        _uploadsCts?.Dispose();
        if (_capture is IDisposable d)
        {
            d.Dispose();
        }

        if (_screensaver is IDisposable sd)
        {
            sd.Dispose();
        }

        if (_idle is IDisposable id)
        {
            id.Dispose();
        }

        _spool.Dispose();
    }

    /// <summary>
    /// Starts the background upload-push loop (mutation queue + screenshot spool), independent
    /// of the capture timer. Runs on a much shorter interval than capture so offline mutations
    /// (e.g. journal starts made while offline) get retried promptly once the API is reachable.
    /// Skips pushing entirely while <see cref="UploadsPaused"/> is true.
    /// </summary>
    private void StartUploadsLoop()
    {
        _uploadsCts = new CancellationTokenSource();
        _uploadsLoop = Task.Run(() => UploadsLoopAsync(_uploadsCts.Token));
    }

    private async Task UploadsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TryBackgroundPushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Uploads loop error: " + ex.Message);
            }

            try
            {
                await Task.Delay(UploadsPushInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Pushes mutation queue first (list creates + journals), then screenshot spool.
    /// Returns (0, 0) without doing anything if a push (manual or background) is already running.
    /// </summary>
    /// <returns>Succeeded and failed counts across both queues.</returns>
    public async Task<(int Ok, int Fail)> PushSpoolAsync()
    {
        if (Interlocked.CompareExchange(ref _pushInFlight, 1, 0) != 0)
        {
            SetStatus(TrayStatusLevel.Info, "Push busy", "A push is already in progress — try again shortly.");
            return (0, 0);
        }

        try
        {
            using var api = CreateApi();
            var mut = await _uploads.PushAsync(api).ConfigureAwait(false);
            var r = await _spool.PushAsync(api, retainUploaded: true).ConfigureAwait(false);
            if (r.Succeeded > 0)
            {
                try { _cacheHousekeeping.Enforce(); }
                catch (Exception hkEx) { AppLog.Warn("Cache: housekeeping failed: " + hkEx.Message); }
            }
            var ok = mut.Succeeded + r.Succeeded;
            var fail = mut.Failed + r.Failed;
            if (fail > 0 && ok == 0)
            {
                SetStatus(TrayStatusLevel.Error, "Push failed",
                    $"Could not flush offline queue (mutations {mut.Failed}, screenshots {r.Failed}). Check API connectivity and login.");
            }
            else if (fail > 0)
            {
                SetStatus(TrayStatusLevel.Warning, "Push partial",
                    $"Pushed mutations {mut.Succeeded}/{mut.Failed}, screenshots {r.Succeeded}/{r.Failed}. "
                    + $"Remaining: {_uploads.PendingCount} mutations, {_spool.GetStatus().PendingCount} shots.");
            }
            else if (ok == 0)
            {
                SetStatus(TrayStatusLevel.Info, "Queue empty",
                    "No due offline mutations or screenshots to push.");
            }
            else
            {
                SetStatus(TrayStatusLevel.Ok, "Queue OK",
                    $"Pushed {mut.Succeeded} mutation(s) and {r.Succeeded} screenshot(s).");
            }
            return (ok, fail);
        }
        catch (Exception ex)
        {
            SetStatus(TrayStatusLevel.Error, "Push failed",
                "Offline push error: " + ex.Message);
            return (0, 1);
        }
        finally
        {
            Interlocked.Exchange(ref _pushInFlight, 0);
        }
    }

    /// <summary>
    /// Sets a status from the app (e.g. login cancelled) without capturing.
    /// </summary>
    /// <param name="level">Severity for tray coloring.</param>
    /// <param name="shortLabel">Short tray menu label.</param>
    /// <param name="detail">Full text for the status dialog.</param>
    public void Report(TrayStatusLevel level, string shortLabel, string? detail)
    {
        AppLog.Debug($"Report: {level} {shortLabel} | {(detail ?? "").Replace('\n',' ')}");
        SetStatus(level, shortLabel, detail ?? "");
    }

    /// <summary>
    /// Captures once and uploads (or queues). Manual capture ignores screensaver.
    /// </summary>
    public async Task CaptureOnceAsync()
    {
        await CaptureAndUploadAsync(respectScreensaver: false, ct: CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Background loop: capture when not paused, then delay by <see cref="Interval"/>.
    /// </summary>
    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!Paused)
                {
                    await CaptureAndUploadAsync(respectScreensaver: true, ct: ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Loop error: " + ex.Message);
                SetStatus(TrayStatusLevel.Error, "Error", ex.Message);
            }

            try
            {
                ApplySettings(_store.Load());
                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// One capture tick: validate, grab frames, encode, always enqueue to the spool, then kick background push.
    /// </summary>
    /// <param name="respectScreensaver">
    /// When true, honor screensaver and idle skip policies; manual capture passes false.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    private async Task CaptureAndUploadAsync(bool respectScreensaver, CancellationToken ct)
    {
        if (BeforeCaptureAsync is not null)
        {
            try
            {
                await BeforeCaptureAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn("BeforeCapture hook failed: " + ex.Message);
            }
        }

        var settings = _store.Load();
        ApplySettings(settings);

        if (!TryValidateReady(settings))
        {
            return;
        }

        if (respectScreensaver)
        {
            if (await ShouldSkipForScreensaverAsync(settings, ct).ConfigureAwait(false))
            {
                return;
            }

            if (await ShouldSkipForIdleAsync(settings, ct).ConfigureAwait(false))
            {
                return;
            }
        }

        if (!TryResolveCapturePlan(settings, out var captureWindow, out var captureMonitors,
                out var captureFull, out var monitorIndexes, out var planError))
        {
            SetStatus(TrayStatusLevel.Warning, "Capture targets", planError!);
            return;
        }

        var modes = DescribePlan(captureWindow, captureMonitors, captureFull, monitorIndexes);
        AppLog.Info("Capture start: " + modes);
        SetStatus(TrayStatusLevel.Info, "Capturing…", modes);

        var captures = await GrabFramesAsync(captureWindow, captureMonitors, captureFull, monitorIndexes, ct)
            .ConfigureAwait(false);
        if (captures == null)
        {
            return;
        }

        if (captures.Count == 0)
        {
            SetStatus(TrayStatusLevel.Error, "Capture failed",
                "Screen capture returned no images. Need an X11 session (native capture) "
                + "or ImageMagick import / scrot / gnome-screenshot on PATH.");
            return;
        }

        var captureTime = DateTimeOffset.UtcNow;
        var encodeOpts = ResolveEncodeOptions(settings);
        var (queued, skipped, lastError) = await EncodeAndEnqueueAllAsync(
            captures, settings, encodeOpts, captureTime, respectScreensaver, ct).ConfigureAwait(false);

        // Always enqueue first; background loop (and this kick) dequeues to the API.
        await TryBackgroundPushAsync(ct).ConfigureAwait(false);

        try { _cacheHousekeeping.Enforce(); }
        catch (Exception ex) { AppLog.Warn("Cache: housekeeping failed: " + ex.Message); }

        ReportCaptureOutcome(queued, skipped, lastError, captureTime);
        AppLog.Info($"Capture result: queued={queued} skipped={skipped}");
    }

    /// <summary>
    /// Ensures login token and default machine are present; updates tray status on failure.
    /// </summary>
    /// <param name="settings">Current client settings.</param>
    /// <returns>True when capture may proceed.</returns>
    private bool TryValidateReady(ClientSettings settings)
    {
        if (string.IsNullOrEmpty(_store.LoadToken())
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN")))
        {
            SetStatus(TrayStatusLevel.Error, "No login",
                "Not signed in. Open Login… from the tray menu.");
            return false;
        }

        if (!settings.MachineId.HasValue)
        {
            SetStatus(TrayStatusLevel.Error, "No machine",
                "No default machine selected. Open Machines… to register or use one.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true when capture should be skipped because the screensaver is active.
    /// </summary>
    /// <param name="settings">Settings including <see cref="ClientSettings.SkipWhenScreensaverActive"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<bool> ShouldSkipForScreensaverAsync(ClientSettings settings, CancellationToken ct)
    {
        if (!settings.SkipWhenScreensaverActive)
        {
            return false;
        }

        try
        {
            if (!await _screensaver.IsActiveAsync(ct).ConfigureAwait(false))
            {
                return false;
            }

            AppLog.Debug("Screensaver active; skipping capture");
            SetStatus(TrayStatusLevel.Info, "Screensaver",
                "Skipped capture while screensaver is active.");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when the timer should skip because the user has been idle longer
    /// than <see cref="ClientSettings.IdlePauseMinutes"/>.
    /// </summary>
    /// <param name="settings">Settings including idle pause minutes (0 = disabled).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<bool> ShouldSkipForIdleAsync(ClientSettings settings, CancellationToken ct)
    {
        var minutes = ClientSettings.Clamp(settings.IdlePauseMinutes, 0, 1440);
        if (minutes <= 0)
        {
            return false;
        }

        try
        {
            var idle = await _idle.GetIdleTimeAsync(ct).ConfigureAwait(false);
            if (idle is null)
            {
                // Because unknown idle time must fail open so capture is not blocked forever.
                return false;
            }

            if (idle.Value < TimeSpan.FromMinutes(minutes))
            {
                return false;
            }

            AppLog.Debug($"User idle {idle.Value.TotalMinutes:F1}m >= {minutes}m; skipping capture");
            SetStatus(TrayStatusLevel.Info, "Idle",
                $"Skipped capture: no user activity for {minutes} minute(s).");
            return true;
        }
        catch
        {
            // Because detector failures should not block capture permanently.
            return false;
        }
    }

    /// <summary>
    /// Resolves independent capture targets and optional monitor index list from settings.
    /// </summary>
    /// <returns>False when monitors are enabled but an empty explicit selection was intended incorrectly — not used; empty list means all.</returns>
    private static bool TryResolveCapturePlan(
        ClientSettings settings,
        out bool captureWindow,
        out bool captureMonitors,
        out bool captureFull,
        out IReadOnlyList<int>? monitorIndexes,
        out string? error)
    {
        settings.NormalizeCaptureTargets();
        captureWindow = settings.CaptureActiveWindow;
        captureMonitors = settings.CaptureMonitors;
        captureFull = settings.CaptureFullDesktop;
        monitorIndexes = null;
        error = null;

        if (!captureWindow && !captureMonitors && !captureFull)
        {
            error = "No capture targets enabled. Open Settings… and choose full desktop, monitors, and/or active window.";
            return false;
        }

        if (captureMonitors)
        {
            var indexes = (settings.CaptureMonitorIndexes ?? [])
                .Where(i => i >= 0)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            // Empty list ⇒ all monitors (handled by capture backend).
            monitorIndexes = indexes.Count == 0 ? null : indexes;
        }

        return true;
    }

    /// <summary>
    /// Invokes the platform capture backend for the enabled target combination.
    /// </summary>
    private async Task<IReadOnlyList<ScreenCaptureResult>?> GrabFramesAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFull,
        IReadOnlyList<int>? monitorIndexes,
        CancellationToken ct)
    {
        try
        {
            AppLog.Debug($"CaptureService: GrabFrames w={captureWindow} m={captureMonitors} f={captureFull}");
            var res = await _capture.CaptureAsync(captureWindow, captureMonitors, captureFull, monitorIndexes, ct)
                .ConfigureAwait(false);
            AppLog.Debug($"CaptureService: GrabFrames -> {(res?.Count ?? 0)} frames");
            return res;
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture failed: " + ex.Message);
            SetStatus(TrayStatusLevel.Error, "Capture failed",
                "Screen capture error: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Encode options derived from settings for one capture tick (per-frame quality/scale applied later).
    /// </summary>
    private readonly record struct EncodeOptions(
        int MaxEdge,
        CaptureImageFormat Format,
        int DefaultQuality,
        string ContentType,
        string Extension);

    /// <summary>
    /// Builds encode options (format, default quality, max edge, content-type) from settings.
    /// </summary>
    private static EncodeOptions ResolveEncodeOptions(ClientSettings settings)
    {
        var format = settings.CaptureFormat;
        if (!Enum.IsDefined(format))
        {
            format = CaptureImageFormat.Jpeg;
        }

        var (contentType, ext) = FormatInfo(format);
        var opts = new EncodeOptions(
            Math.Clamp(settings.CaptureMaxEdge, 320, 16384),
            format,
            Math.Clamp(settings.CaptureQuality, 1, 100),
            contentType,
            ext);
        AppLog.Debug($"CaptureService: encode fmt={format} maxEdge={opts.MaxEdge} q={opts.DefaultQuality}");
        return opts;
    }

    /// <summary>
    /// Resizes, encodes, and always enqueues every captured frame for background upload. Disposes each result.
    /// </summary>
    /// <param name="captures">Frames from the capture backend.</param>
    /// <param name="settings">Settings for machine id and metadata.</param>
    /// <param name="opts">Encode options for this tick.</param>
    /// <param name="captureTime">Shared timestamp for all frames in the tick.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Queue count, skip count, and last error message.</returns>
    private async Task<(int Queued, int Skipped, string? LastError)> EncodeAndEnqueueAllAsync(
        IReadOnlyList<ScreenCaptureResult> captures,
        ClientSettings settings,
        EncodeOptions opts,
        DateTimeOffset captureTime,
        bool allowSkip,
        CancellationToken ct)
    {
        var queued = 0;
        var skipped = 0;
        string? lastError = null;

        foreach (var cap in captures)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await EncodeAndEnqueueOneAsync(cap, settings, opts, captureTime, allowSkip, ct)
                .ConfigureAwait(false);
            if (outcome.Queued)
            {
                queued++;
            }

            if (outcome.Skipped)
            {
                skipped++;
            }

            if (outcome.Error != null)
            {
                lastError = outcome.Error;
            }
        }

        return (queued, skipped, lastError);
    }

    /// <summary>Outcome of encoding and spooling a single frame.</summary>
    private readonly record struct EnqueueOutcome(bool Queued, bool Skipped, string? Error);

    /// <summary>
    /// Encodes one frame and always enqueues it for the background upload loop
    /// (local cache + durable offline queue).
    /// </summary>
    /// <param name="cap">Frame (disposed by this method).</param>
    /// <param name="settings">Client settings.</param>
    /// <param name="opts">Encode options.</param>
    /// <param name="captureTime">Capture timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<EnqueueOutcome> EncodeAndEnqueueOneAsync(
        ScreenCaptureResult cap,
        ClientSettings settings,
        EncodeOptions opts,
        DateTimeOffset captureTime,
        bool allowSkip,
        CancellationToken ct)
    {
        // Snapshot labels before dispose — Image is freed with the result.
        var captureType = cap.CaptureType;
        var monitorIndex = cap.MonitorIndex;
        var monitorName = ResolveMonitorName(cap);
        var windowTitle = cap.WindowTitle;
        var programName = cap.ProgramName;
        var fpKey = FingerprintKey(captureType, monitorIndex, monitorName);

        byte[] encoded;
        int width;
        int height;
        FrameFingerprint? fingerprint = null;
        try
        {
            using (cap)
            {
                var image = cap.Image;
                var quality = settings.ResolveMonitorQuality(monitorIndex);
                var scale = ResolveFrameScale(settings, cap);
                AppLog.Debug(
                    $"Encode: {cap.CaptureType} {image.Width}x{image.Height} fmt={opts.Format} q={quality} scale={scale:F3}");
                ApplyMonitorScale(image, scale);
                ResizeIfNeeded(image, opts.MaxEdge);
                width = image.Width;
                height = image.Height;
                fingerprint = ComputeFingerprint(image);
                if (allowSkip && ShouldDiscardFrame(settings, fingerprint, fpKey, out var skipWhy))
                {
                    AppLog.Info($"Capture skip: {skipWhy} ({captureType} {width}x{height})");
                    return new EnqueueOutcome(false, true, null);
                }

                using var msOut = new MemoryStream();
                await EncodeAsync(image, msOut, opts.Format, quality, ct).ConfigureAwait(false);
                encoded = msOut.ToArray();
                AppLog.Debug($"Encode: produced {encoded.Length} bytes for {cap.CaptureType} ({width}x{height})");
            }
        }
        catch (Exception ex)
        {
            try { cap.Dispose(); } catch { /* ignore */ }
            AppLog.Error("Encode failed: " + ex.Message);
            return new EnqueueOutcome(false, false, ex.Message);
        }

        var journalId = ActiveJournalEntryIdProvider?.Invoke();
        var meta = new ScreenshotUploadMetadata
        {
            MachineId = settings.MachineId!.Value,
            Timestamp = captureTime,
            ClientTimeZoneId = settings.TimeZoneId ?? TimeZoneInfo.Local.Id,
            MonitorId = monitorIndex?.ToString(),
            MonitorName = monitorName,
            WindowTitle = windowTitle,
            ProgramName = programName,
            Width = width,
            Height = height,
            Fingerprint = fingerprint is null ? null : FingerprintMetadata.ToDto(fingerprint),
            // Per-image link: open journal at capture time (None when no journal active).
            JournalEntryId = journalId is { } jid && jid != Uuid7.Empty ? jid : null
        };
        var fileName = BuildFileName(captureType, monitorIndex, opts.Extension);

        try
        {
            var id = _spool.Enqueue(encoded, fileName, opts.ContentType, meta);
            if (fingerprint is not null)
            {
                _lastKept[fpKey] = fingerprint;
            }

            AppLog.Info($"Spool: enqueued {fileName} id={Uuid7Text.Format(id)}");
            return new EnqueueOutcome(true, false, null);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Spool: enqueue failed {fileName}: {ex.Message}");
            return new EnqueueOutcome(false, false, ex.Message);
        }
    }

    private bool ShouldDiscardFrame(
        ClientSettings settings,
        FrameFingerprint fingerprint,
        string key,
        out string reason)
    {
        if (settings.DiscardBlankDuplicates && fingerprint.IsBlack)
        {
            reason = "blank";
            return true;
        }

        if (settings.DiscardIdleDuplicates
            && _lastKept.TryGetValue(key, out var prev)
            && fingerprint.IsIdleDuplicateOf(prev, settings.IdleMaxDirtyCells))
        {
            var dirty = fingerprint.Compare(prev).DirtyCells;
            reason = $"idle ({dirty} dirty cells, max {settings.IdleMaxDirtyCells})";
            return true;
        }

        reason = "";
        return false;
    }

    private static string FingerprintKey(string captureType, int? monitorIndex, string? monitorName)
        => captureType + ":" + (monitorIndex?.ToString() ?? "") + ":" + (monitorName ?? "");

    private static FrameFingerprint ComputeFingerprint(Image image)
    {
        var rgba = image as Image<Rgba32> ?? image.CloneAs<Rgba32>();
        var own = !ReferenceEquals(rgba, image);
        try
        {
            var w = rgba.Width;
            var h = rgba.Height;
            var buf = new byte[checked(w * h * 4)];
            rgba.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    System.Runtime.InteropServices.MemoryMarshal.Cast<Rgba32, byte>(row)
                        .CopyTo(buf.AsSpan(y * w * 4));
                }
            });
            return FrameFingerprint.FromPixels(w, h, buf, w * 4, PixelLayout.Rgba32);
        }
        finally
        {
            if (own)
            {
                rgba.Dispose();
            }
        }
    }

    /// <summary>
    /// Combined XLibre x output resize multiply for monitor/window frames; full desktop = 1.0.
    /// </summary>
    private double ResolveFrameScale(ClientSettings settings, ScreenCaptureResult cap)
    {
        if (cap.CaptureType is "Monitor" or "Window")
        {
            if (cap.MonitorIndex is int mi)
            {
                double detected;
                try { detected = _capture.GetMonitorDetectedScale(mi); }
                catch { detected = 1.0; }
                return settings.ResolveMonitorScale(mi, detected);
            }
            return 1.0;
        }

        if (cap.CaptureType is "Full")
        {
            double autoMin;
            try { autoMin = _capture.GetDesktopAutoScale(); }
            catch { autoMin = 1.0; }
            return settings.ResolveDesktopScale(autoMin);
        }

        return 1.0;
    }

    /// <summary>
    /// Downscales by scale factor (e.g. 0.5 halves both axes) before max-edge clamp.
    /// </summary>
    private static void ApplyMonitorScale(Image image, double scale)
    {
        // scale is multiply (typically ≤1 after XLibre content-up + output %).
        if (scale <= 0 || Math.Abs(scale - 1.0) < 0.001)
        {
            return;
        }

        var w = Math.Max(1, (int)Math.Round(image.Width * scale));
        var h = Math.Max(1, (int)Math.Round(image.Height * scale));
        if (w == image.Width && h == image.Height)
        {
            return;
        }

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Stretch,
            Size = new Size(w, h)
        }));
    }

    /// <summary>
    /// Downscales the image so neither edge exceeds <paramref name="maxEdge"/>.
    /// </summary>
    /// <param name="image">Image to mutate in place.</param>
    /// <param name="maxEdge">Maximum width or height in pixels.</param>
    private static void ResizeIfNeeded(Image image, int maxEdge)
    {
        if (image.Width <= maxEdge && image.Height <= maxEdge)
        {
            return;
        }

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxEdge, maxEdge)
        }));
    }

    /// <summary>
    /// Updates tray status after a full capture tick (frames spooled; upload is background).
    /// </summary>
    private void ReportCaptureOutcome(int queued, int skipped, string? lastError, DateTimeOffset captureTime)
    {
        if (queued > 0)
        {
            var skipNote = skipped > 0 ? $", skipped {skipped} duplicate/blank" : "";
            SetStatus(TrayStatusLevel.Ok, "OK",
                $"Captured {queued} screenshot(s) at {captureTime:u} (spooled for upload{skipNote}).");
        }
        else if (skipped > 0)
        {
            SetStatus(TrayStatusLevel.Info, "Idle",
                $"Skipped {skipped} blank or idle duplicate frame(s).");
        }
        else
        {
            SetStatus(TrayStatusLevel.Error, "Capture failed",
                lastError ?? "Could not encode or spool captures.");
        }
    }

    /// <summary>
    /// Best-effort flush of the offline mutation queue and screenshot spool. Used both after a
    /// successful capture tick and by the dedicated <see cref="UploadsLoopAsync"/> timer. No-op
    /// while <see cref="UploadsPaused"/> is true — the client won't attempt to reach the API.
    /// Also no-op if a push (this one or a manual <see cref="PushSpoolAsync"/>) is already running.
    /// </summary>
    private async Task TryBackgroundPushAsync(CancellationToken ct)
    {
        if (_uploadsPaused)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _pushInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            using var api = CreateApi();
            await _uploads.PushAsync(api, ct).ConfigureAwait(false);
            // Retain local copies as the recent-screenshots cache after a successful push.
            await _spool.PushAsync(api, ct, retainUploaded: true).ConfigureAwait(false);
        }
        catch
        {
            // ignore background flush errors
        }
        finally
        {
            Interlocked.Exchange(ref _pushInFlight, 0);
        }
    }

    /// <summary>
    /// Human-readable description of the capture plan for status/logging.
    /// </summary>
    private static string DescribePlan(
        bool window,
        bool monitors,
        bool full,
        IReadOnlyList<int>? monitorIndexes)
    {
        var parts = new List<string>();
        if (window)
        {
            parts.Add("active window");
        }

        if (monitors)
        {
            if (monitorIndexes is { Count: > 0 })
            {
                parts.Add("monitors " + string.Join(", ", monitorIndexes.Select(i => i + 1)));
            }
            else
            {
                parts.Add("all monitors");
            }
        }
        if (full)
        {
            parts.Add("full desktop");
        }

        return "Capturing " + string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// Resolves the monitor label stored on upload metadata.
    /// </summary>
    private static string ResolveMonitorName(ScreenCaptureResult cap)
    {
        if (!string.IsNullOrWhiteSpace(cap.MonitorName))
        {
            return cap.MonitorName!;
        }

        return cap.CaptureType switch
        {
            "Window" => "window",
            "Monitor" => cap.MonitorIndex is int i ? $"Monitor {i + 1}" : "monitor",
            _ => "desktop"
        };
    }

    /// <summary>
    /// Maps capture format to MIME type and file extension.
    /// </summary>
    private static (string contentType, string ext) FormatInfo(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => ("image/png", "png"),
        CaptureImageFormat.Webp or CaptureImageFormat.WebpLossless => ("image/webp", "webp"),
        _ => ("image/jpeg", "jpg")
    };

    /// <summary>
    /// Encodes an ImageSharp image to the configured format.
    /// </summary>
    /// <param name="image">Pixels to encode.</param>
    /// <param name="dest">Destination stream.</param>
    /// <param name="format">JPEG, PNG, or WebP (lossy/lossless).</param>
    /// <param name="quality">Quality for lossy formats (1-100).</param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task EncodeAsync(
        Image image,
        Stream dest,
        CaptureImageFormat format,
        int quality,
        CancellationToken ct)
    {
        switch (format)
        {
            case CaptureImageFormat.Png:
                await image.SaveAsPngAsync(dest, new PngEncoder(), ct).ConfigureAwait(false);
                break;
            case CaptureImageFormat.WebpLossless:
                await image.SaveAsWebpAsync(dest, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossless
                }, ct).ConfigureAwait(false);
                break;
            case CaptureImageFormat.Webp:
                await image.SaveAsWebpAsync(dest, new WebpEncoder
                {
                    Quality = quality,
                    FileFormat = WebpFileFormatType.Lossy
                }, ct).ConfigureAwait(false);
                break;
            default:
                await image.SaveAsJpegAsync(dest, new JpegEncoder { Quality = quality }, ct)
                    .ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Builds the multipart file name for a frame (e.g. <c>monitor-0.jpg</c>).
    /// </summary>
    private static string BuildFileName(string captureType, int? monitorIndex, string ext)
    {
        var baseName = captureType switch
        {
            "Window" => "window",
            "Monitor" => monitorIndex is int i ? $"monitor-{i}" : "monitor",
            _ => "desktop"
        };
        return baseName + "." + ext;
    }

    /// <summary>
    /// Creates an authenticated API client from settings and token store/env.
    /// </summary>
    private SchnappApiClient CreateApi()
    {
        var settings = _store.Load();
        var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
        return new SchnappApiClient(settings.ApiBaseUrl, token);
    }

    /// <summary>
    /// Stores the last status and raises <see cref="StatusChanged"/> with the effective tray status.
    /// </summary>
    private void SetStatus(TrayStatusLevel level, string shortLabel, string detail)
    {
        _lastStatus = new TrayStatus
        {
            Level = level,
            ShortLabel = shortLabel,
            Detail = detail,
            At = DateTimeOffset.Now
        };
        StatusChanged?.Invoke(this, GetStatus());
    }
}
