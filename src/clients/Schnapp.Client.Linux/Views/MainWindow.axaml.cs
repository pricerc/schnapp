namespace Schnapp.Client.Linux.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Linux.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Main recorder window: journal target (MRU or Customer/Project/Task) and capture start/stop.
/// </summary>
public partial class MainWindow : Window
{
    private readonly CaptureService _capture;
    private readonly JournalSessionService _journal;
    private readonly ClientSettingsStore _store = new();
    private IReadOnlyList<JournalSessionService.MruEntry> _mruItems = Array.Empty<JournalSessionService.MruEntry>();
    private DispatcherTimer? _elapsedTimer;
    private bool _suppressMru;
    private bool _suppressCascade;

    public MainWindow() : this(null!, null!)
    {
    }

    public MainWindow(CaptureService capture, JournalSessionService journal)
    {
        _capture = capture;
        _journal = journal;
        InitializeComponent();
        if (capture is null || journal is null)
        {
            return;
        }


        Opened += OnOpened;
        Closing += OnClosing;
        _journal.Changed += OnJournalChanged;
        CustomerBox.SelectionChanged += OnCustomerSelectionChanged;
        ProjectBox.SelectionChanged += OnProjectSelectionChanged;
    }

    private void OnJournalChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible)
            {
                return;
            }


            BindFromSession();
        });
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var settings = _store.Load();
        WindowPlacement.RestoreOrPlaceNearTray(this, settings.MainWindow);
        await RefreshAllAsync();
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateActiveBanner();
        _elapsedTimer.Start();
        UpdateCaptureButtons();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = true;
        try
        {
            var settings = _store.Load();
            settings.MainWindow = WindowPlacement.Capture(this);
            _store.Save(settings);
        }
        catch { /* ignore */ }
        Hide();
    }

    public void ShowFromTray()
    {
        Show();
        Activate();
        _ = RefreshAllAsync();
        UpdateCaptureButtons();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void OnSnapshot(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _capture.CaptureOnceAsync();
            SetStatus("Snapshot requested.");
        }
        catch (Exception ex)
        {
            SetStatus("Snapshot failed: " + ex.Message);
        }
    }

    private void OnStartCapture(object? sender, RoutedEventArgs e)
    {
        _capture.Paused = false;
        _capture.EnsureTimerRunning();
        _capture.Report(TrayStatusLevel.Ok, "Running", "Capture resumed from main window.");
        UpdateCaptureButtons();
        SetStatus("Capture running.");
    }

    private void OnStopCapture(object? sender, RoutedEventArgs e)
    {
        _capture.Paused = true;
        _capture.Report(TrayStatusLevel.Warning, "Paused", "Capture paused from main window.");
        UpdateCaptureButtons();
        SetStatus("Capture paused.");
    }

    private void UpdateCaptureButtons()
    {
        var paused = _capture.Paused;
        StartCaptureBtn.IsEnabled = paused;
        StopCaptureBtn.IsEnabled = !paused;
    }

    private async Task RefreshAllAsync()
    {
        SetStatus("Loading…");
        try
        {
            await _journal.RefreshAsync();
            TagsBox.SetSuggestions(_capture.Uploads.ReferenceData);
            BindFromSession();
            SetStatus("Ready.");
        }
        catch (Exception ex)
        {
            TagsBox.SetSuggestions(_capture.Uploads.ReferenceData);
            BindFromSession();
            SetStatus(FormatError(ex));
        }
    }

    private void BindFromSession()
    {
        _suppressCascade = true;
        try
        {
            var customer = GetComboText(CustomerBox);
            var project = GetComboText(ProjectBox);
            var task = GetComboText(TaskBox);

            var open = _journal.OpenJournal;
            if (open != null)
            {
                NotesBox.Text = open.Notes ?? "";
                TagsBox.Text = open.Tags ?? "";
                var t = _journal.Targets.FirstOrDefault(x => x.Id == open.JournalTargetId);
                if (t != null)
                {
                    var names = _journal.NamesFor(t);
                    customer = names.Customer;
                    project = names.Project;
                    task = names.Task;
                }
            }

            FillCombo(CustomerBox, _journal.Customers.Select(c => c.Name).OrderBy(n => n), customer);
            FillCombo(ProjectBox,
                _journal.ProjectsFor(customer).Select(p => p.Name).OrderBy(n => n),
                project);
            FillCombo(TaskBox,
                _journal.TasksFor(customer, project).Select(t => t.Name).OrderBy(n => n),
                task);

            _suppressMru = true;
            _mruItems = _journal.BuildMru();
            MruBox.ItemsSource = _mruItems.Select(m => m.Display).ToList();
            MruBox.SelectedIndex = -1;
            _suppressMru = false;
        }
        finally
        {
            _suppressCascade = false;
        }

        UpdateActiveBanner();
    }

    private void OnCustomerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCascade)
        {
            return;
        }
        // New customer → always clear project and task (avoid invalid C/P/T triples).
        _suppressCascade = true;
        try
        {
            var customer = GetComboText(CustomerBox);
            ClearCombo(ProjectBox);
            ClearCombo(TaskBox);
            FillCombo(ProjectBox, _journal.ProjectsFor(customer).Select(p => p.Name).OrderBy(n => n), null);
            FillCombo(TaskBox, _journal.TasksFor(customer, null).Select(t => t.Name).OrderBy(n => n), null);
        }
        finally
        {
            _suppressCascade = false;
        }
    }

    private void OnProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCascade)
        {
            return;
        }
        // New project → always clear task.
        _suppressCascade = true;
        try
        {
            var customer = GetComboText(CustomerBox);
            var project = GetComboText(ProjectBox);
            ClearCombo(TaskBox);
            FillCombo(TaskBox, _journal.TasksFor(customer, project).Select(t => t.Name).OrderBy(n => n), null);
        }
        finally
        {
            _suppressCascade = false;
        }
    }

    private void UpdateActiveBanner()
    {
        var open = _journal.OpenJournal;
        if (open == null)
        {
            ActiveTargetText.Text = "No journal target";
            ActiveTimeText.Text = "Clear or Apply a target to start a journal.";
            return;
        }

        var t = _journal.Targets.FirstOrDefault(x => x.Id == open.JournalTargetId);
        ActiveTargetText.Text = t != null ? _journal.FormatTarget(t) : "Journal active";
        var elapsed = DateTimeOffset.UtcNow - open.StartUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }


        ActiveTimeText.Text = $"Started {open.StartUtc.ToLocalTime():t} · {elapsed:hh\\:mm\\:ss}";
    }

    private void OnMruSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressMru || MruBox.SelectedIndex < 0 || MruBox.SelectedIndex >= _mruItems.Count)
        {
            return;
        }


        var item = _mruItems[MruBox.SelectedIndex];
        var names = _journal.NamesFor(item.Target);
        _suppressCascade = true;
        try
        {
            FillCombo(CustomerBox, _journal.Customers.Select(c => c.Name).OrderBy(n => n), names.Customer);
            FillCombo(ProjectBox, _journal.ProjectsFor(names.Customer).Select(p => p.Name).OrderBy(n => n), names.Project);
            FillCombo(TaskBox, _journal.TasksFor(names.Customer, names.Project).Select(t => t.Name).OrderBy(n => n), names.Task);
        }
        finally
        {
            _suppressCascade = false;
        }
        _ = ApplyMruAsync(item);
    }

    private async Task ApplyMruAsync(JournalSessionService.MruEntry item)
    {
        try
        {
            var msg = await _journal.ApplyMruAsync(item.Target, NotesBox.Text, TagsBox.Text);
            BindFromSession();
            SetStatus(msg);
        }
        catch (Exception ex)
        {
            SetStatus(FormatError(ex));
        }
    }

    private async void OnApplyTarget(object? sender, RoutedEventArgs e)
    {
        try
        {
            var msg = await _journal.ApplyFromNamesAsync(
                GetComboText(CustomerBox),
                GetComboText(ProjectBox),
                GetComboText(TaskBox),
                NotesBox.Text,
                TagsBox.Text);
            BindFromSession();
            SetStatus(msg);
        }
        catch (Exception ex)
        {
            SetStatus(FormatError(ex));
        }
    }

    private async void OnClearTarget(object? sender, RoutedEventArgs e)
    {
        try
        {
            var msg = await _journal.ClearAsync();
            NotesBox.Text = "";
            TagsBox.Text = "";
            CustomerBox.SelectedItem = null;
            CustomerBox.Text = "";
            ProjectBox.SelectedItem = null;
            ProjectBox.Text = "";
            TaskBox.SelectedItem = null;
            TaskBox.Text = "";
            MruBox.SelectedIndex = -1;
            UpdateActiveBanner();
            SetStatus(msg);
        }
        catch (Exception ex)
        {
            SetStatus(FormatError(ex));
        }
    }

    private async void OnNotesLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_journal.OpenJournal == null)
        {
            return;
        }


        SetStatus(await _journal.SaveFieldsAsync(NotesBox.Text, TagsBox.Text, saveNotes: true, saveTags: false));
    }

    private async void OnTagsLostFocus(object? sender, EventArgs e)
    {
        if (_journal.OpenJournal == null)
        {
            return;
        }

        var msg = await _journal.SaveFieldsAsync(NotesBox.Text, TagsBox.Text, saveNotes: false, saveTags: true);
        if (_journal.OpenJournal != null)
        {
            TagsBox.Text = _journal.OpenJournal.Tags ?? TagsBox.Text;
        }

        SetStatus(msg);
    }

    private static void FillCombo(ComboBox box, IEnumerable<string> names, string? prefer)
    {
        var list = names
            .Where(n => !string.IsNullOrWhiteSpace(n)
                && !string.Equals(n, "(none)", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        list.Insert(0, "(none)");

        box.ItemsSource = list;
        if (!string.IsNullOrWhiteSpace(prefer)
            && !string.Equals(prefer, "(none)", StringComparison.OrdinalIgnoreCase)
            && list.Any(n => string.Equals(n, prefer, StringComparison.OrdinalIgnoreCase)))
        {
            box.SelectedItem = list.First(n => string.Equals(n, prefer, StringComparison.OrdinalIgnoreCase));
            box.Text = prefer;
        }
        else
        {
            box.SelectedItem = "(none)";
            box.Text = "";
        }
    }

    private static void ClearCombo(ComboBox box)
    {
        box.SelectedItem = null;
        box.Text = "";
    }

    private static string GetComboText(ComboBox box)
    {
        if (box.SelectedItem is string s && !string.IsNullOrWhiteSpace(s)
            && !string.Equals(s, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        var text = box.Text?.Trim() ?? "";
        if (string.Equals(text, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return text;
    }

    private void SetStatus(string msg) => StatusBar.Text = msg;

    private static string FormatError(Exception ex) =>
        ex is ApiException ae
            ? $"API {ae.StatusCode}: {(ae.ResponseBody.Length > 120 ? ae.ResponseBody[..120] + "…" : ae.ResponseBody)}"
            : ex.Message;
}
