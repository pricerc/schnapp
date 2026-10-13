namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Windows.Services;

/// <summary>
/// Compact journal + capture control window (Linux <c>MainWindow</c> parity).
/// </summary>
/// <remarks>
/// Layout lives in <c>MainForm.Designer.cs</c>. Journal load/apply/clear is delegated to
/// <see cref="JournalSessionService"/>. Customer / Project / Task / MRU fields use
/// <see cref="ContainsFilterComboBox"/> for substring suggestions against the local
/// reference-data cache.
/// </remarks>
public partial class MainForm : Form
{
    /// <summary>Shared capture/upload service owned by the tray shell.</summary>
    private readonly CaptureService _capture;

    /// <summary>Shared journal session (lists + open entry).</summary>
    private readonly JournalSessionService _journal;

    /// <summary>Loads/saves client settings (window placement, etc.).</summary>
    private readonly ClientSettingsStore _store = new();

    /// <summary>Parallel to <see cref="MruBox"/> display strings for apply-by-text.</summary>
    private IReadOnlyList<JournalSessionService.MruEntry> _mruItems =
        Array.Empty<JournalSessionService.MruEntry>();

    /// <summary>
    /// When true, <see cref="MruBox_SelectionChangeCommitted"/> ignores selection changes
    /// (programmatic MRU list reloads).
    /// </summary>
    private bool _suppressMru;

    /// <summary>
    /// When true, customer/project cascade handlers skip work (programmatic C/P/T reloads).
    /// </summary>
    private bool _suppressCascade;

    /// <summary>
    /// Creates the main window bound to shared capture and journal services.
    /// </summary>
    /// <param name="capture">Running capture/upload service owned by the tray shell.</param>
    /// <param name="journal">Shared journal session (lists + open entry).</param>
    /// <exception cref="ArgumentNullException"><paramref name="capture"/> or <paramref name="journal"/> is null.</exception>
    public MainForm(CaptureService capture, JournalSessionService journal)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        InitializeComponent();

        // Because service notifications are not Form virtuals; marshal to the UI thread.
        _journal.Changed += (_, __) => BeginInvoke(new Action(RefreshUi));
        _capture.StatusChanged += (_, __) => BeginInvoke(new Action(UpdateStatusBar));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Restores window placement and binds from the local journal/reference cache.
    /// Full API refresh is only on the Refresh button (startup and server sync already pull).
    /// </remarks>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var s = _store.Load();
        WindowPlacement.RestoreOrPlaceNearTray(this, s.MainWindow);
        RefreshUi();
    }

    /// <inheritdoc />
    /// <remarks>
    /// User close (X) hides the window; the tray owns the instance for the process lifetime.
    /// </remarks>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Because the tray shell keeps this form alive; UserClosing should only hide, not dispose.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            try
            {
                var s = _store.Load();
                s.MainWindow = WindowPlacement.Capture(this);
                _store.Save(s);
            }
            catch { /* ignore placement save */ }
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// Shows the window (if hidden), activates it, and rebinds from the in-memory cache
    /// (no network). Use the Refresh button for a full reference-data pull.
    /// </summary>
    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Normal;
        Activate();
        // Because lists were filled at startup / last sync; only rebind local state here.
        RefreshUi();
    }

    /// <summary>
    /// Rebinds the active-target banner, C/P/T combos, MRU list, and capture buttons
    /// from <see cref="_journal"/> / <see cref="_capture"/>.
    /// </summary>
    private void RefreshUi()
    {
        TagsBox.SetSuggestions(_capture.Uploads.ReferenceData);

        var display = _journal.CurrentTargetDisplay;
        ActiveTargetText.Text = string.IsNullOrEmpty(display) ? "No active target" : display;
        ActiveTimeText.Text = _journal.OpenJournal == null
            ? "—"
            : "Since " + _journal.OpenJournal.StartUtc.ToLocalTime().ToString("g");

        var customer = CustomerBox.SafeText;
        var project = ProjectBox.SafeText;
        var task = TaskBox.SafeText;

        // Because an open journal wins over typed draft text for C/P/T and notes/tags.
        if (_journal.OpenJournal != null)
        {
            var target = _journal.Targets.FirstOrDefault(t => t.Id == _journal.OpenJournal.JournalTargetId);
            if (target != null)
            {
                var names = _journal.NamesFor(target);
                customer = names.Customer;
                project = names.Project;
                task = names.Task;
            }
            NotesBox.Text = _journal.OpenJournal.Notes ?? "";
            TagsBox.Text = _journal.OpenJournal.Tags ?? "";
        }

        // Because SetCandidates would fire cascade Leave/Committed handlers mid-reload.
        _suppressCascade = true;
        using (CustomerBox.SuspendFiltering())
        using (ProjectBox.SuspendFiltering())
        using (TaskBox.SuspendFiltering())
        {
            try
            {
                // Because customers are the full local cache; projects/tasks are filtered by parent selection.
                CustomerBox.SetCandidates(_journal.Customers.Select(c => c.Name), customer, includeNone: true);
                ProjectBox.SetCandidates(_journal.ProjectsFor(customer).Select(p => p.Name), project, includeNone: true);
                TaskBox.SetCandidates(_journal.TasksFor(customer, project).Select(t => t.Name), task, includeNone: true);
            }
            finally
            {
                _suppressCascade = false;
            }
        }

        FillMruCombo();

        StartCaptureBtn.Enabled = _capture.Paused;
        StopCaptureBtn.Enabled = !_capture.Paused;
        UpdateStatusBar();
    }

    /// <summary>Loads MRU display strings into <see cref="MruBox"/> and keeps <see cref="_mruItems"/> in sync.</summary>
    private void FillMruCombo()
    {
        // Because rebuilding Items would otherwise look like a user MRU pick.
        _suppressMru = true;
        using (MruBox.SuspendFiltering())
        {
            try
            {
                _mruItems = [.. _journal.BuildMru()];
                // Because MRU entries are already ordered/formatted; do not inject "(none)".
                MruBox.SetCandidatesExact([.. _mruItems.Select(m => m.Display)], selected: "");
            }
            finally
            {
                _suppressMru = false;
            }
        }
    }

    /// <summary>Updates the bottom status line from capture tray status + pending upload count.</summary>
    private void UpdateStatusBar()
    {
        var st = _capture.GetStatus();
        StatusBar.Text = st.MenuHeader + "  ·  queue " + _capture.PendingUploadsCount;
    }

    /// <summary>
    /// User-requested full reference-data + open-journal pull from the API.
    /// (Open/show only rebinds the local cache; startup and server sync pull otherwise.)
    /// </summary>
    private async void RefreshButton_Click(object sender, EventArgs e)
    {
        StatusBar.Text = "Refreshing…";
        try
        {
            await _journal.RefreshAsync();
            RefreshUi();
            StatusBar.Text = "Lists refreshed.";
        }
        catch (Exception ex)
        {
            StatusBar.Text = "Refresh failed: " + ex.Message;
        }
    }

    /// <summary>Manual one-shot capture (ignores screensaver skip).</summary>
    private async void SnapshotBtn_Click(object sender, EventArgs e)
    {
        StatusBar.Text = "Capturing…";
        try
        {
            await _capture.CaptureOnceAsync();
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            StatusBar.Text = "Capture failed: " + ex.Message;
        }
    }

    /// <summary>Resumes the capture timer.</summary>
    private void StartCaptureBtn_Click(object sender, EventArgs e)
    {
        _capture.Paused = false;
        _capture.EnsureTimerRunning();
        // Because forceRunning restarts capture even if the service thought it was idle.
        _ = _capture.StartAsync(forceRunning: true);
        RefreshUi();
    }

    /// <summary>Pauses the capture timer.</summary>
    private void StopCaptureBtn_Click(object sender, EventArgs e)
    {
        _capture.Paused = true;
        _capture.Report(TrayStatusLevel.Warning, "Paused", "Capture paused from main window.");
        RefreshUi();
    }

    /// <summary>Starts or switches journal to the typed Customer / Project / Task names.</summary>
    private async void ApplyButton_Click(object sender, EventArgs e)
    {
        try
        {
            var msg = await _journal.ApplyFromNamesAsync(
                NullIfNone(CustomerBox.SafeText),
                NullIfNone(ProjectBox.SafeText),
                NullIfNone(TaskBox.SafeText),
                NotesBox.Text,
                TagsBox.Text);
            StatusBar.Text = msg;
            RefreshUi();
        }
        catch (Exception ex)
        {
            StatusBar.Text = ex.Message;
        }
    }

    /// <summary>Clears the active journal target.</summary>
    private async void ClearButton_Click(object sender, EventArgs e)
    {
        try
        {
            StatusBar.Text = await _journal.ClearAsync();
            RefreshUi();
        }
        catch (Exception ex)
        {
            StatusBar.Text = ex.Message;
        }
    }

    /// <summary>Committed customer pick — clear project/task and rebuild their candidates.</summary>
    private void CustomerBox_SelectionChangeCommitted(object sender, EventArgs e) =>
        CascadeFromCustomer(clearChildren: true);

    /// <summary>Customer focus leave — rebuild project/task candidates without clearing text.</summary>
    private void CustomerBox_Leave(object sender, EventArgs e) =>
        CascadeFromCustomer(clearChildren: false);

    /// <summary>Committed project pick — clear task and rebuild its candidates.</summary>
    private void ProjectBox_SelectionChangeCommitted(object sender, EventArgs e) =>
        CascadeFromProject(clearTask: true);

    /// <summary>Project focus leave — rebuild task candidates without clearing text.</summary>
    private void ProjectBox_Leave(object sender, EventArgs e) =>
        CascadeFromProject(clearTask: false);

    /// <summary>
    /// Rebuilds project/task suggestion lists when the customer changes (Linux MainWindow parity).
    /// </summary>
    /// <param name="clearChildren">When true, blanks project and task (new customer pick).</param>
    private void CascadeFromCustomer(bool clearChildren)
    {
        if (_suppressCascade)
        {
            return;
        }

        // Because SetCandidates on project/task would otherwise re-enter cascade via Leave.
        _suppressCascade = true;
        using (ProjectBox.SuspendFiltering())
        using (TaskBox.SuspendFiltering())
        {
            try
            {
                var customer = CustomerBox.SafeText;
                // Because a committed new customer should wipe children; Leave only refilters.
                var project = clearChildren ? null : ProjectBox.SafeText;
                var task = clearChildren ? null : TaskBox.SafeText;
                ProjectBox.SetCandidates(_journal.ProjectsFor(customer).Select(p => p.Name), project, includeNone: true);
                TaskBox.SetCandidates(_journal.TasksFor(customer, project).Select(t => t.Name), task, includeNone: true);
            }
            finally
            {
                _suppressCascade = false;
            }
        }
    }

    /// <summary>Rebuilds the task suggestion list when the project changes.</summary>
    /// <param name="clearTask">When true, blanks the task field (new project pick).</param>
    private void CascadeFromProject(bool clearTask)
    {
        if (_suppressCascade)
        {
            return;
        }

        _suppressCascade = true;
        using (TaskBox.SuspendFiltering())
        {
            try
            {
                var customer = CustomerBox.SafeText;
                var project = ProjectBox.SafeText;
                var task = clearTask ? null : TaskBox.SafeText;
                TaskBox.SetCandidates(_journal.TasksFor(customer, project).Select(t => t.Name), task, includeNone: true);
            }
            finally
            {
                _suppressCascade = false;
            }
        }
    }

    /// <summary>Applies the selected recent journal target from the MRU list.</summary>
    private async void MruBox_SelectionChangeCommitted(object sender, EventArgs e)
    {
        if (_suppressMru)
        {
            return;
        }

        var mru = ResolveMruSelection();
        if (mru == null)
        {
            return;
        }

        try
        {
            // Because Linux fills C/P/T from the MRU target first, then applies the session.
            var names = _journal.NamesFor(mru.Target);
            _suppressCascade = true;
            using (CustomerBox.SuspendFiltering())
            using (ProjectBox.SuspendFiltering())
            using (TaskBox.SuspendFiltering())
            {
                try
                {
                    CustomerBox.SetCandidates(_journal.Customers.Select(c => c.Name), names.Customer, includeNone: true);
                    ProjectBox.SetCandidates(_journal.ProjectsFor(names.Customer).Select(p => p.Name), names.Project, includeNone: true);
                    TaskBox.SetCandidates(_journal.TasksFor(names.Customer, names.Project).Select(t => t.Name), names.Task, includeNone: true);
                }
                finally
                {
                    _suppressCascade = false;
                }
            }

            StatusBar.Text = await _journal.ApplyMruAsync(mru.Target, NotesBox.Text, TagsBox.Text);
            RefreshUi();
        }
        catch (Exception ex)
        {
            StatusBar.Text = ex.Message;
        }
    }

    /// <summary>
    /// Resolves the MRU entry for the current <see cref="MruBox"/> text
    /// (indices are unreliable while the list is filtered).
    /// </summary>
    /// <returns>Matching MRU entry, or null if text is empty or unknown.</returns>
    private JournalSessionService.MruEntry? ResolveMruSelection()
    {
        var text = MruBox.SafeText.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Because SelectedIndex is wrong while ContainsFilterComboBox has filtered Items.
        return _mruItems.FirstOrDefault(m =>
            string.Equals(m.Display, text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Persists notes on focus leave when a journal is open.</summary>
    private async void NotesBox_Leave(object sender, EventArgs e)
    {
        if (_journal.OpenJournal == null)
        {
            return;
        }

        // Because offline/save races should not surface as unhandled UI exceptions.
        try { await _journal.SaveFieldsAsync(NotesBox.Text, null, saveNotes: true, saveTags: false); }
        catch { /* ignore offline/save races */ }
    }

    /// <summary>Persists tags on focus leave when a journal is open.</summary>
    private async void TagsBox_Leave(object sender, EventArgs e)
    {
        if (_journal.OpenJournal == null)
        {
            return;
        }

        try { await _journal.SaveFieldsAsync(null, TagsBox.Text, saveNotes: false, saveTags: true); }
        catch { /* ignore offline/save races */ }
    }

    /// <summary>
    /// Treats blank or <c>(none)</c> as no selection for apply.
    /// </summary>
    /// <param name="text">Raw combo text.</param>
    /// <returns>Trimmed name, or null when empty / <c>(none)</c>.</returns>
    private static string? NullIfNone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text!.Trim();
        // Because "(none)" is a UI sentinel from ContainsFilterComboBox, not a real target name.
        if (string.Equals(t, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return t;
    }
}
