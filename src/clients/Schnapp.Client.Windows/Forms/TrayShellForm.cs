namespace Schnapp.Client.Windows.Forms;

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Windows.Capture;
using Schnapp.Client.Windows.Services;

/// <summary>
/// Hidden host form owning the tray <see cref="NotifyIcon"/> and root menu (VS designer pattern A).
/// </summary>
/// <remarks>
/// Never shown as a normal window. Lifecycle: construct services, bootstrap login/machine,
/// start capture loop. All dialogs are separate designer forms.
/// </remarks>
public partial class TrayShellForm : Form
{
    /// <summary>Persisted client settings (token, machine, capture options).</summary>
    private readonly ClientSettingsStore _store = new();

    /// <summary>Capture timer / spool / uploads service; created in <see cref="OnLoad"/>.</summary>
    private CaptureService? _capture;

    /// <summary>Journal target session shared with <see cref="MainForm"/>.</summary>
    private JournalSessionService? _journal;

    /// <summary>Singleton main window instance (reused until disposed).</summary>
    private MainForm? _mainForm;

    /// <summary>Singleton offline queue window instance (reused until disposed).</summary>
    private OfflineQueueForm? _offlineQueueForm;

    /// <summary>Tray icon while capture is running and healthy.</summary>
    private Icon? _iconRunning;

    /// <summary>Tray icon while capture is paused.</summary>
    private Icon? _iconPaused;

    /// <summary>Tray icon for stopped/error states.</summary>
    private Icon? _iconStopped;

    /// <summary>
    /// Defers single-click pause/resume until past the system double-click interval,
    /// so a double-click can cancel it and only open the main window.
    /// </summary>
    private Timer? _trayClickTimer;

    /// <summary>
    /// Set by double-click so the trailing <see cref="NotifyIcon.MouseClick"/>
    /// (second mouse-up) does not start another pause/resume timer.
    /// </summary>
    private bool _suppressNextTrayClick;

    /// <summary>
    /// When false, the shell stays off the taskbar / screen after load.
    /// Set true only while debugging if you need to inspect the host form.
    /// </summary>
    private readonly bool _showShellForDebug = Debugger.IsAttached;

    /// <summary>Creates the tray host (not visible to the user in normal runs).</summary>
    public TrayShellForm()
    {
        InitializeComponent();
        // Because components (NotifyIcon, menu) live on this form for the VS designer.
        // The form must still "show" once so Application.Run can create the handle and
        // raise OnLoad — we only hide it from the user (see OnLoad).
        ShowInTaskbar = _showShellForDebug;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        if (!_showShellForDebug)
        {
            Opacity = 0;
            WindowState = FormWindowState.Minimized;
        }
    }

    /// <summary>
    /// Form lifecycle: prefer overriding <see cref="Form.OnLoad"/> over <c>Load +=</c>.
    /// The base call raises the Load event for any external subscribers.
    /// </summary>
    /// <remarks>
    /// Do <b>not</b> suppress the first show via <see cref="SetVisibleCore"/> with a hard
    /// <c>base.SetVisibleCore(false)</c> — WinForms only raises Load when the form is first
    /// made visible, so that pattern silently skips all startup work.
    /// </remarks>
    /// <param name="e">Load event args.</param>
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Because hide the shell after the Load path has started (message pump is running).
        // User-facing UI is NotifyIcon + modal dialogs only.
        if (!_showShellForDebug)
        {
            Hide();
            Opacity = 0;
            ShowInTaskbar = false;
        }

        try
        {
            LoadTrayIcons();
            ApplyStaticMenuIcons();
            var settings = _store.Load();
            AppLog.InitFileFromSettings(settings);

            var gdi = new GdiScreenCapture();
            _capture = new CaptureService(gdi);
            _journal = new JournalSessionService(_capture);
            _capture.ActiveJournalEntryIdProvider = () => _journal.OpenJournal?.Id;
            // Refresh open journal after local midnight so captures use today's day-boundary clone.
            _capture.BeforeCaptureAsync = () => _journal.EnsureCurrentDayAsync();
            _capture.StatusChanged += (_, st) =>
            {
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        // Because StatusChanged may fire off the UI thread.
                        BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (!IsDisposed)
                                {
                                    ApplyStatus(st);
                                }
                            }
                            catch (Exception uiEx)
                            {
                                AppLog.Warn("ApplyStatus UI: " + uiEx.Message);
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("StatusChanged marshal: " + ex.Message);
                }
            };

            // Because journal apply/clear/refresh can change the open target without a capture status event.
            _journal.Changed += (_, __) =>
            {
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (!IsDisposed && _capture != null)
                                {
                                    ApplyStatus(_capture.GetStatus());
                                }
                            }
                            catch (Exception uiEx)
                            {
                                AppLog.Warn("Journal Changed UI: " + uiEx.Message);
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Journal Changed marshal: " + ex.Message);
                }
            };

            TrayMenu.Opening += (_, __) =>
            {
                try { RebuildDynamicMenus(); }
                catch (Exception ex) { AppLog.Warn("Rebuild menus: " + ex.Message); }
            };

            // Bootstrap login / machine
            if (string.IsNullOrEmpty(_store.LoadToken())
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN")))
            {
                using (var login = new LoginForm())
                {
                    login.ShowDialog(this);
                }
            }

            settings = _store.Load();
            if (!settings.MachineId.HasValue
                && (!string.IsNullOrEmpty(_store.LoadToken())
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN"))))
            {
                using (var machines = new MachinesForm())
                {
                    machines.ShowDialog(this);
                }
            }

            // Because await so OpenJournal (and target codes for the tooltip) are loaded before first ApplyStatus.
            await _journal.TryRefreshAsync();
            await _capture.StartAsync(forceRunning: false);
            ApplyStatus(_capture.GetStatus());
            AppLog.Info("Tray shell ready");
        }
        catch (Exception ex)
        {
            AppLog.Error("Tray startup failed", ex);
            MessageBox.Show("Schnapp failed to start: " + ex.Message, "Schnapp",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            // Because defer close: Application.Exit while still inside OnLoad/async void is fragile.
            RequestExit();
        }
    }

    /// <summary>
    /// Ends the process by closing the shell form after the current UI event finishes.
    /// </summary>
    /// <remarks>
    /// Do not call <see cref="Application.Exit"/> directly from a tray menu click — WinForms
    /// is still walking open forms / disposing the <see cref="ContextMenuStrip"/>, which
    /// throws <c>Collection was modified</c> inside <c>Application.ExitInternal</c>.
    /// Closing this form ends <c>Application.Run</c> cleanly.
    /// </remarks>
    private void RequestExit()
    {
        try
        {
            notifyIcon?.Visible = false;
        }
        catch { /* ignore */ }

        void closeShell()
        {
            try
            {
                if (!IsDisposed)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("RequestExit Close: " + ex.Message);
                try { Application.Exit(); } catch { /* last resort */ }
            }
        }

        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(closeShell));
            }
            else
            {
                closeShell();
            }
        }
        catch
        {
            closeShell();
        }
    }

    /// <summary>
    /// Form lifecycle: override <see cref="Form.OnFormClosing"/> (not <c>FormClosing +=</c>).
    /// Disposes capture service, singleton child forms, and tray icons before the base close.
    /// </summary>
    /// <param name="e">Closing event args.</param>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            notifyIcon.Visible = false;
            if (_trayClickTimer != null)
            {
                _trayClickTimer.Stop();
                _trayClickTimer.Dispose();
                _trayClickTimer = null;
            }
            _capture?.Dispose();
            _mainForm?.Dispose();
            _offlineQueueForm?.Dispose();
            _iconRunning?.Dispose();
            _iconPaused?.Dispose();
            _iconStopped?.Dispose();
        }
        catch { /* ignore */ }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// Loads running/paused/stopped tray icons and the application icon from Assets.
    /// </summary>
    private void LoadTrayIcons()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _iconRunning = LoadIcon(Path.Combine(baseDir, "Assets", "tray-running.ico"));
            _iconPaused = LoadIcon(Path.Combine(baseDir, "Assets", "tray-paused.ico"));
            _iconStopped = LoadIcon(Path.Combine(baseDir, "Assets", "tray-stopped.ico"));
            var app = LoadIcon(Path.Combine(baseDir, "Assets", "app-icon.ico"));
            if (app != null)
            {
                Icon = app;
            }

            notifyIcon.Icon = _iconStopped ?? app ?? SystemIcons.Application;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Tray icons: " + ex.Message);
            notifyIcon.Icon = SystemIcons.Application;
        }
    }

    /// <summary>
    /// Loads an <see cref="Icon"/> from disk, or returns null when missing/unloadable.
    /// </summary>
    /// <param name="path">Absolute path to a .ico file.</param>
    /// <returns>New icon instance, or null.</returns>
    private static Icon? LoadIcon(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>
    /// Assigns static PNG icons to designer menu items that do not change with state.
    /// </summary>
    private void ApplyStaticMenuIcons()
    {
        try
        {
            TrayMenu.ImageScalingSize = new Size(16, 16);
            MenuIcons.Apply(StatusItem, "status");
            MenuIcons.Apply(LoginItem, "login");
            MenuIcons.Apply(SettingsItem, "settings");
            MenuIcons.Apply(MachinesItem, "machines");
            MenuIcons.Apply(QuitItem, "quit");
            MenuIcons.Apply(ViewCacheButton, "queue");
            MenuIcons.Apply(UploadsPauseItem, "pause");
            MenuIcons.Apply(PushItem, "push");
            MenuIcons.Apply(NoteItem, "note");
            MenuIcons.Apply(TagsItem, "tags");
            MenuIcons.Apply(CustomerItem, "customer");
            MenuIcons.Apply(ProjectItem, "project");
            MenuIcons.Apply(TaskItem, "task");
            MenuIcons.Apply(OpenMainItem, "open");
            MenuIcons.Apply(JournalTargetItem, "journal");
            MenuIcons.Apply(JournalNoneItem, "none");
            MenuIcons.Apply(PauseCaptureItem, "pause");
            MenuIcons.Apply(CaptureNowItem, "capture");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Menu icons: " + ex.Message);
        }
    }

    /// <summary>
    /// Refreshes status menu text, tray tooltip/icon, pause labels, queue counts, and login label.
    /// </summary>
    /// <param name="status">Status event payload (re-read via <see cref="CaptureService.GetStatus"/> for overlays).</param>
    private void ApplyStatus(TrayStatus status)
    {
        if (_capture == null)
        {
            return;
        }

        // Because GetStatus applies pause/spool overlays on top of the last raw report.
        var st = _capture.GetStatus();
        StatusItem.Text = st.MenuHeader;
        notifyIcon.Text = BuildTrayTooltip(st);

        // running / paused / stopped icons on every status path
        if (_capture.Paused)
        {
            notifyIcon.Icon = _iconPaused ?? notifyIcon.Icon;
        }
        else if (st.Level == TrayStatusLevel.Error)
        {
            notifyIcon.Icon = _iconStopped ?? notifyIcon.Icon;
        }
        else
        {
            notifyIcon.Icon = _iconRunning ?? notifyIcon.Icon;
        }

        PauseCaptureItem.Text = _capture.Paused ? "Resume capture" : "Pause capture";
        MenuIcons.Apply(PauseCaptureItem, _capture.Paused ? "play" : "pause");

        UpdateQueueMenuItem();
        UpdateUploadsPauseMenuItem();

        var loggedIn = !string.IsNullOrEmpty(_store.LoadToken())
                       || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN"));
        LoginItem.Text = loggedIn ? "Logout" : "Login…";
    }

    /// <summary>
    /// Tray icon hover text: status plus active journal target <em>codes</em> (when set).
    /// WinForms <see cref="NotifyIcon.Text"/> is limited to 63 characters.
    /// </summary>
    /// <param name="st">Current tray status for the base tooltip label.</param>
    /// <returns>Truncated tooltip string safe for <see cref="NotifyIcon.Text"/>.</returns>
    private string BuildTrayTooltip(TrayStatus st)
    {
        var tip = st.Tooltip ?? "Schnapp";
        // Because prefer short list codes (fit the 63-char NotifyIcon limit better than names).
        var journal = _journal?.CurrentTargetCodes;
        if (!string.IsNullOrWhiteSpace(journal))
        {
            tip = tip + " · " + journal!.Trim();
        }

        // Because NotifyIcon.Text throws if longer than 63 on classic WinForms.
        if (tip.Length > 63)
        {
            tip = tip.Substring(0, 63);
        }

        return tip;
    }

    /// <summary>
    /// Enables the recent-screenshots menu item whenever there are pending (not-yet-uploaded)
    /// or cached (already-uploaded, kept locally) items, and shows the total count.
    /// </summary>
    private void UpdateQueueMenuItem()
    {
        if (_capture == null)
        {
            return;
        }

        var spoolStatus = _capture.Spool.GetStatus();
        var total = spoolStatus.PendingCount + spoolStatus.UploadedCount;
        var pendingSuffix = spoolStatus.PendingCount > 0
            ? $" , {spoolStatus.PendingCount} pending"
            : "";
        ViewCacheButton.Text = total == 0
            ? "View ..."
            : $"View ({total}{pendingSuffix})…";
        ViewCacheButton.Enabled = total > 0;
    }

    /// <summary>
    /// Shows pause/resume state plus the total pending upload count (mutations + screenshots)
    /// awaiting automatic push.
    /// </summary>
    private void UpdateUploadsPauseMenuItem()
    {
        if (_capture == null)
        {
            return;
        }

        var pending = _capture.PendingUploadsCount;
        var suffix = pending > 0 ? " (" + pending + " pending)" : "";
        if (_capture.UploadsPaused)
        {
            UploadsPauseItem.Text = "Resume uploads" + suffix;
            MenuIcons.Apply(UploadsPauseItem, "play");
        }
        else
        {
            UploadsPauseItem.Text = "Pause uploads" + suffix;
            MenuIcons.Apply(UploadsPauseItem, "pause");
        }
    }

    /// <summary>
    /// Rebuilds customer/project/task flyouts and the journal-target MRU when the tray menu opens.
    /// </summary>
    private void RebuildDynamicMenus()
    {
        if (_capture == null || _journal == null)
        {
            return;
        }

        ApplyStatus(_capture.GetStatus());

        // Because scope project/task pickers to the open journal's customer/project (Linux JournalQuickMenu parity).
        // Blank project is valid — TasksFor treats null/"(none)" as "tasks with no project".
        var sel = GetOpenTargetSelection();

        RebuildListMenu(
            CustomerItem, "Customer",
            _journal.Customers.Select(c => c.Name),
            sel.CustomerName, sel.CustomerCode,
            OnCustomerPicked, OnCustomerAdd);
        RebuildListMenu(
            ProjectItem, "Project",
            _journal.ProjectsFor(sel.CustomerName).Select(p => p.Name),
            sel.ProjectName, sel.ProjectCode,
            OnProjectPicked, OnProjectAdd);
        RebuildListMenu(
            TaskItem, "Task",
            _journal.TasksFor(sel.CustomerName, sel.ProjectName).Select(t => t.Name),
            sel.TaskName, sel.TaskCode,
            OnTaskPicked, OnTaskAdd);

        // MRU under Journal target — keep the first designer item (None), replace the rest.
        while (JournalTargetItem.DropDownItems.Count > 1)
        {
            JournalTargetItem.DropDownItems.RemoveAt(JournalTargetItem.DropDownItems.Count - 1);
        }

        foreach (var mru in _journal.BuildMru())
        {
            var item = new ToolStripMenuItem(mru.Display) { Tag = mru };
            MenuIcons.Apply(item, "mru");
            item.Click += async (_, __) =>
            {
                try
                {
                    var msg = await _journal.ApplyMruAsync(mru.Target, null, null);
                    _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
                }
                catch (Exception ex)
                {
                    _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message);
                }
            };
            JournalTargetItem.DropDownItems.Add(item);
        }
    }

    /// <summary>
    /// Open-journal customer/project/task names and codes (null when unset / no open journal).
    /// </summary>
    /// <returns>
    /// Six-tuple of names and codes used to label list menus and pre-select cascade apply.
    /// </returns>
    private (
        string? CustomerName, string? ProjectName, string? TaskName,
        string? CustomerCode, string? ProjectCode, string? TaskCode)
        GetOpenTargetSelection()
    {
        if (_journal?.OpenJournal == null)
        {
            return (null, null, null, null, null, null);
        }

        var target = _journal.Targets.FirstOrDefault(x => x.Id == _journal.OpenJournal.JournalTargetId);
        if (target == null)
        {
            return (null, null, null, null, null, null);
        }

        var names = _journal.NamesFor(target);
        var codes = _journal.CodesFor(target);
        return (
            string.IsNullOrWhiteSpace(names.Customer) ? null : names.Customer,
            string.IsNullOrWhiteSpace(names.Project) ? null : names.Project,
            string.IsNullOrWhiteSpace(names.Task) ? null : names.Task,
            string.IsNullOrWhiteSpace(codes.Customer) ? null : codes.Customer.Trim(),
            string.IsNullOrWhiteSpace(codes.Project) ? null : codes.Project.Trim(),
            string.IsNullOrWhiteSpace(codes.Task) ? null : codes.Task.Trim());
    }

    /// <summary>
    /// Rebuilds a Customer/Project/Task flyout: root label gets <c>Label (CODE)</c>,
    /// and the current selection is checked in the dropdown.
    /// </summary>
    /// <param name="root">Designer menu item that owns the dropdown.</param>
    /// <param name="label">Base label (Customer/Project/Task).</param>
    /// <param name="names">Candidate list item names for this scope.</param>
    /// <param name="selectedName">Currently selected name, or null for (none).</param>
    /// <param name="selectedCode">Code shown in the root label when set.</param>
    /// <param name="pickHandler">Click handler for (none) and named items.</param>
    /// <param name="addHandler">Click handler for “Add new…”.</param>
    private static void RebuildListMenu(
        ToolStripMenuItem root,
        string label,
        System.Collections.Generic.IEnumerable<string> names,
        string? selectedName,
        string? selectedCode,
        EventHandler pickHandler,
        EventHandler addHandler)
    {
        root.Text = string.IsNullOrWhiteSpace(selectedCode)
            ? label
            : label + " (" + selectedCode!.Trim() + ")";

        root.DropDownItems.Clear();

        var noneSelected = string.IsNullOrWhiteSpace(selectedName);
        var none = new ToolStripMenuItem("(none)")
        {
            Tag = "",
            Checked = noneSelected
        };
        MenuIcons.Apply(none, "none");
        none.Click += pickHandler;
        root.DropDownItems.Add(none);

        var add = new ToolStripMenuItem("Add new…");
        MenuIcons.Apply(add, "add");
        add.Click += addHandler;
        root.DropDownItems.Add(add);
        root.DropDownItems.Add(new ToolStripSeparator());

        // Because cap at 40 sorted names so the tray flyout stays usable on large lists.
        foreach (var n in names.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).Take(40))
        {
            var isSelected = !noneSelected
                && string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase);
            var item = new ToolStripMenuItem(n)
            {
                Tag = n,
                Checked = isSelected
            };
            item.Click += pickHandler;
            root.DropDownItems.Add(item);
        }
    }

    /// <summary>
    /// Applies a customer from the tray list (clears project/task cascade).
    /// </summary>
    /// <param name="sender">Menu item whose <c>Tag</c> holds the name (empty for none).</param>
    /// <param name="e">Event args.</param>
    private async void OnCustomerPicked(object? sender, EventArgs e)
    {
        if (_journal == null || _capture == null)
        {
            return;
        }

        var name = (sender as ToolStripMenuItem)?.Tag as string;
        try
        {
            // Because changing customer clears project/task (cascade).
            var msg = await _journal.ApplyFromNamesAsync(
                string.IsNullOrEmpty(name) ? null : name, null, null, null, null);
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    /// <summary>
    /// Applies a project under the current customer (clears task; blank project allowed).
    /// </summary>
    /// <param name="sender">Menu item whose <c>Tag</c> holds the name.</param>
    /// <param name="e">Event args.</param>
    private async void OnProjectPicked(object? sender, EventArgs e)
    {
        if (_journal == null || _capture == null)
        {
            return;
        }

        var name = (sender as ToolStripMenuItem)?.Tag as string;
        try
        {
            var sel = GetOpenTargetSelection();
            // Because changing project clears task; blank project is allowed.
            var msg = await _journal.ApplyFromNamesAsync(
                sel.CustomerName, string.IsNullOrEmpty(name) ? null : name, null, null, null);
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    /// <summary>
    /// Applies a task under the current customer/project selection.
    /// </summary>
    /// <param name="sender">Menu item whose <c>Tag</c> holds the name.</param>
    /// <param name="e">Event args.</param>
    private async void OnTaskPicked(object? sender, EventArgs e)
    {
        if (_journal == null || _capture == null)
        {
            return;
        }

        var name = (sender as ToolStripMenuItem)?.Tag as string;
        try
        {
            var sel = GetOpenTargetSelection();
            var msg = await _journal.ApplyFromNamesAsync(
                sel.CustomerName, sel.ProjectName, string.IsNullOrEmpty(name) ? null : name, null, null);
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    /// <summary>
    /// Prompts for a new customer name and starts a journal on that selection.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void OnCustomerAdd(object? sender, EventArgs e)
    {
        var name = TextPromptForm.Prompt(null, "New customer", "Customer name:");
        if (name == null || _journal == null || _capture == null)
        {
            return;
        }

        try
        {
            var msg = await _journal.ApplyFromNamesAsync(name, null, null, null, null);
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    /// <summary>
    /// Prompts for a new project name under the current customer and starts a journal.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void OnProjectAdd(object? sender, EventArgs e)
    {
        var name = TextPromptForm.Prompt(null, "New project", "Project name:");
        if (name == null || _journal == null || _capture == null)
        {
            return;
        }

        try
        {
            var sel = GetOpenTargetSelection();
            var msg = await _journal.ApplyFromNamesAsync(sel.CustomerName, name, null, null, null);
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    /// <summary>
    /// Prompts for a new task name under the current customer/project and starts a journal.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void OnTaskAdd(object? sender, EventArgs e)
    {
        var name = TextPromptForm.Prompt(null, "New task", "Task name:");
        if (name == null || _journal == null || _capture == null)
        {
            return;
        }

        try
        {
            var sel = GetOpenTargetSelection();
            var msg = await _journal.ApplyFromNamesAsync(sel.CustomerName, sel.ProjectName, name, null, null);
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    #region Static menu handlers

    /// <summary>Opens the status detail dialog for the current tray status.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void StatusItem_Click(object sender, EventArgs e)
    {
        if (_capture == null)
        {
            return;
        }

        using (var f = new StatusDetailForm(_capture.GetStatus()))
        {
            f.ShowDialog();
        }
    }

    /// <summary>
    /// Logs out (clears token) when signed in, otherwise shows the login form.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void LoginItem_Click(object sender, EventArgs e)
    {
        var loggedIn = !string.IsNullOrEmpty(_store.LoadToken())
                       || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCHNAPP_TOKEN"));
        if (loggedIn)
        {
            _store.ClearToken();
            _capture?.Report(TrayStatusLevel.Warning, "Logged out", "Token cleared. Login again to capture.");
            return;
        }

        using (var login = new LoginForm())
        {
            login.ShowDialog();
        }

        ApplyStatus(_capture?.GetStatus() ?? new TrayStatus());
    }

    /// <summary>Opens Settings and reapplies capture settings when saved.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void SettingsItem_Click(object sender, EventArgs e)
    {
        if (_capture == null)
        {
            return;
        }

        using (var f = new SettingsForm(_capture.CaptureBackend))
        {
            if (f.ShowDialog() == DialogResult.OK)
            {
                _capture.ApplySettings(_store.Load());
                _capture.Report(TrayStatusLevel.Ok, "Settings", "Settings saved.");
            }
        }
    }

    /// <summary>Opens the machines dialog and refreshes tray status afterward.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void MachinesItem_Click(object sender, EventArgs e)
    {
        using (var f = new MachinesForm())
        {
            f.ShowDialog();
        }

        ApplyStatus(_capture?.GetStatus() ?? new TrayStatus());
    }

    /// <summary>Requests a clean process exit via <see cref="RequestExit"/>.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void QuitItem_Click(object sender, EventArgs e)
    {
        AppLog.Info("Quit requested from tray");
        RequestExit();
    }

    /// <summary>
    /// Shows or activates the singleton offline queue window when any spool items exist.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void ViewCache_Click(object sender, EventArgs e)
    {
        if (_capture == null)
        {
            return;
        }

        var spoolStatus = _capture.Spool.GetStatus();
        if (spoolStatus.PendingCount + spoolStatus.UploadedCount == 0)
        {
            return;
        }

        if (_offlineQueueForm == null || _offlineQueueForm.IsDisposed)
        {
            _offlineQueueForm = new OfflineQueueForm(_capture, _journal);
        }

        _offlineQueueForm.ShowAndActivate();
    }

    /// <summary>Toggles automatic background upload pause/resume.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void UploadsPauseItem_Click(object sender, EventArgs e)
    {
        if (_capture == null)
        {
            return;
        }

        _capture.UploadsPaused = !_capture.UploadsPaused;
        ApplyStatus(_capture.GetStatus());
    }

    /// <summary>
    /// Pauses capture or resumes it (ensuring the timer loop is running after a paused startup).
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void PauseCaptureItem_Click(object sender, EventArgs e)
    {
        PauseResume();
    }

    private void PauseResume()
    {
        if (_capture == null)
        {
            return;
        }

        if (_capture.Paused)
        {
            _capture.Paused = false;
            _capture.EnsureTimerRunning();
            _ = _capture.StartAsync(forceRunning: true);
        }
        else
        {
            _capture.Paused = true;
            _capture.Report(TrayStatusLevel.Warning, "Paused", "Capture paused from tray.");
        }
        ApplyStatus(_capture.GetStatus());
    }

    /// <summary>Opens the main journal/capture window.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private void OpenMainItem_Click(object sender, EventArgs e) => OpenMain();

    /// <summary>
    /// Left single-click pauses/resumes capture. Deferred so a double-click can cancel it.
    /// Right-click still shows the context menu (handled by <see cref="NotifyIcon"/>).
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Mouse event with button.</param>
    private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Double-click raises MouseClick (1st), MouseDoubleClick, then MouseClick again (2nd up).
        if (_suppressNextTrayClick)
        {
            _suppressNextTrayClick = false;
            return;
        }

        if (_trayClickTimer == null)
        {
            _trayClickTimer = new Timer();
            _trayClickTimer.Tick += (_, __) =>
            {
                _trayClickTimer!.Stop();
                PauseResume();
            };
        }

        _trayClickTimer.Interval = SystemInformation.DoubleClickTime;
        _trayClickTimer.Stop();
        _trayClickTimer.Start();
    }

    /// <summary>
    /// Left double-click opens the main window and cancels any pending single-click pause/resume.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Mouse event with button.</param>
    private void NotifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _trayClickTimer?.Stop();
        _suppressNextTrayClick = true;
        OpenMain();
    }

    /// <summary>
    /// Shows or activates the singleton <see cref="MainForm"/> bound to capture/journal services.
    /// </summary>
    private void OpenMain()
    {
        if (_capture == null || _journal == null)
        {
            return;
        }

        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_capture, _journal);
        }

        _mainForm.ShowAndActivate();
    }

    /// <summary>Prompts to edit notes on the open journal entry.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void NoteItem_Click(object sender, EventArgs e)
    {
        if (_journal == null || _capture == null)
        {
            return;
        }

        var current = _journal.OpenJournal?.Notes ?? "";
        var notes = TextPromptForm.Prompt(null, "Journal notes", "Notes:", current, multiline: true);
        if (notes == null)
        {
            return;
        }

        try
        {
            var msg = await _journal.SaveFieldsAsync(notes, null, saveNotes: true, saveTags: false);
            _capture.Report(TrayStatusLevel.Ok, "Notes", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Notes", ex.Message); }
    }

    /// <summary>Prompts to edit tags on the open journal entry.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void TagsItem_Click(object sender, EventArgs e)
    {
        if (_journal == null || _capture == null)
        {
            return;
        }

        var current = _journal.OpenJournal?.Tags ?? "";
        var tags = TextPromptForm.Prompt(null, "Journal tags", "Tags:", current, referenceData: _capture.Uploads.ReferenceData);
        if (tags == null)
        {
            return;
        }

        try
        {
            var msg = await _journal.SaveFieldsAsync(null, tags, saveNotes: false, saveTags: true);
            _capture.Report(TrayStatusLevel.Ok, "Tags", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Tags", ex.Message); }
    }

    /// <summary>Clears/stops the open journal (None under Journal target).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void JournalNoneItem_Click(object sender, EventArgs e)
    {
        if (_journal == null || _capture == null)
        {
            return;
        }

        try
        {
            var msg = await _journal.ClearAsync();
            _capture.Report(TrayStatusLevel.Ok, "Journal", msg);
        }
        catch (Exception ex) { _capture.Report(TrayStatusLevel.Error, "Journal", ex.Message); }
    }

    /// <summary>Triggers an immediate capture (ignores screensaver skip).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void CaptureNowItem_Click(object sender, EventArgs e)
    {
        await CaptureImages();
    }

    private async Task CaptureImages()
    {
        if (_capture == null)
        {
            return;
        }
        try
        {
            await _capture.CaptureOnceAsync();
            ApplyStatus(_capture.GetStatus());
        }
        catch (Exception ex)
        {
            _capture.Report(TrayStatusLevel.Error, "Capture failed", ex.Message);
        }
    }

    /// <summary>Manually pushes the mutation queue and screenshot spool.</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Event args.</param>
    private async void PushItem_Click(object sender, EventArgs e)
    {
        if (_capture == null)
        {
            return;
        }

        await _capture.PushSpoolAsync();
        ApplyStatus(_capture.GetStatus());
    }

    #endregion

}
