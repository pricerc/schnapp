namespace Schnapp.Client.Linux.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Tags;
using Schnapp.Client.Linux.Services;
using Schnapp.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Batch editor for selected offline-queue screenshots (MainWindow layout without capture controls).
/// </summary>
public partial class ScreenshotEditWindow : Window
{
    private readonly CaptureService _capture;
    private readonly JournalSessionService _journal;
    private readonly IReadOnlyList<Uuid7> _itemIds;
    private readonly ClientSettingsStore _store = new();
    private IReadOnlyList<JournalSessionService.MruEntry> _mruItems =
        Array.Empty<JournalSessionService.MruEntry>();
    private bool _suppressMru;
    private bool _suppressCascade;

    /// <summary>XAML / designer constructor.</summary>
    public ScreenshotEditWindow() : this(null!, null!, Array.Empty<Uuid7>())
    {
    }

    public ScreenshotEditWindow(
        CaptureService capture,
        JournalSessionService journal,
        IReadOnlyList<Uuid7> itemIds)
    {
        _capture = capture;
        _journal = journal;
        _itemIds = itemIds ?? Array.Empty<Uuid7>();
        InitializeComponent();
        if (capture is null || journal is null || _itemIds.Count == 0)
        {
            return;
        }

        CustomerBox.SelectionChanged += OnCustomerSelectionChanged;
        ProjectBox.SelectionChanged += OnProjectSelectionChanged;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        ActiveTargetText.Text = "Editing " + _itemIds.Count + " screenshot(s)";
        ActiveTimeText.Text = "Apply updates journal link, notes, and tags on the selection.";
        PrefillFromItems(out var commonJournalId, out var mixed);
        await _journal.TryRefreshAsync();
        TagsBox.SetSuggestions(_capture.Uploads.ReferenceData);
        await PrefillTargetFromJournalAsync(commonJournalId);
        BindLists();
        if (mixed)
        {
            SetStatus("Selection has mixed values — Apply overwrites all selected items.");
        }
    }

    private void PrefillFromItems(out Uuid7? commonJournalId, out bool mixed)
    {
        string? notes = null;
        string? tags = null;
        Uuid7? journalId = null;
        var notesUniform = true;
        var tagsUniform = true;
        var journalUniform = true;

        foreach (var id in _itemIds)
        {
            var meta = _capture.Spool.ReadUploadMetadata(id);
            var n = meta?.Notes ?? "";
            var t = meta?.Tags ?? "";
            var j = meta?.JournalEntryId is { } jid && jid != Uuid7.Empty ? jid : (Uuid7?)null;

            if (notes is null)
            {
                notes = n;
                tags = t;
                journalId = j;
            }
            else
            {
                if (!string.Equals(notes, n, StringComparison.Ordinal))
                {
                    notesUniform = false;
                }

                if (!string.Equals(tags, t, StringComparison.Ordinal))
                {
                    tagsUniform = false;
                }

                if (journalId != j)
                {
                    journalUniform = false;
                }
            }
        }

        NotesBox.Text = notesUniform ? notes ?? "" : "";
        TagsBox.Text = tagsUniform ? tags ?? "" : "";
        commonJournalId = journalUniform ? journalId : null;
        mixed = !notesUniform || !tagsUniform || !journalUniform;
    }

    private async Task PrefillTargetFromJournalAsync(Uuid7? journalEntryId)
    {
        if (journalEntryId is not { } jEntry)
        {
            return;
        }

        JournalTargetResponse? target = null;
        if (_journal.OpenJournal is { } open && open.Id == jEntry)
        {
            target = _journal.Targets.FirstOrDefault(t => t.Id == open.JournalTargetId);
        }

        if (target is null)
        {
            try
            {
                var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
                if (!string.IsNullOrEmpty(token))
                {
                    using var api = new SchnappApiClient(_store.Load().ApiBaseUrl, token);
                    var journals = await api.ListJournalsAsync();
                    var entry = journals.FirstOrDefault(j => j.Id == jEntry);
                    if (entry is not null)
                    {
                        target = _journal.Targets.FirstOrDefault(t => t.Id == entry.JournalTargetId);
                    }
                }
            }
            catch
            {
                // offline
            }
        }

        if (target is not null)
        {
            ApplyTargetNamesToBoxes(target);
            ActiveTimeText.Text = "Journal: " + _journal.FormatTarget(target);
        }
    }

    private void ApplyTargetNamesToBoxes(JournalTargetResponse target)
    {
        var names = _journal.NamesFor(target);
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
    }

    private void BindLists()
    {
        var customer = GetComboText(CustomerBox);
        var project = GetComboText(ProjectBox);
        var task = GetComboText(TaskBox);
        _suppressCascade = true;
        try
        {
            FillCombo(CustomerBox, _journal.Customers.Select(c => c.Name).OrderBy(n => n), customer);
            FillCombo(ProjectBox, _journal.ProjectsFor(customer).Select(p => p.Name).OrderBy(n => n), project);
            FillCombo(TaskBox, _journal.TasksFor(customer, project).Select(t => t.Name).OrderBy(n => n), task);

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
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        SetStatus("Refreshing…");
        try
        {
            await _journal.RefreshAsync();
            BindLists();
            SetStatus("Lists refreshed.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async void OnApply(object? sender, RoutedEventArgs e) => await ApplyAsync(clear: false);

    private async void OnClear(object? sender, RoutedEventArgs e) => await ApplyAsync(clear: true);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async Task ApplyAsync(bool clear)
    {
        try
        {
            string notes;
            string tags;
            Uuid7? journalEntryId;
            var setJournal = true;

            if (clear)
            {
                notes = "";
                tags = "";
                journalEntryId = null;
                NotesBox.Text = "";
                TagsBox.Text = "";
                _suppressCascade = true;
                try
                {
                    FillCombo(CustomerBox, _journal.Customers.Select(c => c.Name).OrderBy(n => n), null);
                    FillCombo(ProjectBox, _journal.ProjectsFor(null).Select(p => p.Name).OrderBy(n => n), null);
                    FillCombo(TaskBox, _journal.TasksFor(null, null).Select(t => t.Name).OrderBy(n => n), null);
                }
                finally
                {
                    _suppressCascade = false;
                }
            }
            else
            {
                notes = NotesBox.Text ?? "";
                tags = TagNormalizer.Normalize(TagsBox.Text ?? "");
                TagsBox.Text = tags;

                var customer = NullIfNone(GetComboText(CustomerBox));
                var project = NullIfNone(GetComboText(ProjectBox));
                var task = NullIfNone(GetComboText(TaskBox));

                if (customer is null && project is null && task is null)
                {
                    journalEntryId = null;
                }
                else
                {
                    var (target, _, targetStatus) = await _journal.EnsureTargetFromNamesAsync(
                        customer, project, task);
                    ActiveTimeText.Text = targetStatus;
                    journalEntryId = await _journal.FindJournalEntryIdForTargetAsync(target.Id);
                    if (journalEntryId is null)
                    {
                        SetStatus("No journal entry for that target — notes/tags will save; journal link cleared.");
                    }
                }
            }

            SchnappApiClient? api = null;
            try
            {
                var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
                if (!string.IsNullOrEmpty(token))
                {
                    api = new SchnappApiClient(_store.Load().ApiBaseUrl, token);
                }
            }
            catch
            {
                api = null;
            }

            try
            {
                var result = await _capture.Uploads.ApplyScreenshotMetadataAsync(
                    _capture.Spool,
                    [.. _itemIds],
                    notes,
                    tags,
                    setJournal,
                    journalEntryId,
                    api);

                SetStatus((clear ? "Cleared " : "Updated ") + result.LocalOk + " local item(s)"
                    + (result.ServerOk > 0 ? "; server " + result.ServerOk : "")
                    + (result.ServerQueued > 0 ? "; queued " + result.ServerQueued + " for push" : "")
                    + (result.LastError is not null ? " (" + result.LastError + ")" : "")
                    + ".");
            }
            finally
            {
                api?.Dispose();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnCustomerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressCascade)
        {
            return;
        }

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

    private void OnMruSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressMru || MruBox.SelectedIndex < 0 || MruBox.SelectedIndex >= _mruItems.Count)
        {
            return;
        }

        ApplyTargetNamesToBoxes(_mruItems[MruBox.SelectedIndex].Target);
        SetStatus("Filled from recent target — click Apply to save.");
    }

    private void SetStatus(string text) => StatusBar.Text = text;

    private static string? NullIfNone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        if (string.Equals(t, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return t;
    }

    private static string? GetComboText(ComboBox box)
    {
        if (box.SelectedItem is string s)
        {
            return s;
        }

        return box.Text;
    }

    private static void ClearCombo(ComboBox box)
    {
        box.SelectedItem = null;
        box.Text = "";
    }

    private static void FillCombo(ComboBox box, IEnumerable<string> items, string? selected)
    {
        var list = items.ToList();
        if (!list.Contains("(none)", StringComparer.OrdinalIgnoreCase))
        {
            list.Insert(0, "(none)");
        }

        box.ItemsSource = list;
        if (!string.IsNullOrWhiteSpace(selected)
            && list.Any(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)))
        {
            box.SelectedItem = list.First(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
            box.Text = selected;
        }
        else if (string.IsNullOrWhiteSpace(selected))
        {
            box.SelectedItem = "(none)";
            box.Text = "(none)";
        }
        else
        {
            box.SelectedItem = null;
            box.Text = selected;
        }
    }
}
