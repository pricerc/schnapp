namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Spool;
using Schnapp.Client.Windows.Services;
using Schnapp.Contracts;

/// <summary>
/// Offline screenshot spool browser with batch rows and thumbnail columns (Linux OfflineQueueWindow parity).
/// </summary>
/// <remarks>
/// Each row is one capture batch (same second). Dynamic columns are window / desktop / monitors.
/// Double-click a thumbnail to preview. Selection + delete operate on whole rows.
/// </remarks>
public partial class OfflineQueueForm : Form
{
    private const int ThumbWidth = 96;
    private const int ThumbHeight = 64;
    private const string CaptureColumnPrefix = "CaptureColumn";

    private readonly CaptureService _capture;
    private readonly JournalSessionService? _journal;
    private readonly ClientSettingsStore _store = new();

    /// <summary>Decoded thumbnails keyed by spool item id (lazy; disposed on reload/close).</summary>
    private readonly Dictionary<Uuid7, Image> _thumbCache = [];

    /// <summary>Spool ids with a thumbnail decode currently in flight.</summary>
    private readonly HashSet<Uuid7> _thumbLoading = [];

    /// <summary>Batch rows currently shown by the virtual grid (metadata only until thumbs load).</summary>
    private List<RowModel> _rows = [];

    /// <summary>Transparent placeholder so empty image cells do not show the red error glyph.</summary>
    private Image? _emptyThumb;

    /// <summary>Journal entry id → target label and notes for grid columns.</summary>
    private IReadOnlyDictionary<Uuid7, JournalLabel> _journalLabels =
        new Dictionary<Uuid7, JournalLabel>();

    /// <summary>Local calendar date filter; null = all dates. net48 has no <c>DateOnly</c>.</summary>
    private DateTime? _filterDate;

    private CancellationTokenSource? _reloadCts;
    private int _firstImageColumnIndex = -1;
    private int _imageColumnCount;

    /// <summary>
    /// Bumped on each <see cref="ApplyViewModel"/> so in-flight thumbnail tasks ignore superseded reloads.
    /// </summary>
    private int _viewGeneration;

    /// <summary>
    /// True while programmatically aligning checkbox cells with row selection (or vice versa)
    /// so handlers do not re-enter each other.
    /// </summary>
    private bool _syncingSelection;

    /// <summary>
    /// Row indices selected at the start of a mouse-down (used so checkbox clicks can toggle
    /// one row without the default click clearing the rest of a multi-selection).
    /// </summary>
    private HashSet<int>? _selectionSnapshot;

    /// <summary>Row index of a checkbox-column mouse-down, or -1 if none.</summary>
    private int _checkboxToggleRow = -1;

    /// <summary>Creates the offline queue window.</summary>
    /// <param name="capture">Running capture/upload service (owns the spool).</param>
    /// <param name="journal">Optional journal session for the batch screenshot editor.</param>
    public OfflineQueueForm(CaptureService capture, JournalSessionService? journal = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _journal = journal;
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var s = _store.Load();
        WindowPlacement.RestoreOrPlaceNearTray(this, s.OfflineQueueWindow);
        _filterDate = DateTime.Now.Date;
        UpdateDateFilterLabel();
        Reload();
    }

    /// <inheritdoc />
    /// <remarks>
    /// User close (X / Close button) hides the window; the tray owns the instance for the process lifetime.
    /// </remarks>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Because the tray shell keeps this form alive; UserClosing should only hide, not dispose.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            try
            {
                _reloadCts?.Cancel();
                var s = _store.Load();
                s.OfflineQueueWindow = WindowPlacement.Capture(this);
                _store.Save(s);
            }
            catch { /* ignore placement save */ }
            Hide();
            return;
        }

        try
        {
            _reloadCts?.Cancel();
        }
        catch { /* ignore */ }

        base.OnFormClosing(e);
    }

    /// <summary>Shows the window (if hidden), activates it, and reloads the queue.</summary>
    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Normal;
        Activate();
        Reload();
    }

    private void Reload()
    {
        _reloadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _reloadCts = cts;
        _ = ReloadAsync(cts.Token);
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        SetLoading(true);
        try
        {
            await LoadJournalLabelsAsync(ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            var filter = _filterDate;
            var labels = _journalLabels;
            var model = await Task.Run(() => BuildViewModel(filter, labels, ct), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            ApplyViewModel(model);
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch (Exception ex)
        {
            AppLog.Error("OfflineQueueForm.Reload failed", ex);
            if (!IsDisposed)
            {
                SummaryText.Text = "Failed to load offline queue: " + ex.Message;
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested && !IsDisposed)
            {
                SetLoading(false);
            }
        }
    }

    /// <summary>
    /// Resolves journal entry ids to Customer/Project/Task for the Journal column and
    /// notes for the Notes column. Uses local reference cache first; refreshes from API when online.
    /// </summary>
    private async Task LoadJournalLabelsAsync(CancellationToken ct)
    {
        var map = new Dictionary<Uuid7, JournalLabel>();
        try
        {
            var cache = _capture.Uploads.ReferenceData;
            var customers = cache.GetCustomers();
            var projects = cache.GetProjects();
            var tasks = cache.GetTasks();
            var targets = cache.GetJournalTargets();

            IReadOnlyList<JournalEntryResponse> journals = Array.Empty<JournalEntryResponse>();
            try
            {
                var token = _store.LoadToken()
                            ?? Environment.GetEnvironmentVariable("SCHNAPP_TOKEN")
                            ?? "";
                if (!string.IsNullOrEmpty(token))
                {
                    var settings = _store.Load();
                    using (var api = new SchnappApiClient(settings.ApiBaseUrl, token))
                    {
                        journals = await api.ListJournalsAsync(ct).ConfigureAwait(true);
                        // Prefer fresh targets if available.
                        try
                        {
                            var freshTargets = await api.ListJournalTargetsAsync(ct).ConfigureAwait(true);
                            if (freshTargets.Count > 0)
                            {
                                targets = [.. freshTargets];
                            }
                        }
                        catch { /* keep cache */ }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug("OfflineQueue journal labels (API): " + ex.Message);
            }

            foreach (var j in journals)
            {
                ct.ThrowIfCancellationRequested();
                var target = targets.FirstOrDefault(t => t.Id == j.JournalTargetId);
                map[j.Id] = new JournalLabel
                {
                    TargetText = FormatJournalTarget(target, customers, projects, tasks, j.IsOpen),
                    Notes = string.IsNullOrWhiteSpace(j.Notes) ? "" : j.Notes!.Trim()
                };
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("OfflineQueue journal labels: " + ex.Message);
        }

        _journalLabels = map;
    }

    private static string FormatJournalTarget(
        JournalTargetResponse? target,
        List<ListItemResponse> customers,
        List<ListItemResponse> projects,
        List<ListItemResponse> tasks,
        bool isOpen)
    {
        string targetText;
        if (target == null)
        {
            targetText = "(unknown target)";
        }
        else if (!string.IsNullOrWhiteSpace(target.Name) && target.Name != "(none)")
        {
            targetText = target.Name.Trim();
        }
        else
        {
            var c = customers.FirstOrDefault(x => x.Id == target.CustomerId)?.Name
                    ?? (target.CustomerId == Uuid7.Empty ? "(none)" : "?");
            var p = projects.FirstOrDefault(x => x.Id == target.ProjectId)?.Name
                    ?? (target.ProjectId == Uuid7.Empty ? "(none)" : "?");
            var t = tasks.FirstOrDefault(x => x.Id == target.TaskId)?.Name
                    ?? (target.TaskId == Uuid7.Empty ? "(none)" : "?");
            targetText = c + " / " + p + " / " + t;
        }

        return targetText + " (" + (isOpen ? "open" : "closed") + ")";
    }

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            SummaryText.Text = "Loading…";
        }

        SelectAllButton.Enabled = !loading;
        SelectNoneButton.Enabled = !loading;
        RefreshButton.Enabled = !loading;
        PushButton.Enabled = !loading;
        OlderButton.Enabled = !loading;
        NewerButton.Enabled = !loading;
        ShowAllButton.Enabled = !loading && _filterDate.HasValue;
        PreviewButton.Enabled = !loading;
        if (loading)
        {
            EditButton.Enabled = false;
            DeleteButton.Enabled = false;
        }
        else
        {
            UpdateDeleteEnabled();
        }
    }

    private sealed class JournalLabel
    {
        public string TargetText = "";
        public string Notes = "";
    }

    private sealed class ColumnDef
    {
        public string Key = "";
        public string Header = "";
    }

    private sealed class CellModel
    {
        public Uuid7 Id;
        public string KindText = "";
        public SpoolUploadStatus Status;
    }

    private sealed class RowModel
    {
        public DateTimeOffset Timestamp;
        public string TimestampText = "";
        public string JournalText = "";
        public string NotesText = "";
        public string ExtraText = "";
        public CellModel?[] Cells = Array.Empty<CellModel?>();
        public List<Uuid7> Ids = [];
    }

    private sealed class ViewModel
    {
        public List<ColumnDef> Columns = [];
        public List<RowModel> Rows = [];
        public string SummaryText = "";
    }

    private ViewModel BuildViewModel(
        DateTime? filterDate,
        IReadOnlyDictionary<Uuid7, JournalLabel> journalLabels,
        CancellationToken ct)
    {
        _capture.Spool.PruneExpired();
        var pending = _capture.Spool.ListItems();
        var uploaded = _capture.Spool.ListUploadedItems();
        var status = _capture.Spool.GetStatus();

        var loaded = pending.Concat(uploaded)
            .Select(item =>
            {
                var meta = _capture.Spool.ReadUploadMetadata(item.Id);
                var col = ResolveColumn(meta, item.FileName);
                return (Item: item, Meta: meta, Col: col);
            })
            .ToList();

        if (filterDate is DateTime day)
        {
            var d = day.Date;
            loaded = [.. loaded.Where(x => LocalDateOf(x.Meta?.Timestamp ?? x.Item.EnqueuedUtc) == d)];
        }

        ct.ThrowIfCancellationRequested();

        var summary = pending.Count + uploaded.Count == 0
            ? "Nothing here yet — pending uploads and recently-uploaded screenshots will show up here."
            : loaded.Count == 0
                ? "No items on " + (filterDate?.ToString("yyyy-MM-dd") ?? "?") + ". "
                  + pending.Count + " pending / " + uploaded.Count + " cached in total."
                : pending.Count + " pending (" + status.TotalBytes.ToString("N0") + " bytes) · "
                  + uploaded.Count + " cached (" + status.UploadedBytes.ToString("N0") + " bytes)"
                  + (string.IsNullOrEmpty(status.RootPath) ? "" : " · " + status.RootPath);

        var mutPending = 0;
        try { mutPending = _capture.Uploads.PendingCount; } catch { /* ignore */ }
        if (mutPending > 0)
        {
            summary += " · Mutations pending: " + mutPending;
        }

        var columns = loaded
            .Select(x => x.Col)
            .GroupBy(c => c.Key)
            .Select(g => g.First())
            .OrderBy(c => ColumnSortKey(c.Key).Group)
            .ThenBy(c => ColumnSortKey(c.Key).Index)
            .ToList();

        var columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            columnIndex[columns[i].Key] = i;
        }

        var groups = loaded
            .GroupBy(x => TruncateToSecondUtc(x.Meta?.Timestamp ?? x.Item.EnqueuedUtc))
            .OrderByDescending(g => g.Key);

        var rows = new List<RowModel>();
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var cells = new CellModel?[columns.Count];
            string? journalText = null;
            string? notesText = null;
            var attemptsTotal = 0;
            string? firstError = null;
            var count = 0;
            var uploadedCount = 0;
            var ids = new List<Uuid7>();

            foreach (var (item, meta, col) in group)
            {
                count++;
                ids.Add(item.Id);
                if (item.UploadStatus == SpoolUploadStatus.Uploaded)
                {
                    uploadedCount++;
                }

                var idx = columnIndex[col.Key];
                // Thumbnails are lazy-loaded when a virtual image cell is painted.
                cells[idx] = new CellModel
                {
                    Id = item.Id,
                    KindText = DescribeCaptureKind(meta, item.FileName),
                    Status = item.UploadStatus
                };

                if (journalText == null)
                {
                    var jid = meta?.JournalEntryId;
                    if (jid == null || jid == Uuid7.Empty)
                    {
                        journalText = "(none)";
                        notesText = meta?.Notes?.Trim() ?? "";
                    }
                    else if (journalLabels.TryGetValue(jid.Value, out var label))
                    {
                        journalText = string.IsNullOrEmpty(label.TargetText) ? "(unknown journal)" : label.TargetText;
                        // Prefer journal notes; fall back to screenshot metadata notes.
                        notesText = !string.IsNullOrEmpty(label.Notes)
                            ? label.Notes
                            : (meta?.Notes?.Trim() ?? "");
                    }
                    else
                    {
                        journalText = "(unknown journal)";
                        notesText = meta?.Notes?.Trim() ?? "";
                    }
                }

                attemptsTotal += item.Attempts;
                if (firstError == null && !string.IsNullOrEmpty(item.LastError))
                {
                    firstError = item.LastError;
                }
            }

            var statusText = uploadedCount == count ? "Uploaded"
                : uploadedCount == 0 ? "Pending"
                : "Pending (" + (count - uploadedCount) + "/" + count + ")";
            var extra = attemptsTotal > 0 || firstError != null
                ? statusText + " · " + count + " item(s) · attempts " + attemptsTotal
                  + (firstError != null ? " · " + firstError : "")
                : statusText + " · " + count + " item(s)";

            rows.Add(new RowModel
            {
                Timestamp = group.Key,
                TimestampText = group.Key.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                JournalText = journalText ?? "(none)",
                NotesText = notesText ?? "",
                ExtraText = extra,
                Cells = cells,
                Ids = ids
            });
        }

        return new ViewModel
        {
            Columns = columns,
            Rows = rows,
            SummaryText = summary
        };
    }

    private void ApplyViewModel(ViewModel model)
    {
        DisposeThumbnails();
        _viewGeneration++;
        EnsureEmptyThumb();
        QueueGrid.SuspendLayout();
        try
        {
            // VirtualMode: clear count before column rebuild so CellValueNeeded is not called mid-change.
            QueueGrid.RowCount = 0;
            RebuildCaptureColumns(model.Columns);
            _rows = model.Rows;
            QueueGrid.RowTemplate.Height = ThumbHeight + 8;
            QueueGrid.RowCount = _rows.Count;
            SummaryText.Text = model.SummaryText;
            UpdateDeleteEnabled();
        }
        finally
        {
            QueueGrid.ResumeLayout();
        }
    }

    /// <summary>
    /// Inserts dynamic capture thumbnail columns after <see cref="TimeColumn"/>, keeping the
    /// designer-declared Select/Time/Journal/Notes/Status columns in place.
    /// </summary>
    private void RebuildCaptureColumns(IReadOnlyList<ColumnDef> columns)
    {
        // Remove previous dynamic image columns only.
        for (var i = QueueGrid.Columns.Count - 1; i >= 0; i--)
        {
            var name = QueueGrid.Columns[i].Name ?? "";
            if (name.StartsWith(CaptureColumnPrefix, StringComparison.Ordinal))
            {
                QueueGrid.Columns.RemoveAt(i);
            }
        }

        _imageColumnCount = columns.Count;
        var insertAt = TimeColumn.Index + 1;
        _firstImageColumnIndex = columns.Count > 0 ? insertAt : -1;

        for (var i = 0; i < columns.Count; i++)
        {
            var imgCol = new DataGridViewImageColumn
            {
                Name = CaptureColumnPrefix + i,
                HeaderText = columns[i].Header,
                Width = ThumbWidth + 16,
                ReadOnly = true,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                ValuesAreIcons = false
            };
            // Prevent red error icon when a batch has no image in this column.
            imgCol.DefaultCellStyle.NullValue = _emptyThumb;
            QueueGrid.Columns.Insert(insertAt + i, imgCol);
        }
    }

    private void EnsureEmptyThumb()
    {
        if (_emptyThumb != null)
        {
            return;
        }

        var bmp = new Bitmap(ThumbWidth, ThumbHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(32, 32, 32));
        }
        _emptyThumb = bmp;
    }

    private void DisposeThumbnails()
    {
        foreach (var img in _thumbCache.Values)
        {
            try { img.Dispose(); } catch { /* ignore */ }
        }
        _thumbCache.Clear();
        _thumbLoading.Clear();
    }

    /// <summary>
    /// Returns a cached thumbnail, or the empty placeholder while a background decode is started.
    /// </summary>
    private Image GetThumbnailOrStartLoad(CellModel cell, int rowIndex, int columnIndex)
    {
        if (_thumbCache.TryGetValue(cell.Id, out var cached))
        {
            return cached;
        }

        EnsureEmptyThumb();
        if (_thumbLoading.Contains(cell.Id))
        {
            return _emptyThumb!;
        }

        _thumbLoading.Add(cell.Id);
        var gen = _viewGeneration;
        var id = cell.Id;
        var status = cell.Status;
        var capture = _capture;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Image? thumb = null;
            try
            {
                var bytes = capture.Spool.ReadImageBytes(id);
                thumb = MakeThumbnail(bytes, ThumbWidth, ThumbHeight, status);
            }
            catch
            {
                thumb = null;
            }

            void Apply()
            {
                if (IsDisposed || gen != _viewGeneration)
                {
                    try { thumb?.Dispose(); } catch { /* ignore */ }
                    return;
                }

                _thumbLoading.Remove(id);
                if (thumb != null)
                {
                    if (_thumbCache.TryGetValue(id, out var old))
                    {
                        try { old.Dispose(); } catch { /* ignore */ }
                    }

                    _thumbCache[id] = thumb;
                }

                if (rowIndex >= 0 && rowIndex < QueueGrid.RowCount
                    && columnIndex >= 0 && columnIndex < QueueGrid.Columns.Count)
                {
                    QueueGrid.InvalidateCell(columnIndex, rowIndex);
                }
            }

            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke(new Action(Apply));
                }
                catch
                {
                    try { thumb?.Dispose(); } catch { /* ignore */ }
                }
            }
            else
            {
                try { thumb?.Dispose(); } catch { /* ignore */ }
            }
        });

        return _emptyThumb!;
    }

    private RowModel? TryGetRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count)
        {
            return null;
        }

        return _rows[rowIndex];
    }

    private CellModel? TryGetCaptureCell(int rowIndex, int columnIndex)
    {
        var row = TryGetRow(rowIndex);
        if (row == null || _firstImageColumnIndex < 0)
        {
            return null;
        }

        var i = columnIndex - _firstImageColumnIndex;
        if (i < 0 || i >= row.Cells.Length)
        {
            return null;
        }

        return row.Cells[i];
    }

    private void QueueGrid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var row = TryGetRow(e.RowIndex);
        if (row == null)
        {
            return;
        }

        var colName = QueueGrid.Columns[e.ColumnIndex].Name ?? "";
        if (colName == SelectColumn.Name)
        {
            e.Value = QueueGrid.Rows[e.RowIndex].Selected;
            return;
        }

        if (colName == TimeColumn.Name)
        {
            e.Value = row.TimestampText;
            return;
        }

        if (colName == JournalColumn.Name)
        {
            e.Value = row.JournalText;
            return;
        }

        if (colName == NotesColumn.Name)
        {
            e.Value = row.NotesText;
            return;
        }

        if (colName == StatusColumn.Name)
        {
            e.Value = row.ExtraText;
            return;
        }

        if (colName.StartsWith(CaptureColumnPrefix, StringComparison.Ordinal))
        {
            var cell = TryGetCaptureCell(e.RowIndex, e.ColumnIndex);
            if (cell == null)
            {
                EnsureEmptyThumb();
                e.Value = _emptyThumb;
                return;
            }

            e.Value = GetThumbnailOrStartLoad(cell, e.RowIndex, e.ColumnIndex);
        }
    }

    private void QueueGrid_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
    {
        if (_syncingSelection
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || QueueGrid.Columns[e.ColumnIndex].Name != SelectColumn.Name)
        {
            return;
        }

        // Keyboard / accessibility toggle of the virtual checkbox.
        if (_checkboxToggleRow >= 0)
        {
            return;
        }

        var want = e.Value is bool b && b;
        _syncingSelection = true;
        try
        {
            QueueGrid.Rows[e.RowIndex].Selected = want;
        }
        finally
        {
            _syncingSelection = false;
        }

        QueueGrid.InvalidateCell(SelectColumn.Index, e.RowIndex);
        UpdateDeleteEnabled();
    }

    private void QueueGrid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var cell = TryGetCaptureCell(e.RowIndex, e.ColumnIndex);
        if (cell == null)
        {
            return;
        }

        var statusLabel = cell.Status == SpoolUploadStatus.Uploaded
            ? "Uploaded (cached locally)"
            : "Pending upload";
        e.ToolTipText = cell.KindText + " · " + statusLabel;
    }

    private static Image? MakeThumbnail(byte[]? bytes, int maxW, int maxH, SpoolUploadStatus status)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var src = Image.FromStream(ms))
            {
                var sx = (double)maxW / Math.Max(1, src.Width);
                var sy = (double)maxH / Math.Max(1, src.Height);
                var scale = Math.Min(1.0, Math.Min(sx, sy));
                var w = Math.Max(1, (int)Math.Round(src.Width * scale));
                var h = Math.Max(1, (int)Math.Round(src.Height * scale));

                var bmp = new Bitmap(maxW, maxH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(40, 40, 40));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var x = (maxW - w) / 2;
                    var y = (maxH - h) / 2;
                    g.DrawImage(src, x, y, w, h);

                    using (var pen = new Pen(Color.FromArgb(80, 80, 80)))
                    {
                        g.DrawRectangle(pen, 0, 0, maxW - 1, maxH - 1);
                    }

                    // Status badge (Linux: green uploaded / orange pending).
                    var badge = status == SpoolUploadStatus.Uploaded
                        ? Color.MediumSeaGreen
                        : Color.Orange;
                    using (var brush = new SolidBrush(badge))
                    {
                        g.FillEllipse(brush, maxW - 12, 2, 10, 10);
                    }
                }
                return bmp;
            }
        }
        catch
        {
            return null;
        }
    }

    private static DateTime LocalDateOf(DateTimeOffset value) => value.ToLocalTime().Date;

    private static DateTimeOffset TruncateToSecondUtc(DateTimeOffset value)
    {
        var u = value.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, u.Minute, u.Second, TimeSpan.Zero);
    }

    private static ColumnDef ResolveColumn(ScreenshotUploadMetadata? meta, string? fileName)
    {
        if (meta != null)
        {
            var hasWindow = !string.IsNullOrWhiteSpace(meta.WindowTitle) || !string.IsNullOrWhiteSpace(meta.ProgramName);
            if (hasWindow)
            {
                return new ColumnDef { Key = "window", Header = "Window" };
            }

            var mon = meta.MonitorName?.Trim();
            if (string.Equals(mon, "desktop", StringComparison.OrdinalIgnoreCase))
            {
                return new ColumnDef { Key = "desktop", Header = "Desktop" };
            }

            if (string.Equals(mon, "window", StringComparison.OrdinalIgnoreCase))
            {
                return new ColumnDef { Key = "window", Header = "Window" };
            }

            var monId = meta.MonitorId?.Trim();
            if (!string.IsNullOrEmpty(monId))
            {
                return new ColumnDef { Key = "monitor:" + monId, Header = "Monitor " + monId };
            }

            if (!string.IsNullOrEmpty(mon))
            {
                return new ColumnDef { Key = "monitor:" + mon, Header = mon! };
            }
        }

        return InferColumnFromFileName(fileName);
    }

    private static ColumnDef InferColumnFromFileName(string? fileName)
    {
        var n = (fileName ?? "").ToLowerInvariant();
        if (n.StartsWith("window", StringComparison.Ordinal))
        {
            return new ColumnDef { Key = "window", Header = "Window" };
        }

        if (n.StartsWith("desktop", StringComparison.Ordinal))
        {
            return new ColumnDef { Key = "desktop", Header = "Desktop" };
        }

        if (n.StartsWith("monitor-", StringComparison.Ordinal))
        {
            var rest = n.Substring("monitor-".Length);
            var idx = rest.Split('.')[0];
            return new ColumnDef { Key = "monitor:" + idx, Header = "Monitor " + idx };
        }
        if (n.StartsWith("monitor", StringComparison.Ordinal))
        {
            return new ColumnDef { Key = "monitor", Header = "Monitor" };
        }

        return new ColumnDef { Key = "other", Header = "Screenshot" };
    }

    private static (int Group, int Index) ColumnSortKey(string key)
    {
        if (key == "window")
        {
            return (0, 0);
        }

        if (key == "desktop")
        {
            return (1, 0);
        }

        if (key.StartsWith("monitor:", StringComparison.Ordinal))
        {
            var suffix = key.Substring("monitor:".Length);
            return (2, int.TryParse(suffix, out var n) ? n : int.MaxValue);
        }
        return (3, 0);
    }

    private static string DescribeCaptureKind(ScreenshotUploadMetadata? meta, string? fileName)
    {
        if (meta == null)
        {
            return InferColumnFromFileName(fileName).Header;
        }

        var hasWindow = !string.IsNullOrWhiteSpace(meta.WindowTitle)
                        || !string.IsNullOrWhiteSpace(meta.ProgramName);
        var mon = meta.MonitorName?.Trim();
        var monId = meta.MonitorId?.Trim();

        if (hasWindow)
        {
            var app = meta.ProgramName?.Trim();
            var title = meta.WindowTitle?.Trim();
            if (!string.IsNullOrEmpty(app) && !string.IsNullOrEmpty(title))
            {
                return "Window · " + app + " — " + title;
            }

            if (!string.IsNullOrEmpty(title))
            {
                return "Window · " + title;
            }

            if (!string.IsNullOrEmpty(app))
            {
                return "Window · " + app;
            }

            return "Active window";
        }

        if (!string.IsNullOrEmpty(mon))
        {
            if (string.Equals(mon, "desktop", StringComparison.OrdinalIgnoreCase))
            {
                return "Full desktop";
            }

            if (string.Equals(mon, "window", StringComparison.OrdinalIgnoreCase))
            {
                return "Active window";
            }

            return "Monitor · " + mon;
        }

        if (!string.IsNullOrEmpty(monId))
        {
            return "Monitor · #" + monId;
        }

        return InferColumnFromFileName(fileName).Header;
    }

    private void UpdateDateFilterLabel()
    {
        DateFilterText.Text = !_filterDate.HasValue
            ? "All dates"
            : _filterDate.Value.Date == DateTime.Now.Date
                ? "Today"
                : _filterDate.Value.ToString("yyyy-MM-dd");
        ShowAllButton.Enabled = _filterDate.HasValue;
    }

    private void UpdateDeleteEnabled()
    {
        var n = GetSelectedRowModels().Count;
        DeleteButton.Enabled = n > 0;
        EditButton.Enabled = n > 0 && _journal != null;
    }

    /// <summary>
    /// Rows selected via multi-select and/or the checkbox column (kept in sync).
    /// </summary>
    private List<RowModel> GetSelectedRowModels()
    {
        var list = new List<RowModel>();
        foreach (DataGridViewRow row in QueueGrid.SelectedRows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var model = TryGetRow(row.Index);
            if (model != null)
            {
                list.Add(model);
            }
        }
        return list;
    }

    /// <summary>
    /// Invalidates the checkbox column so virtual cells re-read <see cref="DataGridViewRow.Selected"/>.
    /// </summary>
    private void ApplyCheckboxesFromRowSelection()
    {
        if (SelectColumn.Index >= 0)
        {
            QueueGrid.InvalidateColumn(SelectColumn.Index);
        }
    }

    /// <summary>
    /// Mirrors multi-select into the checkbox column (virtual: invalidate select cells).
    /// </summary>
    private void SyncCheckboxesFromRowSelection()
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            ApplyCheckboxesFromRowSelection();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Checkbox click: toggle that row's membership in the selection without clearing other
    /// selected rows (the default grid click would replace the multi-selection).
    /// </summary>
    private void ApplyCheckboxToggle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= QueueGrid.Rows.Count)
        {
            return;
        }

        var snapshot = _selectionSnapshot ?? [];
        var wasSelected = snapshot.Contains(rowIndex);
        var nowSelected = !wasSelected;

        _syncingSelection = true;
        try
        {
            QueueGrid.ClearSelection();
            foreach (var i in snapshot)
            {
                if (i == rowIndex || i < 0 || i >= QueueGrid.Rows.Count)
                {
                    continue;
                }

                QueueGrid.Rows[i].Selected = true;
            }

            if (nowSelected)
            {
                QueueGrid.Rows[rowIndex].Selected = true;
            }

            ApplyCheckboxesFromRowSelection();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Selects or clears every data row and mirrors that in the checkbox column.</summary>
    private void SetAllRowsSelected(bool selected)
    {
        _syncingSelection = true;
        try
        {
            if (!selected)
            {
                QueueGrid.ClearSelection();
            }
            else
            {
                for (var i = 0; i < QueueGrid.RowCount; i++)
                {
                    QueueGrid.Rows[i].Selected = true;
                }
            }

            ApplyCheckboxesFromRowSelection();
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateDeleteEnabled();
    }

    private void OlderButton_Click(object sender, EventArgs e)
    {
        _filterDate = (_filterDate ?? DateTime.Today).AddDays(-1);
        UpdateDateFilterLabel();
        Reload();
    }

    private void NewerButton_Click(object sender, EventArgs e)
    {
        _filterDate = (_filterDate ?? DateTime.Today).AddDays(1);
        UpdateDateFilterLabel();
        Reload();
    }

    private void ShowAllButton_Click(object sender, EventArgs e)
    {
        _filterDate = null;
        UpdateDateFilterLabel();
        Reload();
    }

    private void RefreshButton_Click(object sender, EventArgs e) => Reload();

    private void SelectAllButton_Click(object sender, EventArgs e) => SetAllRowsSelected(true);

    private void SelectNoneButton_Click(object sender, EventArgs e) => SetAllRowsSelected(false);

    private void EditButton_Click(object sender, EventArgs e)
    {
        if (_journal == null)
        {
            return;
        }

        var selected = GetSelectedRowModels();
        if (selected.Count == 0)
        {
            return;
        }

        var ids = selected.SelectMany(r => r.Ids).Distinct().ToList();
        using (var f = new ScreenshotEditForm(_capture, _journal, ids))
        {
            f.ShowDialog(this);
        }

        Reload();
    }

    private void DeleteButton_Click(object sender, EventArgs e)
    {
        var selected = GetSelectedRowModels();
        if (selected.Count == 0)
        {
            return;
        }

        var ids = selected.SelectMany(r => r.Ids).ToList();
        if (MessageBox.Show(this,
                "Delete " + ids.Count + " local spool item(s) in " + selected.Count + " batch row(s)?",
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var n = _capture.Spool.DeleteItems(ids);
            if (n < ids.Count)
            {
                SummaryText.Text = "Deleted " + n + " of " + ids.Count + " item(s); some could not be removed.";
            }

            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void PushButton_Click(object sender, EventArgs e)
    {
        PushButton.Enabled = false;
        try
        {
            var (ok, fail) = await _capture.PushSpoolAsync();
            SummaryText.Text = "Push done: ok=" + ok + " fail=" + fail;
            Reload();
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Push failed: " + ex.Message;
        }
        finally
        {
            if (!IsDisposed)
            {
                PushButton.Enabled = true;
            }
        }
    }

    private void PreviewButton_Click(object sender, EventArgs e) => PreviewFromCurrentCellOrRow();

    private void QueueGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        PreviewFromCell(e.RowIndex, e.ColumnIndex);
    }

    private void QueueGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
    {
        if (QueueGrid.IsCurrentCellDirty && QueueGrid.CurrentCell is DataGridViewCheckBoxCell)
        {
            QueueGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void QueueGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
    {
        // Snapshot selection before the grid replaces it on click (needed for checkbox toggles).
        _selectionSnapshot = [];
        foreach (DataGridViewRow row in QueueGrid.SelectedRows)
        {
            _selectionSnapshot.Add(row.Index);
        }

        _checkboxToggleRow = e.RowIndex >= 0 && e.ColumnIndex >= 0
                             && QueueGrid.Columns[e.ColumnIndex].Name == SelectColumn.Name
            ? e.RowIndex
            : -1;
    }

    private void QueueGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
    {
        if (_syncingSelection
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || QueueGrid.Columns[e.ColumnIndex].Name != SelectColumn.Name)
        {
            return;
        }

        // Checkbox click: toggle this row in/out of the selection; keep other selected rows.
        ApplyCheckboxToggle(e.RowIndex);
        _checkboxToggleRow = -1;
        UpdateDeleteEnabled();
    }

    private void QueueGrid_SelectionChanged(object sender, EventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        // Skip while a checkbox click is in progress — CellContentClick finishes the sync.
        if (_checkboxToggleRow >= 0)
        {
            return;
        }

        // Row multi-select (click / Ctrl / Shift) → mirror into the checkbox column.
        SyncCheckboxesFromRowSelection();
        UpdateDeleteEnabled();
    }

    private void PreviewFromCurrentCellOrRow()
    {
        if (QueueGrid.CurrentCell == null)
        {
            return;
        }

        PreviewFromCell(QueueGrid.CurrentCell.RowIndex, QueueGrid.CurrentCell.ColumnIndex);
    }

    private void PreviewFromCell(int rowIndex, int columnIndex)
    {
        var row = TryGetRow(rowIndex);
        if (row == null)
        {
            return;
        }

        var cell = TryGetCaptureCell(rowIndex, columnIndex);

        // If double-clicked outside an image cell, pick the first image in the batch.
        if (cell == null)
        {
            foreach (var c in row.Cells)
            {
                if (c != null)
                {
                    cell = c;
                    break;
                }
            }
        }

        if (cell == null)
        {
            return;
        }

        ShowPreview(cell.Id, row.TimestampText, cell.KindText, row.JournalText, row.NotesText, row.ExtraText);
    }

    private void ShowPreview(
        Uuid7 id, string timestampText, string kindText,
        string journalText, string notesText, string extraText)
    {
        try
        {
            var bytes = _capture.Spool.ReadImageBytes(id);
            if (bytes == null || bytes.Length == 0)
            {
                MessageBox.Show(this, "Could not load image for preview.", "Preview",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var detail = kindText + Environment.NewLine
                         + "Journal: " + journalText
                         + (string.IsNullOrEmpty(notesText) ? "" : Environment.NewLine + "Notes: " + notesText)
                         + (string.IsNullOrEmpty(extraText) ? "" : Environment.NewLine + extraText);
            ImagePreviewForm.ShowPreview(
                this, bytes, timestampText, detail,
                _capture, _journal, id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Preview failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseButton_Click(object sender, EventArgs e) => Close();
}
