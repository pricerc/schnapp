namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Tags;
using Schnapp.Client.Windows.Encode;
using Schnapp.Client.Windows.Services;
using Schnapp.Contracts;

/// <summary>
/// Modal image preview for offline spool items with an optional journal/metadata editor
/// on the right (Linux ImagePreviewWindow parity for zoom, extended for in-place edit).
/// </summary>
public partial class ImagePreviewForm : Form
{
    private Image? _image;
    private double _zoom = 1.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;

    /// <summary>
    /// When true, <see cref="Scroller_Resize"/> re-applies fit-to-window.
    /// Cleared when the user chooses an explicit zoom (+/- / 100%).
    /// </summary>
    private bool _fitOnResize = true;

    private CaptureService? _capture;
    private JournalSessionService? _journal;
    private Uuid7? _spoolItemId;
    private readonly ClientSettingsStore _store = new();

    private IReadOnlyList<JournalSessionService.MruEntry> _mruItems =
        Array.Empty<JournalSessionService.MruEntry>();

    private bool _suppressMru;
    private bool _suppressCascade;

    /// <summary>Designer / default constructor.</summary>
    public ImagePreviewForm()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows a modal preview of the given image bytes.
    /// </summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="imageBytes">Image payload.</param>
    /// <param name="title">Window title (e.g. capture time).</param>
    /// <param name="subtitle">Optional detail line under the zoom controls.</param>
    /// <param name="capture">When set with <paramref name="journal"/> and <paramref name="spoolItemId"/>, shows the editor panel.</param>
    /// <param name="journal">Journal session for C/P/T lists and target resolve.</param>
    /// <param name="spoolItemId">Spool item being previewed (for metadata apply).</param>
    public static void ShowPreview(
        IWin32Window? owner,
        byte[] imageBytes,
        string title,
        string? subtitle = null,
        CaptureService? capture = null,
        JournalSessionService? journal = null,
        Uuid7? spoolItemId = null)
    {
        using (var f = new ImagePreviewForm())
        {
            f.Text = string.IsNullOrWhiteSpace(title) ? "Preview" : title;
            f.InfoLabel.Text = subtitle ?? "";
            f.LoadImage(imageBytes);
            if (capture != null && journal != null && spoolItemId is { } id && id != Uuid7.Empty)
            {
                f.EnableJournalEditor(capture, journal, id);
            }

            f.ShowDialog(owner);
        }
    }

    private void EnableJournalEditor(CaptureService capture, JournalSessionService journal, Uuid7 spoolItemId)
    {
        _capture = capture;
        _journal = journal;
        _spoolItemId = spoolItemId;
        JournalPanel.Visible = true;
        // Wider default so the side panel has room without crushing the image.
        if (ClientSize.Width < 1000)
        {
            ClientSize = new Size(1000, Math.Max(ClientSize.Height, 560));
        }
    }

    /// <inheritdoc />
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_image != null)
        {
            BeginInvoke(new Action(ZoomFit));
        }

        if (_capture != null && _journal != null && _spoolItemId.HasValue)
        {
            await InitializeEditorAsync();
        }
    }

    /// <inheritdoc />
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeImage();
        base.OnFormClosed(e);
    }

    private void LoadImage(byte[] bytes)
    {
        DisposeImage();
        try
        {
            // WebP via Imazen.WebP; JPEG/PNG use GDI+ (see ImageEncoder.DecodeToBitmap).
            _image = ImageEncoder.DecodeToBitmap(bytes);
            PreviewImage.Image = _image;
            UpdateZoomUi();
        }
        catch (Exception ex)
        {
            InfoLabel.Text = "Could not load image: " + ex.Message;
        }
    }

    private void DisposeImage()
    {
        PreviewImage.Image = null;
        if (_image != null)
        {
            try { _image.Dispose(); } catch { /* ignore */ }
            _image = null;
        }
    }

    private void ZoomInButton_Click(object sender, EventArgs e)
    {
        _fitOnResize = false;
        SetZoom(_zoom * 1.25);
    }

    private void ZoomOutButton_Click(object sender, EventArgs e)
    {
        _fitOnResize = false;
        SetZoom(_zoom / 1.25);
    }

    private void ZoomActualButton_Click(object sender, EventArgs e)
    {
        _fitOnResize = false;
        SetZoom(1.0);
    }

    private void ZoomFitButton_Click(object sender, EventArgs e)
    {
        _fitOnResize = true;
        ZoomFit();
    }

    private void CloseButton_Click(object sender, EventArgs e) => Close();

    private void Scroller_Resize(object sender, EventArgs e)
    {
        if (_fitOnResize && _image != null)
        {
            ZoomFit();
        }
    }

    private void ZoomFit()
    {
        if (_image == null)
        {
            return;
        }

        _fitOnResize = true;
        var vw = Math.Max(1, Scroller.ClientSize.Width - 8);
        var vh = Math.Max(1, Scroller.ClientSize.Height - 8);
        var sx = (double)vw / Math.Max(1, _image.Width);
        var sy = (double)vh / Math.Max(1, _image.Height);
        SetZoom(Math.Min(sx, sy));
        Scroller.AutoScrollPosition = new Point(0, 0);
    }

    private void SetZoom(double zoom)
    {
        if (_image == null)
        {
            return;
        }

        if (zoom < MinZoom)
        {
            zoom = MinZoom;
        }

        if (zoom > MaxZoom)
        {
            zoom = MaxZoom;
        }

        _zoom = zoom;

        var w = Math.Max(1, (int)Math.Round(_image.Width * _zoom));
        var h = Math.Max(1, (int)Math.Round(_image.Height * _zoom));
        PreviewImage.SizeMode = PictureBoxSizeMode.StretchImage;
        PreviewImage.Size = new Size(w, h);
        UpdateZoomUi();
    }

    private void UpdateZoomUi()
    {
        ZoomLabel.Text = (_zoom * 100).ToString("0") + "%";
    }

    // --- Journal / metadata editor -----------------------------------------------------------

    private async Task InitializeEditorAsync()
    {
        if (_journal == null || _capture == null || !_spoolItemId.HasValue)
        {
            return;
        }

        PrefillFromItem();
        await _journal.TryRefreshAsync();
        TagsBox.SetSuggestions(_capture?.Uploads.ReferenceData);
        await PrefillTargetFromJournalAsync();
        RefreshLists();
        EditorStatusLabel.Text = "Ready";
    }

    private void PrefillFromItem()
    {
        if (_capture == null || !_spoolItemId.HasValue)
        {
            return;
        }

        var meta = _capture.Spool.ReadUploadMetadata(_spoolItemId.Value);
        NotesBox.Text = meta?.Notes ?? "";
        TagsBox.Text = meta?.Tags ?? "";
    }

    private async Task PrefillTargetFromJournalAsync()
    {
        if (_capture == null || _journal == null || !_spoolItemId.HasValue)
        {
            return;
        }

        var meta = _capture.Spool.ReadUploadMetadata(_spoolItemId.Value);
        var journalEntryId = meta?.JournalEntryId is { } jid && jid != Uuid7.Empty ? jid : (Uuid7?)null;
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
                // offline — leave C/P/T empty
            }
        }

        if (target != null)
        {
            ApplyTargetNamesToBoxes(target);
            EditorStatusLabel.Text = "Journal: " + _journal.FormatTarget(target);
        }
    }

    private void ApplyTargetNamesToBoxes(JournalTargetResponse target)
    {
        if (_journal == null)
        {
            return;
        }

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
        if (_journal == null)
        {
            return;
        }

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
        if (_journal == null)
        {
            return;
        }

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

    private async void RefreshListsButton_Click(object sender, EventArgs e)
    {
        if (_journal == null)
        {
            return;
        }

        EditorStatusLabel.Text = "Refreshing…";
        try
        {
            await _journal.RefreshAsync();
            RefreshLists();
            EditorStatusLabel.Text = "Lists refreshed.";
        }
        catch (Exception ex)
        {
            EditorStatusLabel.Text = "Refresh failed: " + ex.Message;
        }
    }

    private async void ApplyButton_Click(object sender, EventArgs e) => await ApplyAsync(clear: false);

    private async void ClearButton_Click(object sender, EventArgs e) => await ApplyAsync(clear: true);

    private async Task ApplyAsync(bool clear)
    {
        if (_capture == null || _journal == null || !_spoolItemId.HasValue)
        {
            return;
        }

        ApplyButton.Enabled = false;
        ClearButton.Enabled = false;
        try
        {
            string notes;
            string tags;
            Uuid7? journalEntryId;
            const bool setJournal = true;

            if (clear)
            {
                notes = "";
                tags = "";
                journalEntryId = null;
                NotesBox.Text = "";
                TagsBox.Text = "";
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
            }
            else
            {
                notes = NotesBox.Text ?? "";
                tags = TagNormalizer.Normalize(TagsBox.Text ?? "");
                TagsBox.Text = tags;

                var customer = NullIfNone(CustomerBox.SafeText);
                var project = NullIfNone(ProjectBox.SafeText);
                var task = NullIfNone(TaskBox.SafeText);

                if (customer == null && project == null && task == null)
                {
                    journalEntryId = null;
                }
                else
                {
                    var (target, _, targetStatus) = await _journal.EnsureTargetFromNamesAsync(
                        customer, project, task);
                    EditorStatusLabel.Text = targetStatus;
                    journalEntryId = await _journal.FindJournalEntryIdForTargetAsync(target.Id);
                    if (journalEntryId == null)
                    {
                        EditorStatusLabel.Text = "No journal entry for that target — notes/tags will save; journal link cleared.";
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
                    [_spoolItemId.Value],
                    notes,
                    tags,
                    setJournal,
                    journalEntryId,
                    api);

                EditorStatusLabel.Text = (clear ? "Cleared" : "Updated")
                    + " local"
                    + (result.ServerOk > 0 ? "; server " + result.ServerOk : "")
                    + (result.ServerQueued > 0 ? "; queued " + result.ServerQueued : "")
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
            EditorStatusLabel.Text = ex.Message;
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
        if (_suppressCascade || _journal == null)
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
        if (_suppressCascade || _journal == null)
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
        if (_suppressMru || _journal == null)
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
        EditorStatusLabel.Text = "Filled from recent target — click Apply to save.";
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
}
