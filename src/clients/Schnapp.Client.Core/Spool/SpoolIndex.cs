namespace Schnapp.Client.Core.Spool;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Schnapp.Contracts;

using Medo;

/// <summary>
/// SQLite-backed index over <see cref="ScreenshotSpool"/> items, so listing/status/pruning don't
/// need to enumerate and JSON-parse every item folder on every call. The filesystem (one folder
/// per item under the spool root) remains the source of truth for image bytes and full upload
/// metadata; this index only caches the fields needed to list, filter, and aggregate quickly.
/// Safe to rebuild from disk at any time (see <see cref="ScreenshotSpool"/> reconciliation).
/// </summary>
internal sealed class SpoolIndex : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    /// <summary>Opens (creating if needed) the index database at <paramref name="dbPath"/>.</summary>
    public SpoolIndex(string dbPath)
    {
        SpoolDapperConfig.EnsureConfigured();

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        _connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS spool_item (
                id TEXT NOT NULL PRIMARY KEY,
                enqueued_utc TEXT NOT NULL,
                file_name TEXT NOT NULL,
                content_type TEXT NOT NULL,
                byte_size INTEGER NOT NULL,
                attempts INTEGER NOT NULL,
                last_error TEXT NULL,
                last_attempt_utc TEXT NULL,
                next_attempt_utc TEXT NULL,
                upload_status INTEGER NOT NULL,
                server_screenshot_id TEXT NULL,
                uploaded_at_utc TEXT NULL,
                machine_id TEXT NULL,
                timestamp TEXT NULL,
                client_time_zone_id TEXT NULL,
                monitor_id TEXT NULL,
                monitor_name TEXT NULL,
                window_title TEXT NULL,
                program_name TEXT NULL,
                command_line TEXT NULL,
                notes TEXT NULL,
                tags TEXT NULL,
                journal_entry_id TEXT NULL,
                width INTEGER NULL,
                height INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_spool_item_status ON spool_item(upload_status);
            CREATE INDEX IF NOT EXISTS ix_spool_item_uploaded_at ON spool_item(uploaded_at_utc);
            CREATE INDEX IF NOT EXISTS ix_spool_item_enqueued ON spool_item(enqueued_utc);
            """);
    }

    /// <summary>All ids currently indexed (used by reconciliation against the filesystem).</summary>
    public HashSet<Uuid7> AllIds()
    {
        lock (_gate)
        {
            return [.. _connection.Query<string>("SELECT id FROM spool_item").Select(ParseId)];
        }
    }

    /// <summary>Inserts or replaces the indexed row for one item.</summary>
    public void Upsert(SpoolItem item, long byteSize)
    {
        lock (_gate)
        {
            _connection.Execute(
                """
                INSERT INTO spool_item (
                    id, enqueued_utc, file_name, content_type, byte_size, attempts,
                    last_error, last_attempt_utc, next_attempt_utc,
                    upload_status, server_screenshot_id, uploaded_at_utc,
                    machine_id, timestamp, client_time_zone_id, monitor_id, monitor_name,
                    window_title, program_name, command_line, notes, tags,
                    journal_entry_id, width, height)
                VALUES (
                    @Id, @EnqueuedUtc, @FileName, @ContentType, @ByteSize, @Attempts,
                    @LastError, @LastAttemptUtc, @NextAttemptUtc,
                    @UploadStatus, @ServerScreenshotId, @UploadedAtUtc,
                    @MachineId, @Timestamp, @ClientTimeZoneId, @MonitorId, @MonitorName,
                    @WindowTitle, @ProgramName, @CommandLine, @Notes, @Tags,
                    @JournalEntryId, @Width, @Height)
                ON CONFLICT(id) DO UPDATE SET
                    enqueued_utc = excluded.enqueued_utc,
                    file_name = excluded.file_name,
                    content_type = excluded.content_type,
                    byte_size = excluded.byte_size,
                    attempts = excluded.attempts,
                    last_error = excluded.last_error,
                    last_attempt_utc = excluded.last_attempt_utc,
                    next_attempt_utc = excluded.next_attempt_utc,
                    upload_status = excluded.upload_status,
                    server_screenshot_id = excluded.server_screenshot_id,
                    uploaded_at_utc = excluded.uploaded_at_utc,
                    machine_id = excluded.machine_id,
                    timestamp = excluded.timestamp,
                    client_time_zone_id = excluded.client_time_zone_id,
                    monitor_id = excluded.monitor_id,
                    monitor_name = excluded.monitor_name,
                    window_title = excluded.window_title,
                    program_name = excluded.program_name,
                    command_line = excluded.command_line,
                    notes = excluded.notes,
                    tags = excluded.tags,
                    journal_entry_id = excluded.journal_entry_id,
                    width = excluded.width,
                    height = excluded.height
                """,
                ToRow(item, byteSize));
        }
    }

    /// <summary>Marks an item Uploaded (kept locally as a recent-cache entry) instead of removing it.</summary>
    public void MarkUploaded(Uuid7 id, Uuid7 serverScreenshotId, DateTimeOffset uploadedAtUtc)
    {
        lock (_gate)
        {
            _connection.Execute(
                """
                UPDATE spool_item SET
                    upload_status = @Status,
                    server_screenshot_id = @ServerId,
                    uploaded_at_utc = @UploadedAtUtc,
                    last_error = NULL,
                    next_attempt_utc = NULL
                WHERE id = @Id
                """,
                new
                {
                    Id = Fmt(id),
                    Status = (int)SpoolUploadStatus.Uploaded,
                    ServerId = Fmt(serverScreenshotId),
                    UploadedAtUtc = FmtTime(uploadedAtUtc)
                });
        }
    }

    /// <summary>Removes indexed rows for the given ids (caller also removes the on-disk folders).</summary>
    public void Delete(IEnumerable<Uuid7> ids)
    {
        var idList = ids.Select(Fmt).ToList();
        if (idList.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            _connection.Execute("DELETE FROM spool_item WHERE id = @Id", idList.Select(id => new { Id = id }));
        }
    }

    /// <summary>Pending items (not yet uploaded), oldest first — matches historical <c>ListItems()</c> order.</summary>
    public IReadOnlyList<SpoolItem> ListPending()
    {
        lock (_gate)
        {
            var rows = _connection.Query<Row>(
                "SELECT * FROM spool_item WHERE upload_status = @Status ORDER BY enqueued_utc",
                new { Status = (int)SpoolUploadStatus.Pending });
            return [.. rows.Select(FromRow)];
        }
    }

    /// <summary>Already-uploaded (cached) items, oldest-uploaded first — for FIFO housekeeping.</summary>
    public IReadOnlyList<SpoolItem> ListUploadedOrderedByAge()
    {
        lock (_gate)
        {
            var rows = _connection.Query<Row>(
                "SELECT * FROM spool_item WHERE upload_status = @Status ORDER BY uploaded_at_utc",
                new { Status = (int)SpoolUploadStatus.Uploaded });
            return [.. rows.Select(FromRow)];
        }
    }

    /// <summary>Aggregate counts/bytes for pending and uploaded (cached) items.</summary>
    public SpoolStatus GetAggregateStatus()
    {
        lock (_gate)
        {
            var pending = _connection.QuerySingle<AggregateRow>(
                """
                SELECT COUNT(*) AS Count, COALESCE(SUM(byte_size), 0) AS Bytes,
                       MIN(enqueued_utc) AS Oldest, MAX(enqueued_utc) AS Newest,
                       SUM(CASE WHEN last_error IS NOT NULL AND last_error <> '' THEN 1 ELSE 0 END) AS Failed
                FROM spool_item WHERE upload_status = @Status
                """,
                new { Status = (int)SpoolUploadStatus.Pending });

            var uploaded = _connection.QuerySingle<AggregateRow>(
                "SELECT COUNT(*) AS Count, COALESCE(SUM(byte_size), 0) AS Bytes, NULL AS Oldest, NULL AS Newest, 0 AS Failed FROM spool_item WHERE upload_status = @Status",
                new { Status = (int)SpoolUploadStatus.Uploaded });

            return new SpoolStatus
            {
                PendingCount = pending.Count,
                FailedCount = pending.Failed,
                TotalBytes = pending.Bytes,
                OldestEnqueuedUtc = ParseTimeOrNull(pending.Oldest),
                NewestEnqueuedUtc = ParseTimeOrNull(pending.Newest),
                UploadedCount = uploaded.Count,
                UploadedBytes = uploaded.Bytes
            };
        }
    }

    public void Dispose() => _connection.Dispose();

    private static Uuid7 ParseId(string text) =>
        Uuid7Text.TryParse(text, out var id) ? id : Uuid7.Empty;

    private static string Fmt(Uuid7 id) => Uuid7Text.Format(id);

    private static string? FmtTime(DateTimeOffset? value) =>
        value?.ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimeOrNull(string? text) =>
        string.IsNullOrEmpty(text) ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object ToRow(SpoolItem item, long byteSize) => new
    {
        Id = Fmt(item.Id),
        EnqueuedUtc = FmtTime(item.EnqueuedUtc),
        item.FileName,
        item.ContentType,
        ByteSize = byteSize,
        item.Attempts,
        item.LastError,
        LastAttemptUtc = FmtTime(item.LastAttemptUtc),
        NextAttemptUtc = FmtTime(item.NextAttemptUtc),
        UploadStatus = (int)item.UploadStatus,
        ServerScreenshotId = item.ServerScreenshotId is { } sid ? Fmt(sid) : null,
        UploadedAtUtc = FmtTime(item.UploadedAtUtc),
        MachineId = item.MachineId != Uuid7.Empty ? Fmt(item.MachineId) : null,
        Timestamp = FmtTime(item.Timestamp),
        item.ClientTimeZoneId,
        item.MonitorId,
        item.MonitorName,
        item.WindowTitle,
        item.ProgramName,
        item.CommandLine,
        item.Notes,
        item.Tags,
        JournalEntryId = item.JournalEntryId is { } jid ? Fmt(jid) : null,
        item.Width,
        item.Height
    };

    private static SpoolItem FromRow(Row r) => new()
    {
        Id = ParseId(r.Id),
        EnqueuedUtc = ParseTimeOrNull(r.EnqueuedUtc) ?? default,
        FileName = r.FileName,
        ContentType = r.ContentType,
        ByteSize = r.ByteSize,
        Attempts = r.Attempts,
        LastError = r.LastError,
        LastAttemptUtc = ParseTimeOrNull(r.LastAttemptUtc),
        NextAttemptUtc = ParseTimeOrNull(r.NextAttemptUtc),
        UploadStatus = (SpoolUploadStatus)r.UploadStatus,
        ServerScreenshotId = string.IsNullOrEmpty(r.ServerScreenshotId) ? (Uuid7?)null : ParseId(r.ServerScreenshotId!),
        UploadedAtUtc = ParseTimeOrNull(r.UploadedAtUtc),
        MachineId = string.IsNullOrEmpty(r.MachineId) ? default : ParseId(r.MachineId!),
        Timestamp = ParseTimeOrNull(r.Timestamp),
        ClientTimeZoneId = r.ClientTimeZoneId,
        MonitorId = r.MonitorId,
        MonitorName = r.MonitorName,
        WindowTitle = r.WindowTitle,
        ProgramName = r.ProgramName,
        CommandLine = r.CommandLine,
        Notes = r.Notes,
        Tags = r.Tags,
        JournalEntryId = string.IsNullOrEmpty(r.JournalEntryId) ? (Uuid7?)null : ParseId(r.JournalEntryId!),
        Width = r.Width,
        Height = r.Height
    };

    /// <summary>Raw column shape for Dapper (snake_case columns map via <see cref="SpoolDapperConfig"/>).</summary>
    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string EnqueuedUtc { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long ByteSize { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public string? LastAttemptUtc { get; set; }
        public string? NextAttemptUtc { get; set; }
        public int UploadStatus { get; set; }
        public string? ServerScreenshotId { get; set; }
        public string? UploadedAtUtc { get; set; }
        public string? MachineId { get; set; }
        public string? Timestamp { get; set; }
        public string? ClientTimeZoneId { get; set; }
        public string? MonitorId { get; set; }
        public string? MonitorName { get; set; }
        public string? WindowTitle { get; set; }
        public string? ProgramName { get; set; }
        public string? CommandLine { get; set; }
        public string? Notes { get; set; }
        public string? Tags { get; set; }
        public string? JournalEntryId { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }

    private sealed class AggregateRow
    {
        public int Count { get; set; }
        public long Bytes { get; set; }
        public string? Oldest { get; set; }
        public string? Newest { get; set; }
        public int Failed { get; set; }
    }
}
