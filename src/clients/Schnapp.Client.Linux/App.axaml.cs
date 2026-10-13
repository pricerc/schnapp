namespace Schnapp.Client.Linux;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Linux.Capture;
using Schnapp.Client.Linux.Services;
using Schnapp.Client.Linux.Views;

using System;
using System.Threading.Tasks;

/// <summary>
/// Avalonia application: system tray + capture timer + Core API/spool.
/// </summary>
/// <remarks>
/// On startup, prompts for Login if no token, then Machines if no default machine.
/// Tray structure is declared in App.axaml; dynamic headers/submenu fill in code-behind.
/// Double-click icon opens main window when Clicked fires.
/// </remarks>
public partial class App : Application
{
    private CaptureService? _capture;
    private JournalSessionService? _journal;
    private JournalQuickMenu? _quickMenu;
    private MainWindow? _mainWindow;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem;
    private NativeMenuItem? _pauseItem;
    private NativeMenuItem? _loginItem;
    private NativeMenuItem? _queueItem;
    private NativeMenuItem? _uploadsPauseItem;
    private NativeMenuItem? _journalTargetItem;
    private NativeMenuItem? _currentTargetItem;
    private WindowIcon? _iconRunning;
    private WindowIcon? _iconPaused;
    private WindowIcon? _iconStopped;
    private TrayStatus _currentStatus = new()
    {
        Level = TrayStatusLevel.Info,
        ShortLabel = "Starting…",
        Detail = "Application is starting."
    };
    private readonly ClientSettingsStore _store = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ResolveTrayFromXaml();
        ApplyStaticMenuIcons();
    }

    /// <summary>
    /// Resolves tray/menu controls from App.axaml (TrayIcon/NativeMenuItem have no x:Name support).
    /// </summary>
    private void ResolveTrayFromXaml()
    {
        var icons = TrayIcon.GetIcons(this);
        _tray = icons is { Count: > 0 } ? icons[0] : null;
        if (_tray?.Menu is not { } menu)
        {
            return;
        }

        // Match by initial XAML headers (dynamic items keep the same instance when Header changes).
        foreach (var item in menu.Items)
        {
            if (item is not NativeMenuItem nmi || nmi.Header is not string header)
            {
                continue;
            }

            if (header.StartsWith('⚪') || header == "Starting…")
            {
                _statusItem = nmi;
            }
            else if (header == "Journal target")
            {
                _journalTargetItem = nmi;
            }
            else if (header is "Pause capture" or "Resume capture")
            {
                _pauseItem = nmi;
            }
            else if (header.StartsWith("Recent screenshots", StringComparison.Ordinal))
            {
                _queueItem = nmi;
            }
            else if (header is "Pause uploads" or "Resume uploads"
                     || header.StartsWith("Pause uploads", StringComparison.Ordinal)
                     || header.StartsWith("Resume uploads", StringComparison.Ordinal))
            {
                _uploadsPauseItem = nmi;
            }
            else if (header.StartsWith("Journal:", StringComparison.Ordinal))
            {
                _currentTargetItem = nmi;
            }
            else if (header.StartsWith("Login", StringComparison.Ordinal)
                     || header.StartsWith("Logout", StringComparison.Ordinal))
            {
                _loginItem = nmi;
            }
        }
    }

    /// <summary>Assigns Assets/menu icons to static tray menu items.</summary>
    private void ApplyStaticMenuIcons()
    {
        if (_tray?.Menu is not { } menu)
        {
            return;
        }

        MenuIcons.Apply(_statusItem, "status");
        MenuIcons.Apply(_loginItem, "login");
        MenuIcons.Apply(_queueItem, "queue");
        MenuIcons.Apply(_pauseItem, "pause");
        MenuIcons.Apply(_journalTargetItem, "journal");

        foreach (var item in menu.Items)
        {
            if (item is not NativeMenuItem nmi || nmi.Header is not string h)
            {
                continue;
            }

            if (h.StartsWith("Settings", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "settings");
            }
            else if (h.StartsWith("Machines", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "machines");
            }
            else if (h == "Quit")
            {
                MenuIcons.Apply(nmi, "quit");
            }
            else if (h.StartsWith("Push", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "push");
            }
            else if (h.StartsWith("Open Main", StringComparison.Ordinal) || h.StartsWith("Open…", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "open");
            }
            else if (h.StartsWith("Capture now", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "capture");
            }
            else if (h.StartsWith("Note", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "note");
            }
            else if (h.StartsWith("Tags", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "tags");
            }
            else if (h.StartsWith("Customer", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "customer");
            }
            else if (h.StartsWith("Project", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "project");
            }
            else if (h.StartsWith("Task", StringComparison.Ordinal))
            {
                MenuIcons.Apply(nmi, "task");
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = _store.Load();
            AppLog.InitFileFromSettings(settings);
            AppLog.Info("App starting");
            // Prefer Xinerama for multi-monitor geometry; Avalonia often reports 1 screen in tray mode.
            var screenInfo = new X11ScreenInfoProvider(new AvaloniaScreenInfoProvider());
            _capture = new CaptureService(new CompositeScreenCapture(screenInfo));
            _journal = new JournalSessionService(_capture);
            // Capture attaches the open journal id onto every screenshot (spool + upload).
            _capture.ActiveJournalEntryIdProvider = () => _journal?.OpenJournal?.Id;
            // Refresh open journal after local midnight so captures use today's day-boundary clone.
            _capture.BeforeCaptureAsync = () => _journal is null
                ? Task.CompletedTask
                : _journal.EnsureCurrentDayAsync();
            _quickMenu = new JournalQuickMenu(
                _journal,
                GetOwner,
                ShowMainWindow,
                msg =>
                {
                    AppLog.Info("Journal menu: " + msg);
                    _capture?.Report(TrayStatusLevel.Info, "Journal", msg);
                    RefreshStatus();
                });
            if (_tray?.Menu != null && _journalTargetItem != null)
            {
                _quickMenu.Attach(_tray.Menu, _journalTargetItem);
            }

            LoadTrayIcons();
            WireTrayRuntime(desktop);
            Dispatcher.UIThread.Post(async () =>
            {
                await EnsureLoggedInAndMachineAsync();
                if (_capture != null)
                {
                    await _capture.StartAsync();
                }

                if (_journal != null)
                {
                    await _journal.TryRefreshAsync();
                }

                RefreshJournalNativeSubmenu();
                RefreshStatus();
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Loads tray ICO assets and applies the initial icon to the XAML tray.
    /// </summary>
    private void LoadTrayIcons()
    {
        _iconRunning = LoadWindowIcon("Assets/tray-running.ico");
        _iconPaused = LoadWindowIcon("Assets/tray-paused.ico");
        _iconStopped = LoadWindowIcon("Assets/tray-stopped.ico");
        // Fallback chain if a file is missing
        _iconRunning ??= _iconStopped ?? _iconPaused;
        _iconPaused ??= _iconRunning;
        _iconStopped ??= _iconRunning;
        _tray?.Icon = _iconStopped;
    }

    /// <summary>
    /// Opens an embedded asset stream and builds a <see cref="WindowIcon"/> for the tray.
    /// </summary>
    /// <param name="relativePath">Path under the project (e.g. Assets/tray-running.ico).</param>
    /// <returns>Icon, or null if the asset cannot be opened.</returns>
    private static WindowIcon? LoadWindowIcon(string relativePath)
    {
        try
        {
            // AssemblyName is schnapp-client-linux  (see csproj)
            var assembly = typeof(App).Assembly;
            var assemblyName = assembly.GetName().Name ?? "schnapp-client-linux";
            var uri = new Uri($"avares://{assemblyName}/{relativePath.Replace('\\', '/')}");
            using var stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Hooks capture/journal events; static menu structure lives in App.axaml.
    /// </summary>
    private void WireTrayRuntime(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _ = desktop;

        _capture?.StatusChanged += (_, status) =>
                Dispatcher.UIThread.Post(() => ApplyStatus(status));

        _journal?.Changed += (_, _) =>
                Dispatcher.UIThread.Post(RefreshJournalNativeSubmenu);

        UpdateLoginMenuHeader();
        UpdatePauseMenuHeader();
        RefreshJournalNativeSubmenu();
        RefreshStatus();
    }

    private async Task EnsureLoggedInAndMachineAsync()
    {
        var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
        if (string.IsNullOrEmpty(token))
        {
            AppLog.Info("Ensure login: no token");
            var login = new LoginWindow();
            var ok = await login.ShowDialog<bool?>(GetOwner());
            if (ok != true)
            {
                _capture?.Report(TrayStatusLevel.Error, "No login",
                    "Login was cancelled. Open Login… from the tray menu to sign in.");
                RefreshStatus();
                return;
            }
            AppLog.Info("Login completed");
        }

        var settings = _store.Load();
        if (!settings.MachineId.HasValue)
        {
            AppLog.Info("Ensure machine: none selected");
            var machines = new MachinesWindow();
            await machines.ShowDialog(GetOwner());
            settings = _store.Load();
            if (!settings.MachineId.HasValue)
            {
                _capture?.Report(TrayStatusLevel.Error, "No machine",
                    "No machine was selected. Open Machines… to register or use one.");
            }
            else
            {
                AppLog.Info($"Machine selected: {settings.MachineId} {settings.MachineName}");
            }
        }
        RefreshStatus();
    }

    private Window GetOwner()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
        {
            return main;
        }

        var hidden = new Window
        {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            Opacity = 0
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = hidden;
        }

        hidden.Show();
        return hidden;
    }

    private async void OnTrayMenuNeedsUpdate(object? sender, EventArgs e)
    {
        if (_journal != null)
        {
            await _journal.TryRefreshAsync();
        }

        RefreshJournalNativeSubmenu();
    }

    private void OnStatusClick(object? sender, EventArgs e) => ShowStatusDetail();

    private void OnOpenClick(object? sender, EventArgs e) => ShowMainWindow();

    private DateTime _lastCaptureNowClickUtc = DateTime.MinValue;
    private readonly TimeSpan _captureNowMinInterval = TimeSpan.FromSeconds(1);

    private async void OnCaptureNowClick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastCaptureNowClickUtc < _captureNowMinInterval)
        {
            return;
        }
        _lastCaptureNowClickUtc = now;

        if (_capture != null)
        {
            await _capture.CaptureOnceAsync();
        }

        RefreshStatus();
    }

    private void OnStopStartClick(object? sender, EventArgs e) => ToggleCapturePause();

    private void ToggleCapturePause()
    {
        if (_capture == null)
        {
            return;
        }

        _capture.Paused = !_capture.Paused;
        if (!_capture.Paused)
        {
            _capture.EnsureTimerRunning();
        }

        if (_capture.Paused)
        {
            _capture.Report(TrayStatusLevel.Warning, "Paused",
                "Capture is paused. Click the tray icon or choose Resume capture to continue.");
        }
        else
        {
            _capture.Report(TrayStatusLevel.Ok, "Running",
                "Capture resumed.");
        }
        RefreshStatus();
    }

    private void OnQueueClick(object? sender, EventArgs e)
    {
        if (_capture == null)
        {
            return;
        }

        var status = _capture.Spool.GetStatus();
        if (status.PendingCount + status.UploadedCount == 0)
        {
            return;
        }

        var w = new OfflineQueueWindow(_capture, _journal);
        w.Closed += (_, _) => RefreshStatus();
        w.Show(GetOwner());
    }

    private void OnUploadsPauseToggleClick(object? sender, EventArgs e)
    {
        _capture?.UploadsPaused = !_capture.UploadsPaused;

        RefreshStatus();
    }

    private async void OnMachinesClick(object? sender, EventArgs e)
    {
        var w = new MachinesWindow();
        await w.ShowDialog(GetOwner());
        RefreshStatus();
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        var w = new SettingsWindow();
        w.Show();
    }

    private async void OnLoginClick(object? sender, EventArgs e) =>
        await OnAccountMenuClickAsync();

    private void OnQuitClick(object? sender, EventArgs e)
    {
        _capture?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void RefreshJournalNativeSubmenu()
    {
        _quickMenu?.RefreshDynamicItems();
        UpdateCurrentTargetMenuItem();
    }

    /// <summary>
    /// Read-only bottom-of-menu line reporting the currently-open journal's target (or "(none)").
    /// </summary>
    private void UpdateCurrentTargetMenuItem()
    {
        if (_currentTargetItem == null)
        {
            return;
        }

        var display = _journal?.CurrentTargetDisplay;
        _currentTargetItem.Header = "Journal: " + (display ?? "(none)");
    }

    private async void OnJournalCustomerNone(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnCustomerNoneAsync();
        }
    }

    private async void OnJournalProjectNone(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnProjectNoneAsync();
        }
    }

    private async void OnJournalTaskNone(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnTaskNoneAsync();
        }
    }

    private async void OnJournalCustomerAdd(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnCustomerAddAsync();
        }
    }

    private async void OnJournalProjectAdd(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnProjectAddAsync();
        }
    }

    private async void OnJournalTaskAdd(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnTaskAddAsync();
        }
    }

    private async void OnJournalNoteClick(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnNoteAsync();
        }
    }

    private async void OnJournalTagsClick(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnTagsAsync();
        }
    }

    private async void OnJournalClearClick(object? sender, EventArgs e)
    {
        if (_quickMenu != null)
        {
            await _quickMenu.OnClearAsync();
        }
    }

    /// <summary>Tray icon activate: toggle capture pause/resume (when the host delivers Clicked).</summary>
    private void OnTrayClicked(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ToggleCapturePause);

    private void UpdatePauseMenuHeader()
    {
        if (_pauseItem == null || _capture == null)
        {
            return;
        }

        if (_capture.Paused)
        {
            _pauseItem.Header = "Resume capture";
            MenuIcons.Apply(_pauseItem, "play");
        }
        else
        {
            _pauseItem.Header = "Pause capture";
            MenuIcons.Apply(_pauseItem, "pause");
        }
    }

    private void ShowStatusDetail()
    {
        var status = _capture?.GetStatus() ?? _currentStatus;
        var w = new StatusDetailWindow(status);
        w.Show(GetOwner());
    }

    /// <summary>
    /// Shows or focuses the main recorder window (single instance; close hides).
    /// </summary>
    private void ShowMainWindow()
    {
        if (_capture == null || _journal == null)
        {
            return;
        }

        _mainWindow ??= new MainWindow(_capture, _journal);

        _mainWindow.ShowFromTray();
    }

    /// <summary>
    /// Single account menu item: Login… when signed out, Logout (identity) when signed in.
    /// </summary>
    private async Task OnAccountMenuClickAsync()
    {
        if (IsLoggedIn())
        {
            _store.ClearToken();
            UpdateLoginMenuHeader();
            _capture?.Report(TrayStatusLevel.Error, "No login",
                "Logged out. Choose Login… from the tray menu to sign in again.");
            RefreshStatus();
            return;
        }

        var login = new LoginWindow();
        await login.ShowDialog(GetOwner());
        UpdateLoginMenuHeader();
        RefreshStatus();
    }

    private bool IsLoggedIn() =>
        !string.IsNullOrEmpty(_store.LoadToken())
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN"));

    private void UpdateLoginMenuHeader()
    {
        if (_loginItem == null)
        {
            return;
        }

        var s = _store.Load();
        if (IsLoggedIn())
        {
            var who = s.LoginIdentity ?? "signed in";
            _loginItem.Header = $"Logout ({who})";
        }
        else
        {
            _loginItem.Header = "Login…";
        }
    }

    private void RefreshStatus()
    {
        if (_capture != null)
        {
            ApplyStatus(_capture.GetStatus());
        }
        else
        {
            ApplyStatus(_currentStatus);
        }
    }

    private void ApplyStatus(TrayStatus status)
    {
        _currentStatus = status;
        _statusItem?.Header = status.MenuHeader;

        if (_tray != null)
        {
            var targetSuffix = _journal?.CurrentTargetDisplay is { } target ? $" · {target}" : "";
            _tray.ToolTipText = status.Tooltip + targetSuffix + " · click: pause/resume";
            _tray.Icon = SelectTrayIcon(status);
        }
        UpdatePauseMenuHeader();
        UpdateQueueMenuItem();
        UpdateUploadsPauseMenuItem();
        UpdateCurrentTargetMenuItem();
    }

    /// <summary>
    /// Enables the recent-screenshots menu item whenever there are pending (not-yet-uploaded)
    /// or cached (already-uploaded, kept locally) items, and shows the total count.
    /// </summary>
    private void UpdateQueueMenuItem()
    {
        if (_queueItem == null)
        {
            return;
        }

        var status = _capture?.Spool.GetStatus();
        var total = (status?.PendingCount ?? 0) + (status?.UploadedCount ?? 0);
        var pendingSuffix = status is { PendingCount: > 0 } ? $", {status.PendingCount} pending" : "";
        _queueItem.Header = total == 0 ? "Recent screenshots (0)" : $"Recent screenshots ({total}{pendingSuffix})…";
        _queueItem.IsEnabled = total > 0;
    }

    /// <summary>
    /// Shows pause/resume state plus the total pending upload count (mutations + screenshots)
    /// awaiting automatic push.
    /// </summary>
    private void UpdateUploadsPauseMenuItem()
    {
        if (_uploadsPauseItem == null || _capture == null)
        {
            return;
        }

        var pending = _capture.PendingUploadsCount;
        var suffix = pending > 0 ? $" ({pending} pending)" : "";
        if (_capture.UploadsPaused)
        {
            _uploadsPauseItem.Header = "Resume uploads" + suffix;
            MenuIcons.Apply(_uploadsPauseItem, "play");
        }
        else
        {
            _uploadsPauseItem.Header = "Pause uploads" + suffix;
            MenuIcons.Apply(_uploadsPauseItem, "pause");
        }
    }

    /// <summary>
    /// Picks running / paused / stopped tray artwork from the current status.
    /// </summary>
    private WindowIcon? SelectTrayIcon(TrayStatus status)
    {
        if (_capture?.Paused == true
            || string.Equals(status.ShortLabel, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            return _iconPaused;
        }

        if (status.Level == TrayStatusLevel.Error
            || string.Equals(status.ShortLabel, "Starting…", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.ShortLabel, "Idle", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.ShortLabel, "No login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.ShortLabel, "No machine", StringComparison.OrdinalIgnoreCase))
        {
            return _iconStopped;
        }

        // OK, Capturing, Queued, Running, etc.
        return _iconRunning;
    }
}
