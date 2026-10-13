namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Tags;
using Schnapp.Client.Windows.Services;
using Schnapp.Contracts;

/// <summary>
/// Batch editor for selected offline-queue screenshots (MainForm layout without capture controls).
/// Applies customer/project/task, notes, and tags to local spool metadata and, when already
/// uploaded, patches the server.
/// </summary>
public partial class ScreenshotEditForm : Form
{
    private readonly CaptureService _capture;
    private readonly JournalSessionService _journal;
    private readonly IReadOnlyList<Uuid7> _itemIds;
    private readonly ClientSettingsStore _store = new();

    private IReadOnlyList<JournalSessionService.MruEntry> _mruItems =
        Array.Empty<JournalSessionService.MruEntry>();

    private bool _suppressMru;
    private bool _suppressCascade;

    /// <summary>
    /// Creates the editor for the given spool item ids.
    /// </summary>
    public ScreenshotEditForm(
        CaptureService capture,
        JournalSessionService journal,
        IReadOnlyList<Uuid7> itemIds)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _itemIds = itemIds ?? throw new ArgumentNullException(nameof(itemIds));
        if (_itemIds.Count == 0)
        {
            throw new ArgumentException("At least one item id is required.", nameof(itemIds));
        }

        InitializeComponent();
    }

    /// <inheritdoc />
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ActiveTargetText.Text = "Editing " + _itemIds.Count + " screenshot(s)";
        ActiveTimeText.Text = "Apply updates journal link, notes, and tags on the selection.";
        PrefillFromItems(out var commonJournalId, out var mixed);
        await _journal.TryRefreshAsync();
        await PrefillTargetFromJournalAsync(commonJournalId);
        RefreshLists();
        TagsBox.SetSuggestions(_capture.Uploads.ReferenceData);
        if (mixed)
        {
            StatusBar.Text = "Selection has mixed values — Apply overwrites all selected items.";
        }
    }

    /// <summary>Loads notes/tags from the selection (common values when uniform).</summary>
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

            if (notes == null)
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

        NotesBox.Text = notesUniform ? (notes ?? "") : "";
        TagsBox.Text = tagsUniform ? (tags ?? "") : "";
        commonJournalId = journalUniform ? journalId : null;
        mixed = !notesUniform || !tagsUniform || !journalUniform;
    }

    /// <summary>When every selected item shares a journal entry, fill C/P/T from that entry's target.</summary>
    private async Task PrefillTargetFromJournalAsync(Uuid7? journalEntryId)
    {
        if (journalEntryId is not { } jEntry)
        {
            return;
        }

        JournalTargetResponse? target = null;
        if (_journal.OpenJournal != null && _journal.OpenJournal.Id == jEntry)
        {
            target = _journal.Targets.FirstOrDefault(t => t.Id == _journal.OpenJournal.JournalTargetId);
        }

        if (target == null)
        {
            try
            {
                var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
                if (!string.IsNullOrEmpty(token))
                {
                    var settings = _store.Load();
                    using (var api = new SchnappApiClient(settings.ApiBaseUrl, token))
                    {
                        var journals = await api.ListJournalsAsync();
                        var entry = journals.FirstOrDefault(j => j.Id == jEntry);
                        if (entry != null)
                        {
                            target = _journal.Targets.FirstOrDefault(t => t.Id == entry.JournalTargetId);
                        }
                    }
                }
            }
            catch
            {
                // leave C/P/T empty when offline
            }
        }

        if (target != null)
        {
            ApplyTargetNamesToBoxes(target);
            ActiveTimeText.Text = "Journal: " + _journal.FormatTarget(target);
        }
    }

    private void ApplyTargetNamesToBoxes(JournalTargetResponse target)
    {
        var names = _journal.NamesFor(target);
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
    }

    private void RefreshLists()
    {
        var customer = CustomerBox.SafeText;
        var project = ProjectBox.SafeText;
        var task = TaskBox.SafeText;

        _suppressCascade = true;
        using (CustomerBox.SuspendFiltering())
        using (ProjectBox.SuspendFiltering())
        using (TaskBox.SuspendFiltering())
        {
            try
            {
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
    }

    private void FillMruCombo()
    {
        _suppressMru = true;
        using (MruBox.SuspendFiltering())
        {
            try
            {
                _mruItems = [.. _journal.BuildMru()];
                MruBox.SetCandidatesExact([.. _mruItems.Select(m => m.Display)], selected: "");
            }
            finally
            {
                _suppressMru = false;
            }
        }
    }

    private async void RefreshButton_Click(object sender, EventArgs e)
    {
        StatusBar.Text = "Refreshing…";
        try
        {
            await _journal.RefreshAsync();
            RefreshLists();
            StatusBar.Text = "Lists refreshed.";
        }
        catch (Exception ex)
        {
            StatusBar.Text = "Refresh failed: " + ex.Message;
        }
    }

    private async void ApplyButton_Click(object sender, EventArgs e)
    {
        await ApplyAsync(clearJournalAndFields: false);
    }

    private async void ClearButton_Click(object sender, EventArgs e)
    {
        await ApplyAsync(clearJournalAndFields: true);
    }

    /// <summary>
    /// Writes notes/tags/journal to every selected spool item (local + server when uploaded).
    /// </summary>
    private async Task ApplyAsync(bool clearJournalAndFields)
    {
        ApplyButton.Enabled = false;
        ClearButton.Enabled = false;
        try
        {
            string? notes;
            string? tags;
            Uuid7? journalEntryId;
            bool setJournal;

            if (clearJournalAndFields)
            {
                notes = "";
                tags = "";
                journalEntryId = null;
                setJournal = true;
                _suppressCascade = true;
                using (CustomerBox.SuspendFiltering())
                using (ProjectBox.SuspendFiltering())
                using (TaskBox.SuspendFiltering())
                {
                    try
                    {
                        CustomerBox.SetCandidates(_journal.Customers.Select(c => c.Name), "", includeNone: true);
                        ProjectBox.SetCandidates(_journal.ProjectsFor(null).Select(p => p.Name), "", includeNone: true);
                        TaskBox.SetCandidates(_journal.TasksFor(null, null).Select(t => t.Name), "", includeNone: true);
                    }
                    finally
                    {
                        _suppressCascade = false;
                    }
                }

                NotesBox.Text = "";
                TagsBox.Text = "";
            }
            else
            {
                notes = NotesBox.Text ?? "";
                tags = TagNormalizer.Normalize(TagsBox.Text ?? "");
                TagsBox.Text = tags;

                var customer = NullIfNone(CustomerBox.SafeText);
                var project = NullIfNone(ProjectBox.SafeText);
                var task = NullIfNone(TaskBox.SafeText);

                // All empty C/P/T → clear journal link; still apply notes/tags.
                if (customer == null && project == null && task == null)
                {
                    journalEntryId = null;
                    setJournal = true;
                }
                else
                {
                    var (target, _, targetStatus) = await _journal.EnsureTargetFromNamesAsync(
                        customer, project, task);
                    ActiveTimeText.Text = targetStatus;
                    journalEntryId = await _journal.FindJournalEntryIdForTargetAsync(target.Id);
                    setJournal = true;
                    if (journalEntryId == null)
                    {
                        StatusBar.Text = "No journal entry for that target — notes/tags will save; journal link cleared.";
                    }
                }
            }

            SchnappApiClient? api = null;
            try
            {
                var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
                var settings = _store.Load();
                if (!string.IsNullOrEmpty(token))
                {
                    api = new SchnappApiClient(settings.ApiBaseUrl, token);
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

                var prefix = clearJournalAndFields
                    ? "Cleared "
                    : journalEntryId == null && setJournal
                        ? "Updated "
                        : "Updated ";
                var detail = clearJournalAndFields
                    ? ""
                    : journalEntryId == null && setJournal
                        ? " (notes/tags; no journal link)"
                        : "";
                StatusBar.Text = prefix + result.LocalOk + " local item(s)" + detail
                    + (result.ServerOk > 0 ? "; server " + result.ServerOk : "")
                    + (result.ServerQueued > 0 ? "; queued " + result.ServerQueued + " for push" : "")
                    + (result.LastError != null ? " (" + result.LastError + ")" : "")
                    + ".";
            }
            finally
            {
                api?.Dispose();
            }
        }
        catch (Exception ex)
        {
            StatusBar.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed)
            {
                ApplyButton.Enabled = true;
                ClearButton.Enabled = true;
            }
        }
    }

    private void CustomerBox_SelectionChangeCommitted(object sender, EventArgs e) =>
        CascadeFromCustomer(clearChildren: true);

    private void CustomerBox_Leave(object sender, EventArgs e) =>
        CascadeFromCustomer(clearChildren: false);

    private void ProjectBox_SelectionChangeCommitted(object sender, EventArgs e) =>
        CascadeFromProject(clearTask: true);

    private void ProjectBox_Leave(object sender, EventArgs e) =>
        CascadeFromProject(clearTask: false);

    private void CascadeFromCustomer(bool clearChildren)
    {
        if (_suppressCascade)
        {
            return;
        }

        _suppressCascade = true;
        using (ProjectBox.SuspendFiltering())
        using (TaskBox.SuspendFiltering())
        {
            try
            {
                var customer = CustomerBox.SafeText;
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

    private void MruBox_SelectionChangeCommitted(object sender, EventArgs e)
    {
        if (_suppressMru)
        {
            return;
        }

        var text = MruBox.SafeText.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var mru = _mruItems.FirstOrDefault(m =>
            string.Equals(m.Display, text, StringComparison.OrdinalIgnoreCase));
        if (mru == null)
        {
            return;
        }

        ApplyTargetNamesToBoxes(mru.Target);
        StatusBar.Text = "Filled from recent target — click Apply to save.";
    }

    private static string? NullIfNone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text!.Trim();
        if (string.Equals(t, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return t;
    }

    private void CloseButton_Click(object sender, EventArgs e) => Close();
}
