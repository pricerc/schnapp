namespace Schnapp.Client.Core.Upload;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using Medo;
using Microsoft.Data.Sqlite;
using Schnapp.Client.Core.Spool;
using Schnapp.Contracts;

/// <summary>
/// SQLite-backed offline mutation queue (journal / list creates), stored in the unified
/// client database alongside <c>spool_item</c> and reference (customers, projects, tasks) tables.
/// </summary>
internal sealed class MutationStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;
    private readonly object _gate = new();

    /// <summary>Opens (creating if needed) the mutation table in the database at <paramref name="dbPath"/>.</summary>
    public MutationStore(string dbPath)
    {
        SpoolDapperConfig.EnsureConfigured();
        _dbPath = dbPath;

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
            CREATE TABLE IF NOT EXISTS mutation (
                id TEXT NOT NULL PRIMARY KEY,
                kind INTEGER NOT NULL,
                enqueued_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                last_attempt_utc TEXT NULL,
                next_attempt_utc TEXT NULL,
                dead INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_mutation_enqueued ON mutation(enqueued_utc);
            CREATE INDEX IF NOT EXISTS ix_mutation_dead ON mutation(dead);
            """);
    }

    public string DatabasePath => _dbPath;

    public void Upsert(MutationItem item)
    {
        lock (_gate)
        {
            _connection.Execute(
                """
                INSERT INTO mutation (
                    id, kind, enqueued_utc, payload_json, attempts,
                    last_error, last_attempt_utc, next_attempt_utc, dead)
                VALUES (
                    @Id, @Kind, @EnqueuedUtc, @PayloadJson, @Attempts,
                    @LastError, @LastAttemptUtc, @NextAttemptUtc, @Dead)
                ON CONFLICT(id) DO UPDATE SET
                    kind = excluded.kind,
                    enqueued_utc = excluded.enqueued_utc,
                    payload_json = excluded.payload_json,
                    attempts = excluded.attempts,
                    last_error = excluded.last_error,
                    last_attempt_utc = excluded.last_attempt_utc,
                    next_attempt_utc = excluded.next_attempt_utc,
                    dead = excluded.dead
                """,
                ToRow(item));
        }
    }

    public IReadOnlyList<MutationItem> ListAll()
    {
        lock (_gate)
        {
            return [.. _connection.Query<Row>("SELECT * FROM mutation").Select(FromRow)];
        }
    }

    public int Delete(IEnumerable<Uuid7> ids)
    {
        var n = 0;
        lock (_gate)
        {
            foreach (var id in ids)
            {
                n += _connection.Execute(
                    "DELETE FROM mutation WHERE id = @Id",
                    new { Id = Uuid7Text.Format(id) });
            }
        }
        return n;
    }

    public int CountPending()
    {
        lock (_gate)
        {
            return _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM mutation WHERE dead = 0");
        }
    }

    public int CountDead()
    {
        lock (_gate)
        {
            return _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM mutation WHERE dead = 1");
        }
    }

    public void Dispose() => _connection.Dispose();

    private static object ToRow(MutationItem item) => new
    {
        Id = Uuid7Text.Format(item.Id),
        Kind = (int)item.Kind,
        EnqueuedUtc = FmtTime(item.EnqueuedUtc),
        PayloadJson = item.PayloadJson ?? "{}",
        item.Attempts,
        item.LastError,
        LastAttemptUtc = item.LastAttemptUtc is { } la ? FmtTime(la) : null,
        NextAttemptUtc = item.NextAttemptUtc is { } na ? FmtTime(na) : null,
        Dead = item.Dead ? 1 : 0
    };

    private static MutationItem FromRow(Row r) => new()
    {
        Id = ParseId(r.Id),
        Kind = (MutationKind)r.Kind,
        EnqueuedUtc = ParseTime(r.EnqueuedUtc) ?? default,
        PayloadJson = r.PayloadJson ?? "{}",
        Attempts = r.Attempts,
        LastError = r.LastError,
        LastAttemptUtc = ParseTime(r.LastAttemptUtc),
        NextAttemptUtc = ParseTime(r.NextAttemptUtc),
        Dead = r.Dead != 0
    };

    private static string FmtTime(DateTimeOffset t) =>
        t.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var t)
            ? t
            : null;
    }

    private static Uuid7 ParseId(string text) =>
        Uuid7Text.TryParse(text, out var id) ? id : Uuid7.Empty;

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public int Kind { get; set; }
        public string EnqueuedUtc { get; set; } = "";
        public string PayloadJson { get; set; } = "{}";
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public string? LastAttemptUtc { get; set; }
        public string? NextAttemptUtc { get; set; }
        public int Dead { get; set; }
    }
}
