namespace Schnapp.Client.Core.Spool;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Ids;
using Schnapp.Contracts;

using Medo;

/// <summary>
/// Durable filesystem queue + recent-screenshots cache. Capture always <see cref="Enqueue"/>s;
/// a background <see cref="PushAsync"/> (with <c>retainUploaded: true</c>) uploads pending items
/// and keeps them as cache. Each item's <see cref="SpoolItem.Id"/> is sent as
/// <see cref="ScreenshotUploadMetadata.Id"/> so the server can use a stable client id
/// (idempotent retries). <see cref="SpoolItem.ServerScreenshotId"/> records the id the server
/// returned (usually the same; may differ if the server reassigned on conflict).
/// </summary>
/// <remarks>
/// Layout under <c>{data}/spool/{id}/</c> (default <c>~/.local/share/schnapp/spool</c>):
/// <list type="bullet">
/// <item><description><c>meta.json</c> — spool queue bookkeeping (<see cref="SpoolItem"/>)</description></item>
/// <item><description><c>upload.json</c> — <see cref="ScreenshotUploadMetadata"/> for the API</description></item>
/// <item><description><c>image.bin</c> — raw image bytes</description></item>
/// </list>
/// A SQLite index (<c>{root}/index.db</c>, see <see cref="SpoolIndex"/>) caches item metadata so
/// listing/status/pruning don't need to enumerate and JSON-parse every folder on every call; the
/// filesystem remains the source of truth and the index is reconciled against it on construction
/// (self-healing: covers first-run migration from older versions, and crash recovery).
/// Legacy items may still embed upload metadata inside <c>meta.json</c> as <c>metadataJson</c>;
/// those are read and rewritten on the next successful meta write path when loaded.
/// Caps (max items, max total bytes, max age) apply only to not-yet-uploaded (pending) items —
/// dropping a pending item loses the only copy of that screenshot, so these never touch
/// already-uploaded (cached) items. See <see cref="ScreenshotCacheHousekeeping"/> for the
/// separate size-based eviction policy that governs already-uploaded items. Used by CLI and tray.
/// </remarks>
public sealed class ScreenshotSpool : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    }.AddUuid7Converters();

    private readonly string _root;
    private readonly int _maxItems;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;
    private readonly SpoolIndex _index;

    /// <summary>
    /// Creates a spool under the user data directory (or an explicit root for tests).
    /// </summary>
    /// <param name="rootDirectory">Optional override root; default via <see cref="ClientPaths.GetSpoolDirectory"/>.</param>
    /// <param name="maxItems">Max queued (pending) items (default 2000).</param>
    /// <param name="maxBytes">Max total image bytes for pending items (default 2 GiB).</param>
    /// <param name="maxAge">Drop pending items older than this (default 14 days).</param>
    /// <param name="settingsSpoolDirectory">Optional path from <c>ClientSettings.SpoolDirectory</c>.</param>
    /// <param name="databasePath">
    /// Optional override for the SQLite index db path. Defaults to <c>{root}/index.db</c> for
    /// test isolation; production callers pass <see cref="ClientPaths.GetDatabasePath"/> so this
    /// index shares the same unified <c>schnapp.db</c> file as <see cref="Upload.ReferenceDataCache"/>.
    /// </param>
    public ScreenshotSpool(
        string? rootDirectory = null,
        int maxItems = 2000,
        long maxBytes = 2L * 1024 * 1024 * 1024,
        TimeSpan? maxAge = null,
        string? settingsSpoolDirectory = null,
        string? databasePath = null)
    {
        _root = rootDirectory ?? ClientPaths.GetSpoolDirectory(settingsSpoolDirectory);
        Directory.CreateDirectory(_root);
        _maxItems = maxItems > 0 ? maxItems : 2000;
        _maxBytes = maxBytes > 0 ? maxBytes : 2L * 1024 * 1024 * 1024;
        _maxAge = maxAge ?? TimeSpan.FromDays(14);

        _index = new SpoolIndex(databasePath ?? Path.Combine(_root, "index.db"));
        ReconcileIndexWithFilesystem();
    }

    /// <summary>Spool root path.</summary>
    public string RootPath => _root;

    /// <summary>
    /// Always-enqueue capture path: writes image + metadata under a new spool id for later
    /// <see cref="PushAsync"/>. Sets <c>metadata.Id</c> to that spool id (client screenshot id).
    /// </summary>
    /// <param name="imageBytes">Raw image content.</param>
    /// <param name="fileName">Multipart file name.</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="metadata">Upload metadata (machine, timestamp, tags, …); id is overwritten.</param>
    /// <returns>The new spool item id (also the client id sent on upload).</returns>
    public Uuid7 Enqueue(byte[] imageBytes, string fileName, string contentType, ScreenshotUploadMetadata metadata)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes required.", nameof(imageBytes));
        }

        EnforceCapsBeforeAdd(imageBytes.LongLength);

        var id = IdFactory.NewId();
        var dir = ItemDir(id);
        Directory.CreateDirectory(dir);

        // Client id is the intended server screenshot id (push sends metadata.Id = this).
        var meta = metadata ?? new ScreenshotUploadMetadata();
        meta.Id = id;

        var item = new SpoolItem
        {
            Id = id,
            EnqueuedUtc = DateTimeOffset.UtcNow,
            FileName = string.IsNullOrWhiteSpace(fileName) ? "shot.bin" : fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Attempts = 0,
            ByteSize = imageBytes.LongLength
        };
        ApplyMetadata(item, meta);

        File.WriteAllBytes(ImagePath(id), imageBytes);
        WriteUploadMetadata(id, meta);
        WriteMeta(item);
        return id;
    }

    /// <summary>
    /// Always-enqueue capture path from a stream (copies to memory then disk).
    /// Same client-id behavior as the byte[] overload.
    /// </summary>
    public Uuid7 Enqueue(Stream image, string fileName, string contentType, ScreenshotUploadMetadata metadata)
    {
        using (var ms = new MemoryStream())
        {
            image.CopyTo(ms);
            return Enqueue(ms.ToArray(), fileName, contentType, metadata);
        }
    }

    /// <summary>
    /// Saves an already-uploaded screenshot as a recent-cache entry (not queued for upload).
    /// Used when a direct upload succeeds and the caller wants to keep a local copy — see
    /// <see cref="ScreenshotCacheHousekeeping"/> for the size cap that eventually evicts these.
    /// </summary>
    /// <param name="imageBytes">Raw image content already uploaded to the server.</param>
    /// <param name="fileName">Multipart file name (for parity with pending items).</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="metadata">Upload metadata that was sent to the server.</param>
    /// <param name="serverScreenshotId">The id the server assigned to this screenshot.</param>
    /// <returns>The new local cache item id.</returns>
    public Uuid7 SaveUploaded(
        byte[] imageBytes, string fileName, string contentType,
        ScreenshotUploadMetadata metadata, Uuid7 serverScreenshotId)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes required.", nameof(imageBytes));
        }

        var id = IdFactory.NewId();
        Directory.CreateDirectory(ItemDir(id));
        var now = DateTimeOffset.UtcNow;

        var item = new SpoolItem
        {
            Id = id,
            EnqueuedUtc = now,
            FileName = string.IsNullOrWhiteSpace(fileName) ? "shot.bin" : fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            ByteSize = imageBytes.LongLength,
            UploadStatus = SpoolUploadStatus.Uploaded,
            ServerScreenshotId = serverScreenshotId,
            UploadedAtUtc = now
        };
        ApplyMetadata(item, metadata);

        File.WriteAllBytes(ImagePath(id), imageBytes);
        WriteUploadMetadata(id, metadata ?? new ScreenshotUploadMetadata());
        WriteMeta(item);
        return id;
    }

    /// <summary>
    /// Returns aggregate status for pending items (unchanged meaning) plus already-uploaded
    /// (cached) counts/bytes.
    /// </summary>
    public SpoolStatus GetStatus()
    {
        PruneExpired();
        var status = _index.GetAggregateStatus();
        status.RootPath = _root;
        return status;
    }

    /// <summary>
    /// Lists pending (not yet uploaded) items ordered by enqueue time.
    /// Does not prune; call <see cref="PruneExpired"/> first if needed.
    /// </summary>
    public IReadOnlyList<SpoolItem> ListItems() => _index.ListPending();

    /// <summary>
    /// Lists already-uploaded items kept as a recent-screenshots cache, oldest-uploaded first.
    /// </summary>
    public IReadOnlyList<SpoolItem> ListUploadedItems() => _index.ListUploadedOrderedByAge();

    /// <summary>
    /// Absolute path to the image bytes for a spool item, or null if missing.
    /// </summary>
    /// <param name="id">Spool item id.</param>
    public string? GetImagePath(Uuid7 id)
    {
        var path = ImagePath(id);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Reads image bytes for a spool item, or null if missing.
    /// </summary>
    /// <param name="id">Spool item id.</param>
    public byte[]? ReadImageBytes(Uuid7 id)
    {
        var path = ImagePath(id);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Loads queue bookkeeping for one item from <c>meta.json</c>, or null if missing.
    /// </summary>
    /// <param name="id">Spool item id.</param>
    public SpoolItem? GetItem(Uuid7 id)
    {
        var path = MetaPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SpoolItem>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Updates notes, tags, and/or journal link on a spool item's upload metadata and index row.
    /// Does not call the API — callers patch the server for already-uploaded items.
    /// </summary>
    /// <param name="id">Spool item id.</param>
    /// <param name="notes">Replacement notes (always written).</param>
    /// <param name="tags">Replacement tags (always written; caller should normalize).</param>
    /// <param name="setJournal">When true, apply <paramref name="journalEntryId"/> (null clears the link).</param>
    /// <param name="journalEntryId">Journal entry to link when <paramref name="setJournal"/> is true.</param>
    /// <returns>True when the item existed and was updated.</returns>
    public bool UpdateUploadMetadata(
        Uuid7 id,
        string? notes,
        string? tags,
        bool setJournal,
        Uuid7? journalEntryId)
    {
        var item = GetItem(id);
        if (item == null)
        {
            return false;
        }

        var meta = ReadUploadMetadata(id) ?? new ScreenshotUploadMetadata { Id = id };
        meta.Id = meta.Id is { } mid && mid != Uuid7.Empty ? mid : id;
        meta.Notes = notes;
        meta.Tags = tags;
        if (setJournal)
        {
            meta.JournalEntryId = journalEntryId is { } jid && jid != Uuid7.Empty ? jid : (Uuid7?)null;
        }

        // Preserve machine/timestamp/etc. from existing meta when present.
        if (meta.MachineId == Uuid7.Empty && item.MachineId != Uuid7.Empty)
        {
            meta.MachineId = item.MachineId;
        }

        ApplyMetadata(item, meta);
        WriteUploadMetadata(id, meta);
        WriteMeta(item);
        return true;
    }

    /// <summary>
    /// Reads upload metadata for a spool item (<c>upload.json</c>, or legacy embedded field).
    /// </summary>
    /// <param name="id">Spool item id.</param>
    public ScreenshotUploadMetadata? ReadUploadMetadata(Uuid7 id)
    {
        var uploadPath = UploadMetaPath(id);
        if (File.Exists(uploadPath))
        {
            try
            {
                return JsonSerializer.Deserialize<ScreenshotUploadMetadata>(
                    File.ReadAllText(uploadPath), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        // Legacy: metadataJson inside meta.json
        var metaPath = MetaPath(id);
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            if (doc.RootElement.TryGetProperty("metadataJson", out var embedded)
                || doc.RootElement.TryGetProperty("MetadataJson", out embedded))
            {
                var json = embedded.GetString();
                if (!string.IsNullOrEmpty(json))
                {
                    return JsonSerializer.Deserialize<ScreenshotUploadMetadata>(json!, JsonOptions);
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Deletes one or more spool items (meta + upload meta + image + index rows).
    /// </summary>
    /// <param name="ids">Item ids to remove.</param>
    /// <returns>Number of items whose directories were removed.</returns>
    public int DeleteItems(IEnumerable<Uuid7> ids)
    {
        var idList = ids.ToList();
        var n = 0;
        foreach (var id in idList)
        {
            var dir = ItemDir(id);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, true);
                n++;
            }
            catch
            {
                // ignore locked/partial
            }
        }
        _index.Delete(idList);
        return n;
    }

    /// <summary>
    /// Attempts to upload all due pending items via <paramref name="api"/>.
    /// Each item's <see cref="SpoolItem.Id"/> is sent as the request metadata id; the server
    /// response id is recorded as <see cref="SpoolItem.ServerScreenshotId"/> when
    /// <paramref name="retainUploaded"/> is true.
    /// </summary>
    /// <param name="api">Authenticated API client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="retainUploaded">
    /// When true (tray/background path), successfully-uploaded items are marked Uploaded and
    /// kept on disk as the recent-screenshots cache (then size-capped by
    /// <see cref="ScreenshotCacheHousekeeping"/>). When false (CLI one-shot push/sync), items
    /// are deleted after a successful upload.
    /// </param>
    /// <returns>Counts of succeeded and failed attempts this run.</returns>
    public async Task<(int Succeeded, int Failed)> PushAsync(
        SchnappApiClient api, CancellationToken ct = default, bool retainUploaded = false)
    {
        if (api == null)
        {
            throw new ArgumentNullException(nameof(api));
        }

        PruneExpired();
        var succeeded = 0;
        var failed = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in ListItems())
        {
            ct.ThrowIfCancellationRequested();
            if (item.NextAttemptUtc.HasValue && item.NextAttemptUtc.Value > now)
            {
                continue;
            }

            var imagePath = ImagePath(item.Id);
            if (!File.Exists(imagePath))
            {
                DeleteItem(item.Id);
                continue;
            }

            try
            {
                var meta = ReadUploadMetadata(item.Id) ?? new ScreenshotUploadMetadata();
                // Prefer spool id as the server screenshot id (idempotent retries).
                meta.Id = item.Id;
                ScreenshotResponse response;
                using (var fs = File.OpenRead(imagePath))
                {
                    response = await api.UploadScreenshotAsync(fs, item.FileName, item.ContentType, meta, ct)
                        .ConfigureAwait(false);
                }

                if (retainUploaded)
                {
                    // response.Id may differ if the server reassigned on a non-duplicate conflict.
                    _index.MarkUploaded(item.Id, response.Id, now);
                }
                else
                {
                    DeleteItem(item.Id);
                }

                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                item.Attempts++;
                item.LastError = ex.Message;
                item.LastAttemptUtc = now;
                // exponential backoff: 5s, 10s, 20s, … cap 1 hour
                var delaySec = Math.Min(3600, 5 * (1 << Math.Min(item.Attempts, 10)));
                item.NextAttemptUtc = now.AddSeconds(delaySec);
                WriteMeta(item);
            }
        }

        return (succeeded, failed);
    }

    /// <summary>
    /// Removes pending items older than max age and enforces the pending count/bytes caps by
    /// dropping the oldest pending items. Never touches already-uploaded (cached) items.
    /// </summary>
    public void PruneExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _maxAge;
        foreach (var item in _index.ListPending())
        {
            if (item.EnqueuedUtc < cutoff)
            {
                DeleteItem(item.Id);
            }
        }

        DropOldestUntilWithinCaps();
    }

    private void EnforceCapsBeforeAdd(long newBytes)
    {
        PruneExpired();
        var items = _index.ListPending().ToList();
        long total = items.Sum(ItemBytes) + newBytes;

        while ((items.Count >= _maxItems || total > _maxBytes) && items.Count > 0)
        {
            var drop = items[0];
            total -= ItemBytes(drop);
            DeleteItem(drop.Id);
            items.RemoveAt(0);
        }
    }

    private void DropOldestUntilWithinCaps()
    {
        var items = _index.ListPending().ToList();
        long total = items.Sum(ItemBytes);
        while ((items.Count > _maxItems || total > _maxBytes) && items.Count > 0)
        {
            var drop = items[0];
            total -= ItemBytes(drop);
            DeleteItem(drop.Id);
            items.RemoveAt(0);
        }
    }

    /// <summary>Cached byte size if known, else stats the file (legacy items predating <see cref="SpoolItem.ByteSize"/>).</summary>
    private long ItemBytes(SpoolItem i)
    {
        if (i.ByteSize > 0)
        {
            return i.ByteSize;
        }

        var p = ImagePath(i.Id);
        return File.Exists(p) ? new FileInfo(p).Length : 0L;
    }

    /// <summary>
    /// Brings the SQLite index in line with what's actually on disk: imports folders missing
    /// from the index (first-run migration from a pre-index spool, or crash before an insert),
    /// and drops index rows whose folder no longer exists (externally deleted). Called once
    /// per <see cref="ScreenshotSpool"/> construction.
    /// </summary>
    private void ReconcileIndexWithFilesystem()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        var known = _index.AllIds();
        var onDisk = new HashSet<Uuid7>();

        foreach (var dir in Directory.GetDirectories(_root))
        {
            var name = Path.GetFileName(dir);
            if (!Uuid7Text.TryParse(name, out var dirId))
            {
                continue; // e.g. not an item folder (shouldn't happen under the spool root)
            }

            onDisk.Add(dirId);

            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath) || known.Contains(dirId))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<SpoolItem>(File.ReadAllText(metaPath), JsonOptions);
                if (item is null)
                {
                    continue;
                }

                MaybeMigrateLegacyUploadMetadata(item.Id, metaPath);
                if (item.ByteSize <= 0)
                {
                    var p = ImagePath(item.Id);
                    if (File.Exists(p))
                    {
                        item.ByteSize = new FileInfo(p).Length;
                    }
                }
                if (item.MachineId == Uuid7.Empty)
                {
                    // Backfill metadata fields into the index for items written before this
                    // field set existed (reads the still-authoritative upload.json).
                    var upload = ReadUploadMetadata(item.Id);
                    if (upload != null)
                    {
                        ApplyMetadata(item, upload);
                    }
                }
                _index.Upsert(item, item.ByteSize);
            }
            catch
            {
                // skip unreadable item
            }
        }

        var stale = known.Where(id => !onDisk.Contains(id)).ToList();
        if (stale.Count > 0)
        {
            _index.Delete(stale);
        }
    }

    private void MaybeMigrateLegacyUploadMetadata(Uuid7 id, string metaPath)
    {
        var uploadPath = UploadMetaPath(id);
        if (File.Exists(uploadPath))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            if (!doc.RootElement.TryGetProperty("metadataJson", out var embedded)
                && !doc.RootElement.TryGetProperty("MetadataJson", out embedded))
            {
                return;
            }

            var json = embedded.GetString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var upload = JsonSerializer.Deserialize<ScreenshotUploadMetadata>(json!, JsonOptions)
                         ?? new ScreenshotUploadMetadata();
            WriteUploadMetadata(id, upload);

            // Rewrite meta without the embedded field (SpoolItem no longer has it).
            var item = JsonSerializer.Deserialize<SpoolItem>(File.ReadAllText(metaPath), JsonOptions);
            if (item != null)
            {
                ApplyMetadata(item, upload);
                WriteMeta(item);
            }
        }
        catch
        {
            // leave legacy as-is
        }
    }

    private void WriteMeta(SpoolItem item)
    {
        var path = MetaPath(item.Id);
        Directory.CreateDirectory(ItemDir(item.Id));
        File.WriteAllText(path, JsonSerializer.Serialize(item, JsonOptions));
        _index.Upsert(item, item.ByteSize);
    }

    private void WriteUploadMetadata(Uuid7 id, ScreenshotUploadMetadata metadata)
    {
        Directory.CreateDirectory(ItemDir(id));
        File.WriteAllText(UploadMetaPath(id), JsonSerializer.Serialize(metadata, JsonOptions));
    }

    /// <summary>
    /// Copies <see cref="ScreenshotUploadMetadata"/> fields onto the <see cref="SpoolItem"/> so
    /// they're also persisted in the unified SQLite database (alongside <c>upload.json</c>).
    /// </summary>
    private static void ApplyMetadata(SpoolItem item, ScreenshotUploadMetadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        item.MachineId = metadata.MachineId;
        item.Timestamp = metadata.Timestamp;
        item.ClientTimeZoneId = metadata.ClientTimeZoneId;
        item.MonitorId = metadata.MonitorId;
        item.MonitorName = metadata.MonitorName;
        item.WindowTitle = metadata.WindowTitle;
        item.ProgramName = metadata.ProgramName;
        item.CommandLine = metadata.CommandLine;
        item.Notes = metadata.Notes;
        item.Tags = metadata.Tags;
        item.JournalEntryId = metadata.JournalEntryId;
        item.Width = metadata.Width;
        item.Height = metadata.Height;
    }

    private void DeleteItem(Uuid7 id)
    {
        var dir = ItemDir(id);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, true); }
            catch { /* ignore */ }
        }
        _index.Delete(new[] { id });
    }

    private string ItemDir(Uuid7 id)
    {
        var id26 = Path.Combine(_root, Uuid7Text.Format(id));
        if (Directory.Exists(id26))
        {
            return id26;
        }

        var legacyN = Path.Combine(_root, id.ToString("N"));
        if (Directory.Exists(legacyN))
        {
            return legacyN;
        }

        return id26;
    }

    private string MetaPath(Uuid7 id) => Path.Combine(ItemDir(id), "meta.json");
    private string UploadMetaPath(Uuid7 id) => Path.Combine(ItemDir(id), "upload.json");
    private string ImagePath(Uuid7 id) => Path.Combine(ItemDir(id), "image.bin");

    /// <summary>Closes the SQLite index connection.</summary>
    public void Dispose() => _index.Dispose();
}
