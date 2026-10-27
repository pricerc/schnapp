namespace Schnapp.Client.Windows.Services;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Medo;

using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Capture;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Imaging;
using Schnapp.Client.Core.Spool;
using Schnapp.Client.Core.Upload;
using Schnapp.Client.Windows.Capture;
using Schnapp.Client.Windows.Encode;
using Schnapp.Contracts;
using Schnapp.Imaging;

/// <summary>
/// Timed screen capture using Core config/API/spool and a platform <see cref="IScreenCapture"/>.
/// Each tick always enqueues encoded frames into the offline spool; a background loop pushes them.
/// </summary>
/// <remarks>
/// Windows port of the Linux CaptureService — GDI capture + System.Drawing encode (no ImageSharp).
/// Publishes <see cref="TrayStatus"/> for the tray menu; full detail is for the status dialog.
/// </remarks>
public sealed class CaptureService : IDisposable
{
    /// <summary>Platform screen capture backend (GDI).</summary>
    private readonly IScreenCapture _capture;

    /// <summary>Screensaver/lock detector used when skip-on-screensaver is enabled.</summary>
    private readonly IScreensaverDetector _screensaver;

    /// <summary>User idle-time detector used when <c>IdlePauseMinutes</c> is greater than zero.</summary>
    private readonly IUserIdleDetector _idle;

    /// <summary>Loads/saves client settings (interval, machine, pause flags).</summary>
    private readonly ClientSettingsStore _store = new();

    /// <summary>Offline screenshot spool database and file storage.</summary>
    private readonly ScreenshotSpool _spool;

    /// <summary>Enforces local cache size/age limits after uploads.</summary>
    private readonly ScreenshotCacheHousekeeping _cacheHousekeeping;

    /// <summary>Offline mutation queue and reference-data cache.</summary>
    private readonly UploadService _uploads;

    /// <summary>Last kept fingerprint per capture target (anchor for idle-duplicate skip).</summary>
    private readonly Dictionary<string, FrameFingerprint> _lastKept = [];

    /// <summary>CTS for the capture timer loop; null until started.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Background task running <see cref="LoopAsync"/>.</summary>
    private Task? _loop;

    /// <summary>CTS for the automatic uploads push loop.</summary>
    private CancellationTokenSource? _uploadsCts;

    /// <summary>Background task running <see cref="UploadsLoopAsync"/>.</summary>
    private Task? _uploadsLoop;

    /// <summary>Delay between automatic background push attempts.</summary>
    private static readonly TimeSpan UploadsPushInterval = TimeSpan.FromSeconds(20);

    /// <summary>0/1 interlock so manual and background pushes do not overlap.</summary>
    private int _pushInFlight;

    /// <summary>Focus-session clock for the window-dwell snapshot.</summary>
    private readonly WindowFocusDwellTracker _dwell = new WindowFocusDwellTracker();

    /// <summary>Serializes interval captures and focus-dwell captures.</summary>
    private readonly SemaphoreSlim _captureGate = new SemaphoreSlim(1, 1);

    /// <summary>Background task running <see cref="FocusDwellLoopAsync"/>.</summary>
    private Task? _dwellLoop;

    /// <summary>Cached dwell from settings. Zero disables the trigger.</summary>
    private TimeSpan _focusDwell = TimeSpan.Zero;

    /// <summary>Next time the dwell loop rechecks screensaver and idle.</summary>
    private DateTime _nextDwellBlockCheckUtc = DateTime.MinValue;

    /// <summary>Last screensaver/idle result for the dwell loop.</summary>
    private bool _dwellBlocked;

    /// <summary>When true, the capture timer skips work each tick.</summary>
    private bool _paused = true;

    /// <summary>When true, automatic background pushes are skipped.</summary>
    private bool _uploadsPaused;

    /// <summary>Last raw status from capture/report (before pause/spool overlays).</summary>
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
    /// <param name="capture">Screen capture implementation (GDI).</param>
    /// <param name="screensaver">Optional screensaver detector; defaults to <see cref="WinScreensaverDetector"/>.</param>
    /// <param name="idle">Optional idle detector; defaults to <see cref="WinUserIdleDetector"/>.</param>
    public CaptureService(
        IScreenCapture capture,
        IScreensaverDetector? screensaver = null,
        IUserIdleDetector? idle = null)
    {
        _capture = capture;
        _screensaver = screensaver ?? new WinScreensaverDetector();
        _idle = idle ?? new WinUserIdleDetector();
        var settings = _store.Load();
        var databasePath = ClientPaths.GetDatabasePath();
        _spool = new ScreenshotSpool(settingsSpoolDirectory: settings.SpoolDirectory, databasePath: databasePath);
        _cacheHousekeeping = new ScreenshotCacheHousekeeping(_spool, settings.Cache);
        _uploads = new UploadService(settingsMutationsDirectory: settings.MutationsDirectory, referenceDataDbPath: databasePath);
        _uploadsPaused = settings.UploadsPaused;
        AppLog.Debug("CaptureService: created backend=" + capture.GetType().Name);
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

    /// <summary>
    /// When true, automatic background pushes are skipped. Manual pushes still work.
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

    /// <summary>Total items awaiting automatic push across both offline queues.</summary>
    public int PendingUploadsCount => _uploads.PendingCount + _spool.GetStatus().PendingCount;

    /// <summary>Raised when status should update (may be from a background thread).</summary>
    public event EventHandler<TrayStatus>? StatusChanged;

    /// <summary>Interval seconds between captures (from settings; default 300s).</summary>
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

    /// <summary>Platform capture backend (for settings UI monitor list).</summary>
    public IScreenCapture CaptureBackend => _capture;

    /// <summary>Reloads the capture interval from persisted settings.</summary>
    /// <param name="settings">Loaded client settings (interval clamped to 15-3600s).</param>
    public void ApplySettings(ClientSettings settings)
    {
        var seconds = settings.CaptureIntervalSeconds;
        if (seconds < 15)
        {
            seconds = 15;
        }

        if (seconds > 3600)
        {
            seconds = 3600;
        }

        Interval = TimeSpan.FromSeconds(seconds);
        var dwellSeconds = ClientSettings.Clamp(settings.WindowFocusDwellSeconds, 0, 3600);
        _focusDwell = TimeSpan.FromSeconds(dwellSeconds);
    }

    /// <summary>Builds the current tray status, applying pause / spool overlays.</summary>
    /// <returns>
    /// Overlay status (no login, no machine, paused, spool/upload errors, queued)
    /// or the last raw <see cref="_lastStatus"/> when healthy.
    /// </returns>
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
                Detail = "Offline spool has " + spool.FailedCount + " item(s) with errors "
                         + "(" + spool.PendingCount + " pending).\n"
                         + "Use Push offline spool from the tray.\n\n"
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
                Detail = deadMutations + " offline mutation(s) failed permanently.\n\n"
                         + "Last: " + (_lastStatus.Detail ?? _lastStatus.ShortLabel),
                At = DateTimeOffset.Now
            };
        }

        if (spool.PendingCount > 0 && _lastStatus.Level != TrayStatusLevel.Error)
        {
            if (_lastStatus.Level == TrayStatusLevel.Warning
                || (_lastStatus.ShortLabel != null
                    && _lastStatus.ShortLabel.IndexOf("Queued", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return _lastStatus;
            }

            return new TrayStatus
            {
                Level = TrayStatusLevel.Warning,
                ShortLabel = "Queued",
                Detail = spool.PendingCount + " screenshot(s) waiting in offline spool.\n\n"
                         + "Last: " + (_lastStatus.Detail ?? _lastStatus.ShortLabel),
                At = DateTimeOffset.Now
            };
        }

        return _lastStatus;
    }

    /// <summary>
    /// Starts the background capture timer loop.
    /// Restores pause state from <see cref="ClientSettings.CaptureRunning"/> unless forceRunning.
    /// </summary>
    /// <param name="forceRunning">
    /// When true, clears pause and persists running even if last session was paused.
    /// </param>
    /// <returns>Completed task (loop runs in the background).</returns>
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

        StartLoops();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ensures the background timer loop is running (used when resuming from paused-at-startup).
    /// </summary>
    public void EnsureTimerRunning()
    {
        StartLoops();
    }

    /// <summary>Starts the interval loop and the focus-dwell watcher. No-op when already started.</summary>
    private void StartLoops()
    {
        if (_cts != null)
        {
            return;
        }

        ApplySettings(_store.Load());
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _dwellLoop = Task.Run(() => FocusDwellLoopAsync(_cts.Token));
        var dwellNote = _focusDwell > TimeSpan.Zero
            ? " Focus snapshot after " + _focusDwell.TotalSeconds.ToString("0") + "s."
            : "";
        AppLog.Info("Capture timer started (every " + Interval.TotalSeconds.ToString("0") + "s)." + dwellNote);
        SetStatus(TrayStatusLevel.Ok, "Running",
            "Capture timer started (every " + Interval.TotalSeconds.ToString("0") + "s)." + dwellNote);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        AppLog.Debug("CaptureService: Dispose");
        _cts?.Cancel();
        try { _loop?.Wait(2000); } catch { /* ignore */ }
        try { _dwellLoop?.Wait(2000); } catch { /* ignore */ }
        _cts?.Dispose();
        _captureGate.Dispose();
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

    /// <summary>Pushes mutation queue first, then screenshot spool.</summary>
    /// <returns>Aggregate succeeded/failed counts across mutations and screenshots.</returns>
    public async Task<(int Ok, int Fail)> PushSpoolAsync()
    {
        if (Interlocked.CompareExchange(ref _pushInFlight, 1, 0) != 0)
        {
            SetStatus(TrayStatusLevel.Info, "Push busy", "A push is already in progress — try again shortly.");
            return (0, 0);
        }

        try
        {
            using (var api = CreateApi())
            {
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
                        "Could not flush offline queue. Check API connectivity and login.");
                }
                else if (fail > 0)
                {
                    SetStatus(TrayStatusLevel.Warning, "Push partial",
                        "Pushed mutations " + mut.Succeeded + "/" + mut.Failed
                        + ", screenshots " + r.Succeeded + "/" + r.Failed + ".");
                }
                else if (ok == 0)
                {
                    SetStatus(TrayStatusLevel.Info, "Queue empty",
                        "No due offline mutations or screenshots to push.");
                }
                else
                {
                    SetStatus(TrayStatusLevel.Ok, "Queue OK",
                        "Pushed " + mut.Succeeded + " mutation(s) and " + r.Succeeded + " screenshot(s).");
                }
                return (ok, fail);
            }
        }
        catch (Exception ex)
        {
            SetStatus(TrayStatusLevel.Error, "Push failed", "Offline push error: " + ex.Message);
            return (0, 1);
        }
        finally
        {
            Interlocked.Exchange(ref _pushInFlight, 0);
        }
    }

    /// <summary>Sets a status from the app without capturing.</summary>
    /// <param name="level">Severity for the tray icon/menu.</param>
    /// <param name="shortLabel">Brief menu label.</param>
    /// <param name="detail">Longer explanation for the status dialog.</param>
    public void Report(TrayStatusLevel level, string shortLabel, string? detail)
    {
        AppLog.Debug("Report: " + level + " " + shortLabel);
        SetStatus(level, shortLabel, detail ?? "");
    }

    /// <summary>Captures once and enqueues (manual capture ignores screensaver).</summary>
    /// <returns>Task that completes when the single capture attempt finishes.</returns>
    public Task CaptureOnceAsync() =>
        CaptureAndUploadAsync(CancellationToken.None, respectScreensaver: false);

    /// <summary>Starts the background loop that periodically pushes offline queues.</summary>
    private void StartUploadsLoop()
    {
        _uploadsCts = new CancellationTokenSource();
        _uploadsLoop = Task.Run(() => UploadsLoopAsync(_uploadsCts.Token));
    }

    /// <summary>
    /// Forever loop: try background push, then delay <see cref="UploadsPushInterval"/>.
    /// </summary>
    /// <param name="ct">Cancellation when the service is disposed.</param>
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
    /// Best-effort automatic push when uploads are not paused and nothing else is pushing.
    /// </summary>
    /// <param name="ct">Cancellation token for API calls.</param>
    private async Task TryBackgroundPushAsync(CancellationToken ct)
    {
        if (UploadsPaused)
        {
            return;
        }

        // Because skip when a manual or concurrent background push is already running.
        if (Interlocked.CompareExchange(ref _pushInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (_uploads.PendingCount == 0 && _spool.GetStatus().PendingCount == 0)
            {
                return;
            }

            using (var api = CreateApi())
            {
                await _uploads.PushAsync(api, ct).ConfigureAwait(false);
                var r = await _spool.PushAsync(api, retainUploaded: true, ct: ct).ConfigureAwait(false);
                if (r.Succeeded > 0)
                {
                    try { _cacheHousekeeping.Enforce(); }
                    catch (Exception hkEx) { AppLog.Warn("Cache: housekeeping failed: " + hkEx.Message); }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("Background push: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _pushInFlight, 0);
        }
    }

    /// <summary>
    /// Capture timer loop: capture when not paused, then delay <see cref="Interval"/>.
    /// </summary>
    /// <param name="ct">Cancellation when disposed or loop restarted.</param>
    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!Paused)
                {
                    await CaptureAndUploadAsync(ct, respectScreensaver: true, applyWindowDedupe: true)
                        .ConfigureAwait(false);
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
    /// One capture tick: validate, optional screensaver/idle skip, grab frames, encode into spool.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="respectScreensaver">
    /// When true, honor screensaver and idle skip policies; manual capture passes false.
    /// </param>
    /// <param name="windowOnly">When true, capture the focused window and ignore the interval targets.</param>
    /// <param name="expectedWindowId">Window that must still be focused when <paramref name="windowOnly"/> is set.</param>
    /// <param name="applyWindowDedupe">
    /// When true, skip the active-window frame if that window was snapshotted in the last few seconds.
    /// </param>
    /// <returns>
    /// True when a focus-dwell session should be committed (shot taken, or a recent shot already covers it).
    /// </returns>
    private async Task<bool> CaptureAndUploadAsync(
        CancellationToken ct,
        bool respectScreensaver,
        bool windowOnly = false,
        long? expectedWindowId = null,
        bool applyWindowDedupe = false)
    {
        await _captureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await CaptureAndUploadCoreAsync(
                ct, respectScreensaver, windowOnly, expectedWindowId, applyWindowDedupe).ConfigureAwait(false);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    /// <summary>Body of <see cref="CaptureAndUploadAsync"/> while the capture gate is held.</summary>
    private async Task<bool> CaptureAndUploadCoreAsync(
        CancellationToken ct,
        bool respectScreensaver,
        bool windowOnly,
        long? expectedWindowId,
        bool applyWindowDedupe)
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
            return false;
        }

        if (respectScreensaver)
        {
            if (await ShouldSkipForScreensaverAsync(settings, ct, reportStatus: !windowOnly).ConfigureAwait(false))
            {
                return false;
            }

            if (await ShouldSkipForIdleAsync(settings, ct, reportStatus: !windowOnly).ConfigureAwait(false))
            {
                return false;
            }
        }

        bool captureWindow;
        bool captureMonitors;
        bool captureFull;
        IReadOnlyList<int>? monitorIndexes;
        if (windowOnly)
        {
            long? current = null;
            try
            {
                current = _capture.TryGetFocusedWindowId();
            }
            catch (Exception ex)
            {
                AppLog.Debug("Focus dwell: focused window query failed: " + ex.Message);
            }

            if (current != expectedWindowId || expectedWindowId == null || expectedWindowId.Value <= 0)
            {
                return false;
            }

            if (_dwell.WasRecentlyCaptured(expectedWindowId.Value, DateTime.UtcNow))
            {
                AppLog.Debug("Focus dwell: window 0x" + expectedWindowId.Value.ToString("x") + " already snapshotted");
                return true;
            }

            captureWindow = true;
            captureMonitors = false;
            captureFull = false;
            monitorIndexes = null;
        }
        else if (!TryResolveCapturePlan(settings, out captureWindow, out captureMonitors,
                out captureFull, out monitorIndexes, out var planError))
        {
            SetStatus(TrayStatusLevel.Warning, "Capture targets", planError!);
            return false;
        }
        else if (applyWindowDedupe && captureWindow && TrySkipRecentWindow(ref captureWindow)
            && !captureWindow && !captureMonitors && !captureFull)
        {
            return false;
        }

        var modes = windowOnly
            ? "Capturing focused window (focus snapshot)."
            : DescribePlan(captureWindow, captureMonitors, captureFull, monitorIndexes);
        AppLog.Info("Capture start: " + modes);
        SetStatus(TrayStatusLevel.Info, "Capturing…", modes);

        IReadOnlyList<ScreenCaptureResult>? captures;
        try
        {
            captures = await _capture.CaptureAsync(captureWindow, captureMonitors, captureFull, monitorIndexes, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture failed: " + ex.Message);
            if (windowOnly)
            {
                SetStatus(TrayStatusLevel.Warning, "Window focus", "Focused window could not be captured: " + ex.Message);
                return true;
            }

            SetStatus(TrayStatusLevel.Error, "Capture failed", "Screen capture error: " + ex.Message);
            return false;
        }

        if (captures == null || captures.Count == 0)
        {
            if (windowOnly)
            {
                AppLog.Info("Focus dwell: focused window could not be captured");
                SetStatus(TrayStatusLevel.Warning, "Window focus", "Focused window could not be captured.");
                return true;
            }

            SetStatus(TrayStatusLevel.Error, "Capture failed",
                "Screen capture returned no images.");
            return false;
        }

        var captureTime = DateTimeOffset.UtcNow;
        var formatInfo = ImageEncoder.ResolveFormat(settings.CaptureFormat);
        var maxEdge = settings.CaptureMaxEdge;
        if (maxEdge < 320)
        {
            maxEdge = 320;
        }

        if (maxEdge > 16384)
        {
            maxEdge = 16384;
        }

        var queued = 0;
        var skipped = 0;
        string? lastError = null;

        foreach (var cap in captures)
        {
            ct.ThrowIfCancellationRequested();
            var nativeWindowId = cap.NativeWindowId;
            var capturedType = cap.CaptureType;
            try
            {
                var outcome = EncodeAndEnqueueOne(
                    cap, settings, formatInfo, maxEdge, captureTime, respectScreensaver);
                if (outcome.Queued)
                {
                    queued++;
                    if (capturedType == "Window" && nativeWindowId is long wid)
                    {
                        _dwell.NoteCaptured(wid, DateTime.UtcNow);
                    }
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
            catch (Exception ex)
            {
                lastError = ex.Message;
                AppLog.Error("Encode/enqueue failed: " + ex.Message);
            }
        }

        // Because try an immediate push after capture so the user sees Queue OK sooner online.
        await TryBackgroundPushAsync(ct).ConfigureAwait(false);

        try { _cacheHousekeeping.Enforce(); }
        catch (Exception ex) { AppLog.Warn("Cache: housekeeping failed: " + ex.Message); }

        if (queued > 0)
        {
            var skipNote = skipped > 0 ? ", skipped " + skipped + " duplicate/blank" : "";
            SetStatus(TrayStatusLevel.Ok, "OK",
                "Last capture at " + captureTime.ToString("u") + " (" + queued + " images" + skipNote + ")");
        }
        else if (skipped > 0)
        {
            SetStatus(TrayStatusLevel.Info, "Idle",
                "Skipped " + skipped + " blank or idle duplicate frame(s).");
        }
        else
        {
            SetStatus(TrayStatusLevel.Error, "Capture failed",
                lastError ?? "No frames could be encoded.");
        }

        AppLog.Info("Capture result: queued=" + queued + " skipped=" + skipped);
        return windowOnly;
    }

    /// <summary>
    /// Drops the active-window target when that window was snapshotted moments ago.
    /// </summary>
    /// <returns>True when the window target was suppressed.</returns>
    private bool TrySkipRecentWindow(ref bool captureWindow)
    {
        if (!captureWindow)
        {
            return false;
        }

        long? id = null;
        try
        {
            id = _capture.TryGetFocusedWindowId();
        }
        catch (Exception ex)
        {
            AppLog.Debug("Capture: focused window query failed: " + ex.Message);
        }

        if (id is long wid && _dwell.WasRecentlyCaptured(wid, DateTime.UtcNow))
        {
            AppLog.Info("Skipping active-window capture; window 0x" + wid.ToString("x") + " was snapshotted recently");
            captureWindow = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// While capture is running, snapshot a window that keeps focus for <see cref="_focusDwell"/>.
    /// </summary>
    /// <param name="ct">Cancellation when the service is disposed.</param>
    private async Task FocusDwellLoopAsync(CancellationToken ct)
    {
        var poll = TimeSpan.FromMilliseconds(WindowFocusDwellTracker.PollMilliseconds);
        var nextSettingsUtc = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now >= nextSettingsUtc)
                {
                    ApplySettings(_store.Load());
                    nextSettingsUtc = now.AddSeconds(1);
                }

                if (Paused || _focusDwell <= TimeSpan.Zero)
                {
                    _dwell.Clear();
                }
                else
                {
                    await PollFocusDwellAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    break;
                }

                AppLog.Warn("Focus dwell loop: " + ex.Message);
            }

            try
            {
                // Because a disabled or paused watcher only needs to notice settings changes, not focus.
                var delay = !Paused && _focusDwell > TimeSpan.Zero
                    ? poll
                    : TimeSpan.FromSeconds(1);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One focus sample. Captures when the current window has been focused long enough.</summary>
    /// <param name="ct">Cancellation token.</param>
    private async Task PollFocusDwellAsync(CancellationToken ct)
    {
        var settings = _store.Load();
        if (!HasIdentity(settings))
        {
            _dwell.Clear();
            return;
        }

        if (await IsDwellBlockedAsync(settings, ct).ConfigureAwait(false))
        {
            _dwell.Clear();
            return;
        }

        long? id = null;
        try
        {
            id = _capture.TryGetFocusedWindowId();
        }
        catch (Exception ex)
        {
            AppLog.Debug("Focus dwell: focused window query failed: " + ex.Message);
        }

        var now = DateTime.UtcNow;
        // Because a null id must still be observed so time on the desktop does not count toward the previous window.
        if (!_dwell.IsDue(id, now, _focusDwell) || id == null)
        {
            return;
        }

        if (_dwell.WasRecentlyCaptured(id.Value, now))
        {
            _dwell.Commit(id.Value, now);
            return;
        }

        var commit = await CaptureAndUploadAsync(
            ct,
            respectScreensaver: true,
            windowOnly: true,
            expectedWindowId: id,
            applyWindowDedupe: false).ConfigureAwait(false);
        if (commit)
        {
            _dwell.Commit(id.Value, DateTime.UtcNow);
        }
    }

    /// <summary>True when login and a default machine are present, without updating tray status.</summary>
    /// <param name="settings">Current client settings.</param>
    private bool HasIdentity(ClientSettings settings)
    {
        if (!settings.MachineId.HasValue)
        {
            return false;
        }

        return !string.IsNullOrEmpty(_store.LoadToken())
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN"));
    }

    /// <summary>
    /// Screensaver and idle checks for the dwell watcher, refreshed every couple of seconds.
    /// </summary>
    /// <param name="settings">Current client settings.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<bool> IsDwellBlockedAsync(ClientSettings settings, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (now < _nextDwellBlockCheckUtc)
        {
            return _dwellBlocked;
        }

        _nextDwellBlockCheckUtc = now.AddSeconds(2);
        _dwellBlocked = await ShouldSkipForScreensaverAsync(settings, ct, reportStatus: false).ConfigureAwait(false)
            || await ShouldSkipForIdleAsync(settings, ct, reportStatus: false).ConfigureAwait(false);
        return _dwellBlocked;
    }

    /// <summary>
    /// Scales, clamps, encodes one frame and enqueues it in the offline spool.
    /// </summary>
    /// <param name="cap">Capture result (disposed by this method).</param>
    /// <param name="settings">Current settings for quality/scale/machine.</param>
    /// <param name="formatInfo">Resolved content-type, extension, and effective format.</param>
    /// <param name="maxEdge">Max edge length after scale (pixels).</param>
    /// <param name="captureTime">UTC timestamp shared by all frames in this tick.</param>
    private struct EnqueueOutcome
    {
        public bool Queued;
        public bool Skipped;
        public string? Error;

        public EnqueueOutcome(bool queued, bool skipped, string? error)
        {
            Queued = queued;
            Skipped = skipped;
            Error = error;
        }
    }

    /// <returns>Queued / skipped / error for one frame.</returns>
    private EnqueueOutcome EncodeAndEnqueueOne(
        ScreenCaptureResult cap,
        ClientSettings settings,
        (string ContentType, string Extension, CaptureImageFormat Effective) formatInfo,
        int maxEdge,
        DateTimeOffset captureTime,
        bool allowSkip)
    {
        var captureType = cap.CaptureType;
        var monitorIndex = cap.MonitorIndex;
        var monitorName = !string.IsNullOrEmpty(cap.MonitorName)
            ? cap.MonitorName
            : (monitorIndex is int mi ? _capture.GetMonitorName(mi) : null);
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
                var bmp = cap.Image;
                var quality = settings.ResolveMonitorQuality(monitorIndex);
                var scale = ResolveFrameScale(settings, captureType, monitorIndex);
                AppLog.Debug("Encode: " + captureType + " " + bmp.Width + "x" + bmp.Height
                             + " scale=" + scale.ToString("F3") + " q=" + quality);

                bmp = ImageEncoder.ApplyScale(bmp, scale);
                bmp = ImageEncoder.ClampMaxEdge(bmp, maxEdge);
                // Because keep ownership for dispose if ApplyScale returned the original bitmap.
                cap.Image = bmp;

                width = bmp.Width;
                height = bmp.Height;
                fingerprint = ComputeFingerprint(bmp);
                if (allowSkip && ShouldDiscardFrame(settings, fingerprint, fpKey, out var skipWhy))
                {
                    AppLog.Info("Capture skip: " + skipWhy + " (" + captureType + " " + width + "x" + height + ")");
                    return new EnqueueOutcome(false, true, null);
                }

                encoded = ImageEncoder.Encode(bmp, formatInfo.Effective, quality);
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
            Fingerprint = fingerprint == null ? null : FingerprintMetadata.ToDto(fingerprint),
            JournalEntryId = journalId is { } jid && jid != Uuid7.Empty ? jid : (Uuid7?)null
        };
        var fileName = BuildFileName(captureType, monitorIndex, formatInfo.Extension);

        try
        {
            var id = _spool.Enqueue(encoded, fileName, formatInfo.ContentType, meta);
            if (fingerprint != null)
            {
                _lastKept[fpKey] = fingerprint;
            }

            AppLog.Info("Spool: enqueued " + fileName + " id=" + id);
            return new EnqueueOutcome(true, false, null);
        }
        catch (Exception ex)
        {
            AppLog.Error("Spool: enqueue failed " + fileName + ": " + ex.Message);
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
            reason = "idle (" + dirty + " dirty cells, max " + settings.IdleMaxDirtyCells + ")";
            return true;
        }

        reason = "";
        return false;
    }

    private static string FingerprintKey(string captureType, int? monitorIndex, string? monitorName)
        => captureType + ":" + (monitorIndex?.ToString() ?? "") + ":" + (monitorName ?? "");

    private static FrameFingerprint ComputeFingerprint(Bitmap bmp)
    {
        var source = bmp;
        var own = false;
        if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
        {
            source = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppArgb);
            own = true;
        }

        var data = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var buf = new byte[stride * source.Height];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            return FrameFingerprint.FromPixels(
                source.Width, source.Height, buf, stride, PixelLayout.Bgra32);
        }
        finally
        {
            source.UnlockBits(data);
            if (own)
            {
                source.Dispose();
            }
        }
    }

    /// <summary>
    /// Resolves the post-capture scale factor for a frame type and optional monitor index.
    /// </summary>
    /// <param name="settings">Client settings with per-monitor / desktop scale preferences.</param>
    /// <param name="captureType">Window, Monitor, or Full.</param>
    /// <param name="monitorIndex">Monitor index for Window/Monitor frames when known.</param>
    /// <returns>Scale factor ≤ 1.0 (typically user-selected steps).</returns>
    private double ResolveFrameScale(ClientSettings settings, string captureType, int? monitorIndex)
    {
        if (captureType == "Monitor" || captureType == "Window")
        {
            if (monitorIndex is int mi)
            {
                double detected;
                try { detected = _capture.GetMonitorDetectedScale(mi); }
                catch { detected = 1.0; }
                return settings.ResolveMonitorScale(mi, detected);
            }
            return 1.0;
        }

        if (captureType == "Full")
        {
            double autoMin;
            try { autoMin = _capture.GetDesktopAutoScale(); }
            catch { autoMin = 1.0; }
            return settings.ResolveDesktopScale(autoMin);
        }

        return 1.0;
    }

    /// <summary>
    /// Ensures login token and machine id are present before capturing.
    /// </summary>
    /// <param name="settings">Loaded settings (machine id).</param>
    /// <returns><c>false</c> when capture should abort (status already set).</returns>
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
    /// Returns true when the timer should skip this tick because screensaver/lock is active.
    /// </summary>
    /// <param name="settings">Settings including <c>SkipWhenScreensaverActive</c>.</param>
    /// <param name="ct">Cancellation for the detector call.</param>
    /// <returns><c>true</c> to skip capture this tick.</returns>
    private async Task<bool> ShouldSkipForScreensaverAsync(
        ClientSettings settings,
        CancellationToken ct,
        bool reportStatus = true)
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

            AppLog.Debug("Screensaver/lock active; skipping capture");
            if (reportStatus)
            {
                SetStatus(TrayStatusLevel.Info, "Screensaver",
                    "Skipped capture while screensaver or lock screen is active.");
            }

            return true;
        }
        catch
        {
            // Because detector failures should not block capture permanently.
            return false;
        }
    }

    /// <summary>
    /// Returns true when the timer should skip this tick because the user has been idle
    /// longer than <see cref="ClientSettings.IdlePauseMinutes"/>.
    /// </summary>
    /// <param name="settings">Settings including idle pause minutes (0 = disabled).</param>
    /// <param name="ct">Cancellation for the detector call.</param>
    /// <returns><c>true</c> to skip capture this tick.</returns>
    private async Task<bool> ShouldSkipForIdleAsync(
        ClientSettings settings,
        CancellationToken ct,
        bool reportStatus = true)
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

            AppLog.Debug("User idle " + idle.Value.TotalMinutes.ToString("F1") + "m >= " + minutes + "m; skipping capture");
            if (reportStatus)
            {
                SetStatus(TrayStatusLevel.Info, "Idle",
                    "Skipped capture: no user activity for " + minutes + " minute(s).");
            }

            return true;
        }
        catch
        {
            // Because detector failures should not block capture permanently.
            return false;
        }
    }

    /// <summary>
    /// Normalizes settings flags into a concrete capture plan (targets + monitor subset).
    /// </summary>
    /// <param name="settings">Client capture target settings.</param>
    /// <param name="captureWindow">Set when active window capture is enabled.</param>
    /// <param name="captureMonitors">Set when per-monitor capture is enabled.</param>
    /// <param name="captureFull">Set when full-desktop capture is enabled.</param>
    /// <param name="monitorIndexes">Null/empty means all monitors when capturing monitors.</param>
    /// <param name="error">Human error when no targets are enabled.</param>
    /// <returns><c>false</c> when nothing would be captured.</returns>
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
            monitorIndexes = indexes.Count == 0 ? null : indexes;
        }

        return true;
    }

    /// <summary>
    /// Builds a short human description of the capture plan for logs and status.
    /// </summary>
    /// <param name="window">Include active window.</param>
    /// <param name="monitors">Include per-monitor captures.</param>
    /// <param name="full">Include full desktop.</param>
    /// <param name="indexes">Optional monitor subset when capturing monitors.</param>
    /// <returns>Joined plan string such as <c>window + all monitors</c>.</returns>
    private static string DescribePlan(bool window, bool monitors, bool full, IReadOnlyList<int>? indexes)
    {
        var parts = new List<string>();
        if (window)
        {
            parts.Add("window");
        }

        if (monitors)
        {
            parts.Add(indexes == null || indexes.Count == 0
                ? "all monitors"
                : "monitors [" + string.Join(",", indexes) + "]");
        }

        if (full)
        {
            parts.Add("full desktop");
        }

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Builds a unique spool file name for a frame type and timestamp.
    /// </summary>
    /// <param name="captureType">Window, Monitor, or Full.</param>
    /// <param name="monitorIndex">Monitor index when type is Monitor.</param>
    /// <param name="ext">File extension without dot (e.g. jpg).</param>
    /// <returns>File name suitable for the offline spool.</returns>
    private static string BuildFileName(string captureType, int? monitorIndex, string ext)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        if (captureType == "Monitor" && monitorIndex is int mi)
        {
            return "monitor-" + mi + "-" + stamp + "." + ext;
        }

        if (captureType == "Window")
        {
            return "window-" + stamp + "." + ext;
        }

        return "desktop-" + stamp + "." + ext;
    }

    /// <summary>
    /// Creates an API client from settings and the stored/env token (may be empty for status-only use).
    /// </summary>
    /// <returns>Disposable API client bound to the configured base URL.</returns>
    private SchnappApiClient CreateApi()
    {
        var settings = _store.Load();
        var token = _store.LoadToken()
                    ?? Environment.GetEnvironmentVariable("SCHNAPP_TOKEN")
                    ?? "";
        return new SchnappApiClient(settings.ApiBaseUrl, token);
    }

    /// <summary>
    /// Stores raw status and raises <see cref="StatusChanged"/> (swallows UI handler errors).
    /// </summary>
    /// <param name="level">Severity.</param>
    /// <param name="shortLabel">Brief label.</param>
    /// <param name="detail">Detail text for the dialog.</param>
    private void SetStatus(TrayStatusLevel level, string shortLabel, string detail)
    {
        _lastStatus = new TrayStatus
        {
            Level = level,
            ShortLabel = shortLabel,
            Detail = detail,
            At = DateTimeOffset.Now
        };
        try { StatusChanged?.Invoke(this, _lastStatus); }
        catch { /* ignore UI errors */ }
    }

    /// <summary>
    /// Writes <see cref="ClientSettings.CaptureRunning"/> so next launch restores pause/run.
    /// </summary>
    /// <param name="running">True when capture should auto-start next session.</param>
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
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureService: failed to persist CaptureRunning: " + ex.Message);
        }
    }

    /// <summary>
    /// Writes <see cref="ClientSettings.UploadsPaused"/> so automatic push state survives restart.
    /// </summary>
    /// <param name="paused">True when background uploads should stay paused.</param>
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
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureService: failed to persist UploadsPaused: " + ex.Message);
        }
    }
}
