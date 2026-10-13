namespace Schnapp.Client.Linux.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Spool;
using Schnapp.Client.Linux.Services;
using Schnapp.Contracts;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Medo;

/// <summary>
/// Offline screenshot spool browser. Each row is one capture "batch" (everything captured
/// at the same instant, e.g. all monitors + window in one tick); each column is a distinct
/// thing being captured (a monitor, the active window, the full desktop). Rows — not
/// individual items — are the unit of selection and deletion.
/// </summary>
/// <remarks>
/// Opened from the tray when the queue is non-empty. Uses <see cref="CaptureService.Spool"/>.
/// </remarks>
public partial class OfflineQueueWindow : Window
{
    private readonly CaptureService _capture;
    private readonly JournalSessionService? _journal;
    private readonly ClientSettingsStore _store = new();
    private readonly ObservableCollection<QueueRow> _rows = [];
    private Dictionary<Uuid7, string> _journalLabels = [];
    private CancellationTokenSource? _reloadCts;
    private DateOnly? _filterDate;

    /// <summary>
    /// True while programmatically aligning checkboxes with DataGrid selection (or vice versa).
    /// </summary>
    private bool _syncingSelection;

    /// <summary>XAML / designer constructor.</summary>
    public OfflineQueueWindow() : this(null!)
    {
    }

    /// <summary>
    /// Creates the queue window bound to the running capture service's spool.
    /// </summary>
    /// <param name="capture">Live capture service (shares spool with the tray).</param>
    /// <param name="journal">Optional journal session for the batch screenshot editor.</param>
    public OfflineQueueWindow(CaptureService capture, JournalSessionService? journal = null)
    {
        _capture = capture;
        _journal = journal;
        InitializeComponent();
        QueueGrid.ItemsSource = _rows;
        QueueGrid.SelectionChanged += OnGridSelectionChanged;
        if (capture is null)
        {
            return;
        }

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        var settings = _store.Load();
        WindowPlacement.RestoreOrPlaceNearTray(this, settings.OfflineQueueWindow);
        _filterDate = DateOnly.FromDateTime(DateTime.Now);
        UpdateDateFilterLabel();
        await LoadJournalLabelsAsync();
        Reload();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            var settings = _store.Load();
            settings.OfflineQueueWindow = WindowPlacement.Capture(this);
            _store.Save(settings);
        }
        catch
        {
            // ignore save failures
        }
    }

    private async Task LoadJournalLabelsAsync()
    {
        try
        {
            // Prefer local reference cache for C/P/T names; refresh targets/journals from API when online.
            var cache = _capture.Uploads.ReferenceData;
            var customers = cache.GetCustomers();
            var projects = cache.GetProjects();
            var tasks = cache.GetTasks();
            var targets = cache.GetJournalTargets();

            IReadOnlyList<JournalEntryResponse> journals = Array.Empty<JournalEntryResponse>();
            var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var settings = _store.Load();
                    using var api = new SchnappApiClient(settings.ApiBaseUrl, token);
                    journals = await api.ListJournalsAsync();
                    try
                    {
                        var freshTargets = await api.ListJournalTargetsAsync();
                        if (freshTargets.Count > 0)
                        {
                            targets = [.. freshTargets];
                        }
                    }
                    catch
                    {
                        // keep cache
                    }
                }
                catch
                {
                    // offline / API down — labels stay empty unless we already had data
                }
            }

            var map = new Dictionary<Uuid7, string>();
            foreach (var j in journals)
            {
                var target = targets.FirstOrDefault(t => t.Id == j.JournalTargetId);
                map[j.Id] = FormatJournalDescription(target, customers, projects, tasks, j.Notes, j.IsOpen);
            }

            _journalLabels = map;
        }
        catch
        {
            _journalLabels = [];
        }
    }

    /// <summary>
    /// Human journal label: target name or Customer/Project/Task, open/closed, optional notes.
    /// </summary>
    private static string FormatJournalDescription(
        JournalTargetResponse? target,
        List<ListItemResponse> customers,
        List<ListItemResponse> projects,
        List<ListItemResponse> tasks,
        string? notes,
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
            targetText = $"{c} / {p} / {t}";
        }

        var open = isOpen ? "open" : "closed";
        if (!string.IsNullOrWhiteSpace(notes))
        {
            return $"{targetText} ({open}) — {notes.Trim()}";
        }

        return $"{targetText} ({open})";
    }

    private void Reload()
    {
        ClearError();
        _reloadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _reloadCts = cts;
        _ = ReloadAsync(cts.Token);
    }

    /// <summary>
    /// Builds the row/column view model off the UI thread (SQLite/file I/O + thumbnail
    /// decoding can be slow with a large recent-cache), then applies it on the UI thread.
    /// A newer <see cref="Reload"/> call cancels any in-flight one via <see cref="_reloadCts"/>.
    /// </summary>
    private async Task ReloadAsync(CancellationToken ct)
    {
        SetLoading(true);
        try
        {
            var model = await Task.Run(() => BuildViewModel(ct), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            ApplyViewModel(model);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer Reload() call
        }
        catch (Exception ex)
        {
            AppLog.Error("OfflineQueueWindow.Reload failed: " + ex);
            ShowError("Failed to load offline queue: " + ex.Message);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                SetLoading(false);
            }
        }
    }

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            SummaryText.Text = "Loading…";
        }

        SelectAllButton.IsEnabled = !loading;
        SelectNoneButton.IsEnabled = !loading;
        RefreshButton.IsEnabled = !loading;
        PushButton.IsEnabled = !loading;
        OlderButton.IsEnabled = !loading;
        NewerButton.IsEnabled = !loading;
        ShowAllButton.IsEnabled = !loading && _filterDate.HasValue;
        if (loading)
        {
            EditButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
        }
        else
        {
            UpdateDeleteEnabled();
        }
    }

    /// <summary>One computed reload result, built off the UI thread by <see cref="BuildViewModel"/>.</summary>
    private sealed record ViewModel(IReadOnlyList<QueueColumnDef> Columns, List<QueueRow> Rows, string SummaryText);

    /// <summary>
    /// Reads the spool, applies the current date filter, groups into batches, decodes
    /// thumbnails, and builds columns/rows. Touches only <see cref="_capture"/> (I/O) and
    /// <see cref="_journalLabels"/> (read-only snapshot) — safe to run off the UI thread.
    /// </summary>
    private ViewModel BuildViewModel(CancellationToken ct)
    {
        _capture.Spool.PruneExpired();
        var pending = _capture.Spool.ListItems();
        var uploaded = _capture.Spool.ListUploadedItems();
        var status = _capture.Spool.GetStatus();

        var loaded = pending.Concat(uploaded)
            .Select(item => (Item: item, Meta: _capture.Spool.ReadUploadMetadata(item.Id)))
            .Select(x => (x.Item, x.Meta, Col: ResolveColumn(x.Meta, x.Item.FileName)))
            .ToList();

        if (_filterDate is { } day)
        {
            loaded = [.. loaded.Where(x => LocalDateOf(x.Meta?.Timestamp ?? x.Item.EnqueuedUtc) == day)];
        }

        ct.ThrowIfCancellationRequested();

        var summary = pending.Count + uploaded.Count == 0
            ? "Nothing here yet — pending uploads and recently-uploaded screenshots will show up here."
            : loaded.Count == 0
                ? $"No items on {_filterDate:yyyy-MM-dd}. {pending.Count} pending / {uploaded.Count} cached in total."
                : $"{pending.Count} pending ({status.TotalBytes:N0} bytes) · "
                  + $"{uploaded.Count} cached ({status.UploadedBytes:N0} bytes) · {status.RootPath}";

        // Discover the distinct set of "things captured" across the (filtered) queue, in a
        // stable, sensible order (window, desktop, monitors in numeric order, then other).
        var columns = loaded
            .Select(x => x.Col)
            .GroupBy(c => c.Key)
            .Select(g => g.First())
            .OrderBy(c => ColumnSortKey(c.Key))
            .ToList();
        var columnIndex = columns
            .Select((c, i) => (c.Key, i))
            .ToDictionary(t => t.Key, t => t.i);

        // Group into batches: everything captured at (about) the same instant.
        var groups = loaded
            .GroupBy(x => TruncateToSecondUtc(x.Meta?.Timestamp ?? x.Item.EnqueuedUtc))
            .OrderBy(g => g.Key);

        var rows = new List<QueueRow>();
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var cells = new QueueCell?[columns.Count];
            string? journalText = null;
            var attemptsTotal = 0;
            string? firstError = null;
            var count = 0;
            var uploadedCount = 0;

            foreach (var (item, meta, col) in group)
            {
                count++;
                if (item.UploadStatus == SpoolUploadStatus.Uploaded)
                {
                    uploadedCount++;
                }

                var idx = columnIndex[col.Key];
                cells[idx] = new QueueCell(_capture.Spool.ReadImageBytes)
                {
                    Id = item.Id,
                    KindText = DescribeCaptureKind(meta, item.FileName),
                    Status = item.UploadStatus
                };

                if (journalText is null)
                {
                    var jid = meta?.JournalEntryId;
                    journalText = jid is null || jid == Uuid7.Empty
                        ? "(none)"
                        : _journalLabels.TryGetValue(jid.Value, out var label) && !string.IsNullOrEmpty(label)
                            ? label
                            : "(unknown journal)";
                }

                attemptsTotal += item.Attempts;
                if (firstError is null && !string.IsNullOrEmpty(item.LastError))
                {
                    firstError = item.LastError;
                }
            }

            var statusText = uploadedCount == count ? "Uploaded"
                : uploadedCount == 0 ? "Pending"
                : $"Pending ({count - uploadedCount}/{count})";
            var extra = attemptsTotal > 0 || firstError != null
                ? $"{statusText} · {count} item(s) · attempts {attemptsTotal}" + (firstError != null ? " · " + firstError : "")
                : $"{statusText} · {count} item(s)";

            rows.Add(new QueueRow
            {
                Timestamp = group.Key,
                TimestampText = group.Key.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                JournalText = journalText ?? "(none)",
                ExtraText = extra,
                Cells = cells
            });
        }

        return new ViewModel(columns, rows, summary);
    }

    /// <summary>Applies a background-built <see cref="ViewModel"/> on the UI thread.</summary>
    private void ApplyViewModel(ViewModel model)
    {
        foreach (var row in _rows)
        {
            row.Dispose();
        }

        _rows.Clear();

        // Build the grid's columns *before* adding any rows: the DataGrid materializes
        // row/cell visuals as items are added, so columns must already exist or rows
        // render with zero cells (the whole grid ends up looking blank).
        RebuildColumns(model.Columns);

        foreach (var row in model.Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
            _rows.Add(row);
        }

        SummaryText.Text = model.SummaryText;
        UpdateDeleteEnabled();
    }

    private static DateOnly LocalDateOf(DateTimeOffset value) => DateOnly.FromDateTime(value.ToLocalTime().DateTime);

    private void OnOlderDay(object? sender, RoutedEventArgs e)
    {
        _filterDate = (_filterDate ?? DateOnly.FromDateTime(DateTime.Now)).AddDays(-1);
        UpdateDateFilterLabel();
        Reload();
    }

    private void OnNewerDay(object? sender, RoutedEventArgs e)
    {
        _filterDate = (_filterDate ?? DateOnly.FromDateTime(DateTime.Now)).AddDays(1);
        UpdateDateFilterLabel();
        Reload();
    }

    private void OnShowAll(object? sender, RoutedEventArgs e)
    {
        _filterDate = null;
        UpdateDateFilterLabel();
        Reload();
    }

    private void UpdateDateFilterLabel()
    {
        DateFilterText.Text = _filterDate is not { } date
            ? "All dates"
            : date == DateOnly.FromDateTime(DateTime.Now)
                ? "Today"
                : date.ToString("yyyy-MM-dd");
        ShowAllButton.IsEnabled = _filterDate.HasValue;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QueueRow.IsSelected) || sender is not QueueRow row)
        {
            return;
        }

        // Checkbox toggled → add/remove this row in the grid multi-selection.
        if (!_syncingSelection)
        {
            _syncingSelection = true;
            try
            {
                if (row.IsSelected)
                {
                    if (!QueueGrid.SelectedItems.Contains(row))
                    {
                        QueueGrid.SelectedItems.Add(row);
                    }
                }
                else
                {
                    QueueGrid.SelectedItems.Remove(row);
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        UpdateDeleteEnabled();
    }

    /// <summary>
    /// Grid multi-select (click / Ctrl / Shift) → mirror into each row's checkbox (<see cref="QueueRow.IsSelected"/>).
    /// </summary>
    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            var selected = new HashSet<QueueRow>(QueueGrid.SelectedItems.OfType<QueueRow>());
            foreach (var row in _rows)
            {
                row.IsSelected = selected.Contains(row);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateDeleteEnabled();
    }

    /// <summary>
    /// Rebuilds the grid's columns: a fixed set (select/time/journal/status) followed by
    /// one column per distinct "thing captured" discovered in the current queue contents.
    /// </summary>
    private void RebuildColumns(IReadOnlyList<QueueColumnDef> columns)
    {
        QueueGrid.Columns.Clear();

        QueueGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(32),
            CanUserSort = false,
            CellTemplate = new FuncDataTemplate<QueueRow>((row, _) => BuildSelectCheckbox(row), supportsRecycling: true)
        });

        QueueGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Time",
            Binding = new Binding(nameof(QueueRow.TimestampText)),
            Width = new DataGridLength(150, DataGridLengthUnitType.SizeToCells),
            // Display the formatted text but sort by the real DateTimeOffset, not the string.
            SortMemberPath = nameof(QueueRow.Timestamp)
        });


        for (var i = 0; i < columns.Count; i++)
        {
            var index = i;
            QueueGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = columns[i].Header,
                Width = new DataGridLength(110),
                CanUserSort = false,
                // supportsRecycling: false — BuildCellVisual bakes the thumbnail/id/tooltip into
                // the control via closures rather than live bindings. With recycling enabled,
                // Avalonia reuses the *same* Control for a different row (only swapping
                // DataContext) instead of calling the build delegate again, so a recycled cell
                // keeps showing whatever row it was originally built for — wrong images after
                // sorting/scrolling, or on initial load once containers get reused. Rebuilding
                // each cell is cheap now that thumbnails decode lazily and are cached per-cell.
                CellTemplate = new FuncDataTemplate<QueueRow>((row, _) => BuildCellVisual(row, index), supportsRecycling: false)
            });
        }

        QueueGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Journal",
            Binding = new Binding(nameof(QueueRow.JournalText)),
            Width = new DataGridLength(160, DataGridLengthUnitType.SizeToCells),
            MinWidth = 120
        });

        QueueGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(QueueRow.ExtraText)),
            Width = new DataGridLength(180, DataGridLengthUnitType.SizeToCells)
        });

    }

    private static CheckBox BuildSelectCheckbox(QueueRow row)
    {
        var cb = new CheckBox { Margin = new Thickness(4, 0, 0, 0), Focusable = false };
        cb.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(QueueRow.IsSelected)) { Mode = BindingMode.TwoWay });
        // Because the DataGrid would otherwise treat the click as a row selection and clear
        // multi-select; the checkbox still toggles first, then we stop the event from bubbling.
        cb.PointerPressed += (_, e) => e.Handled = true;
        return cb;
    }

    /// <summary>Builds one grid cell: a thumbnail if this row has an item for the column, else empty.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "<Pending>")]
    private Control BuildCellVisual(QueueRow row, int index)
    {
        if (index >= row.Cells.Length || row.Cells[index] is not { } cell)
        {
            return new Border();
        }

        var badge = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = cell.Status == SpoolUploadStatus.Uploaded ? Brushes.MediumSeaGreen : Brushes.Orange,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0)
        };

        var border = new Border
        {
            Width = 96,
            Height = 64,
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new Grid
            {
                Children =
                {
                    new Image { Source = cell.GetThumbnail(), Stretch = Stretch.UniformToFill },
                    badge
                }
            }
        };
        var statusLabel = cell.Status == SpoolUploadStatus.Uploaded ? "Uploaded (cached locally)" : "Pending upload";
        ToolTip.SetTip(border, cell.KindText + " · " + statusLabel);

        var id = cell.Id;
        var kind = cell.KindText;
        border.DoubleTapped += async (_, _) => await ShowPreviewAsync(id, row.TimestampText, kind, row.JournalText, row.ExtraText);
        return border;
    }

    private async Task ShowPreviewAsync(Uuid7 id, string timestampText, string kindText, string journalText, string extraText)
    {
        var bytes = _capture.Spool.ReadImageBytes(id);
        if (bytes == null || bytes.Length == 0)
        {
            ShowError("Could not load image for preview.");
            return;
        }

        var detail = kindText + "\nJournal: " + journalText + (string.IsNullOrEmpty(extraText) ? "" : "\n" + extraText);
        await ImagePreviewWindow.ShowPreviewAsync(this, bytes, timestampText, detail);
    }

    /// <summary>
    /// Stable column identity ("window", "desktop", "monitor:{id}") plus a friendly header,
    /// for what was captured (monitor / desktop / window / app).
    /// </summary>
    private static QueueColumnDef ResolveColumn(ScreenshotUploadMetadata? meta, string? fileName)
    {
        if (meta != null)
        {
            var hasWindow = !string.IsNullOrWhiteSpace(meta.WindowTitle) || !string.IsNullOrWhiteSpace(meta.ProgramName);
            if (hasWindow)
            {
                return new QueueColumnDef("window", "Window");
            }

            var mon = meta.MonitorName?.Trim();
            if (string.Equals(mon, "desktop", StringComparison.OrdinalIgnoreCase))
            {
                return new QueueColumnDef("desktop", "Desktop");
            }

            if (string.Equals(mon, "window", StringComparison.OrdinalIgnoreCase))
            {
                return new QueueColumnDef("window", "Window");
            }

            var monId = meta.MonitorId?.Trim();
            if (!string.IsNullOrEmpty(monId))
            {
                return new QueueColumnDef("monitor:" + monId, "Monitor " + monId);
            }

            if (!string.IsNullOrEmpty(mon))
            {
                return new QueueColumnDef("monitor:" + mon, mon);
            }
        }

        return InferColumnFromFileName(fileName);
    }

    private static QueueColumnDef InferColumnFromFileName(string? fileName)
    {
        var n = fileName?.ToLowerInvariant() ?? "";
        if (n.StartsWith("window", StringComparison.Ordinal))
        {
            return new QueueColumnDef("window", "Window");
        }

        if (n.StartsWith("desktop", StringComparison.Ordinal))
        {
            return new QueueColumnDef("desktop", "Desktop");
        }

        if (n.StartsWith("monitor-", StringComparison.Ordinal))
        {
            var idx = n["monitor-".Length..].Split('.')[0];
            return new QueueColumnDef("monitor:" + idx, "Monitor " + idx);
        }
        if (n.StartsWith("monitor", StringComparison.Ordinal))
        {
            return new QueueColumnDef("monitor", "Monitor");
        }

        return new QueueColumnDef("other", "Screenshot");
    }

    /// <summary>Sort order: Window, Desktop, Monitor N (numeric), then anything else.</summary>
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
            var suffix = key["monitor:".Length..];
            return (2, int.TryParse(suffix, out var n) ? n : int.MaxValue);
        }
        return (3, 0);
    }

    /// <summary>Drops sub-second precision (in UTC), so items from the same capture tick group together.</summary>
    private static DateTimeOffset TruncateToSecondUtc(DateTimeOffset value)
    {
        var u = value.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, u.Minute, u.Second, TimeSpan.Zero);
    }

    /// <summary>
    /// Human label for what was captured (monitor / desktop / window / app).
    /// </summary>
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
                return $"Window · {app} — {title}";
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
            if (string.Equals(mon, "desktop", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mon, "window", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(mon, "desktop", StringComparison.OrdinalIgnoreCase))
                {
                    return "Full desktop";
                }

                return "Active window";
            }

            if (mon.StartsWith("Monitor", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(monId))
            {
                return "Monitor · " + mon;
            }

            return mon;
        }

        if (!string.IsNullOrEmpty(monId))
        {
            return "Monitor · #" + monId;
        }

        return InferColumnFromFileName(fileName).Header;
    }

    private void UpdateDeleteEnabled()
    {
        var any = _rows.Any(r => r.IsSelected);
        DeleteButton.IsEnabled = any;
        EditButton.IsEnabled = any && _journal is not null;
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (_journal is null)
        {
            return;
        }

        var selectedRows = _rows.Where(r => r.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        var ids = selectedRows
            .SelectMany(r => r.Cells)
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var w = new ScreenshotEditWindow(_capture, _journal, ids);
        await w.ShowDialog(this);
        Reload();
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        _syncingSelection = true;
        try
        {
            QueueGrid.SelectAll();
            foreach (var row in _rows)
            {
                row.IsSelected = true;
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateDeleteEnabled();
    }

    private void OnSelectNone(object? sender, RoutedEventArgs e)
    {
        _syncingSelection = true;
        try
        {
            QueueGrid.SelectedItems.Clear();
            foreach (var row in _rows)
            {
                row.IsSelected = false;
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateDeleteEnabled();
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Reload();

    private async void OnPush(object? sender, RoutedEventArgs e)
    {
        ClearError();
        try
        {
            await _capture.PushSpoolAsync();
            Reload();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        ClearError();
        var selectedRows = _rows.Where(r => r.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        var ids = selectedRows
            .SelectMany(r => r.Cells)
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
        var n = _capture.Spool.DeleteItems(ids);
        Reload();
        if (n < ids.Count)
        {
            ShowError($"Deleted {n} of {ids.Count} item(s); some could not be removed.");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.IsVisible = true;
    }

    private void ClearError()
    {
        ErrorText.IsVisible = false;
        ErrorText.Text = "";
    }

    /// <summary>Identity + display header for one distinct "thing captured" (a grid column).</summary>
    private sealed record QueueColumnDef(string Key, string Header);

    /// <summary>One captured image within a batch row (one grid cell).</summary>
    internal sealed class QueueCell : IDisposable
    {
        private readonly Func<Uuid7, byte[]?> _readImageBytes;
        private Bitmap? _thumbnail;
        private bool _thumbnailLoaded;

        public QueueCell(Func<Uuid7, byte[]?> readImageBytes) => _readImageBytes = readImageBytes;

        public Uuid7 Id { get; init; }
        public string KindText { get; init; } = "";
        public SpoolUploadStatus Status { get; init; }

        /// <summary>
        /// Decodes the thumbnail on first use (and caches it) rather than eagerly for every
        /// item during reload — the DataGrid only ever renders the handful of visible rows,
        /// so decoding hundreds of images up front was the main cause of slow loads.
        /// </summary>
        public Bitmap? GetThumbnail()
        {
            if (_thumbnailLoaded)
            {
                return _thumbnail;
            }

            _thumbnailLoaded = true;
            try
            {
                var bytes = _readImageBytes(Id);
                if (bytes is { Length: > 0 })
                {
                    using var ms = new MemoryStream(bytes);
                    _thumbnail = new Bitmap(ms);
                }
            }
            catch
            {
                // leave null; cell renders blank
            }

            return _thumbnail;
        }

        public void Dispose() => _thumbnail?.Dispose();
    }

    /// <summary>One row in the offline queue grid: a capture batch (same-instant items).</summary>
    public sealed class QueueRow : IDisposable, INotifyPropertyChanged
    {
        private bool _isSelected;

        public DateTimeOffset Timestamp { get; init; }
        public string TimestampText { get; init; } = "";
        public string JournalText { get; init; } = "";
        public string ExtraText { get; init; } = "";

        /// <summary>Cells aligned to the grid's dynamic capture-kind columns; null = not captured in this batch.</summary>
        internal QueueCell?[] Cells { get; init; } = Array.Empty<QueueCell?>();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Dispose()
        {
            foreach (var cell in Cells)
            {
                cell?.Dispose();
            }
        }
    }
}
