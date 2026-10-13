namespace Schnapp.Client.Core.Upload;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Medo;
using Microsoft.Data.Sqlite;
using Schnapp.Client.Core.Spool;
using Schnapp.Contracts;

/// <summary>
/// SQLite-backed local cache of customer/project/task reference data and journal targets,
/// pulled from the API by <see cref="UploadService.RefreshReferenceDataAsync"/>. Durable across
/// app restarts and API outages (unlike the previous in-memory-only lists), so tray menus can
/// populate immediately on launch and keep working offline.
/// </summary>
public sealed class ReferenceDataCache : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    /// <summary>Opens (creating if needed) the reference-data cache database at <paramref name="dbPath"/>.</summary>
    public ReferenceDataCache(string dbPath)
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
            CREATE TABLE IF NOT EXISTS customer (
                id TEXT NOT NULL PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                notes TEXT NULL,
                tags TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS project (
                id TEXT NOT NULL PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                notes TEXT NULL,
                tags TEXT NULL,
                customer_id TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS task (
                id TEXT NOT NULL PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                notes TEXT NULL,
                tags TEXT NULL,
                customer_id TEXT NULL,
                project_id TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS journal_target (
                id TEXT NOT NULL PRIMARY KEY,
                customer_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                notes TEXT NULL,
                tags TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS tag (
                tag TEXT NOT NULL,
                notes TEXT NULL,
                customer_id TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (tag, customer_id)
            );
            """);
    }

    /// <summary>Replaces the cached customer list wholesale.</summary>
    public void ReplaceCustomers(IEnumerable<ListItemResponse> items) =>
        Replace("customer", items, i => new { Id = Fmt(i.Id), i.Code, i.Name, i.Notes, i.Tags });

    /// <summary>Replaces the cached project list wholesale.</summary>
    public void ReplaceProjects(IEnumerable<ListItemResponse> items) =>
        Replace("project", items, i => new { Id = Fmt(i.Id), i.Code, i.Name, i.Notes, i.Tags, CustomerId = FmtOrNull(i.CustomerId) });

    /// <summary>Replaces the cached task list wholesale.</summary>
    public void ReplaceTasks(IEnumerable<ListItemResponse> items) =>
        Replace("task", items, i => new { Id = Fmt(i.Id), i.Code, i.Name, i.Notes, i.Tags, CustomerId = FmtOrNull(i.CustomerId), ProjectId = FmtOrNull(i.ProjectId) });

    /// <summary>Replaces the cached tag dictionary wholesale.</summary>
    public void ReplaceTags(IEnumerable<TagResponse> items)
    {
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            _connection.Execute("DELETE FROM tag", transaction: tx);
            foreach (var i in items)
            {
                if (string.IsNullOrWhiteSpace(i.Tag))
                {
                    continue;
                }

                _connection.Execute(
                    "INSERT OR REPLACE INTO tag (tag, notes, customer_id) VALUES (@Tag, @Notes, @CustomerId)",
                    new
                    {
                        i.Tag,
                        i.Notes,
                        CustomerId = i.CustomerId is { } c && c != Uuid7.Empty ? Fmt(c) : ""
                    },
                    transaction: tx);
            }
            tx.Commit();
        }
    }

    /// <summary>
    /// Distinct tag strings for autocomplete. When <paramref name="customerId"/> is set,
    /// returns global tags plus that customer's tags.
    /// </summary>
    public IReadOnlyList<string> ListTagValues(Uuid7? customerId = null)
    {
        lock (_gate)
        {
            if (customerId is { } cid && cid != Uuid7.Empty)
            {
                var cidText = Fmt(cid);
                return _connection.Query<string>(
                    """
                    SELECT DISTINCT tag FROM tag
                    WHERE customer_id = '' OR customer_id = @cid
                    ORDER BY tag
                    """,
                    new { cid = cidText }).ToList();
            }

            return _connection.Query<string>(
                "SELECT DISTINCT tag FROM tag ORDER BY tag").ToList();
        }
    }

    /// <summary>Replaces the cached journal-target list wholesale.</summary>
    public void ReplaceJournalTargets(IEnumerable<JournalTargetResponse> items) =>
        Replace("journal_target", items, i => new
        {
            Id = Fmt(i.Id),
            CustomerId = Fmt(i.CustomerId),
            ProjectId = Fmt(i.ProjectId),
            TaskId = Fmt(i.TaskId),
            i.Code,
            i.Name,
            i.Notes,
            i.Tags
        });

    /// <summary>All cached customers.</summary>
    public List<ListItemResponse> GetCustomers()
    {
        lock (_gate)
        {
            return [.. _connection.Query<CustomerRow>("SELECT * FROM customer").Select(r => new ListItemResponse { Id = Parse(r.Id), Code = r.Code, Name = r.Name, Notes = r.Notes, Tags = r.Tags })];
        }
    }

    /// <summary>All cached projects.</summary>
    public List<ListItemResponse> GetProjects()
    {
        lock (_gate)
        {
            return [.. _connection.Query<ProjectRow>("SELECT * FROM project")
                .Select(r => new ListItemResponse
                {
                    Id = Parse(r.Id),
                    Code = r.Code,
                    Name = r.Name,
                    Notes = r.Notes,
                    Tags = r.Tags,
                    CustomerId = ParseOrNull(r.CustomerId)
                })];
        }
    }

    /// <summary>All cached tasks.</summary>
    public List<ListItemResponse> GetTasks()
    {
        lock (_gate)
        {
            return [.. _connection.Query<TaskRow>("SELECT * FROM task")
                .Select(r => new ListItemResponse
                {
                    Id = Parse(r.Id),
                    Code = r.Code,
                    Name = r.Name,
                    Notes = r.Notes,
                    Tags = r.Tags,
                    CustomerId = ParseOrNull(r.CustomerId),
                    ProjectId = ParseOrNull(r.ProjectId)
                })];
        }
    }

    /// <summary>All cached journal targets.</summary>
    public List<JournalTargetResponse> GetJournalTargets()
    {
        lock (_gate)
        {
            return [.. _connection.Query<JournalTargetRow>("SELECT * FROM journal_target")
                .Select(r => new JournalTargetResponse
                {
                    Id = Parse(r.Id),
                    CustomerId = Parse(r.CustomerId),
                    ProjectId = Parse(r.ProjectId),
                    TaskId = Parse(r.TaskId),
                    Code = r.Code,
                    Name = r.Name,
                    Notes = r.Notes,
                    Tags = r.Tags
                })];
        }
    }

    private void Replace<T>(string table, IEnumerable<T> items, Func<T, object> toRow)
    {
        var rows = items.Select(toRow).ToList();
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            _connection.Execute($"DELETE FROM {table}", transaction: tx);
            if (rows.Count > 0)
            {
                _connection.Execute($"INSERT INTO {table} ({ColumnNamesFor(table)}) VALUES ({ParameterNamesFor(table)})", rows, transaction: tx);
            }
            tx.Commit();
        }
    }

    private static string ColumnNamesFor(string table) => table switch
    {
        "customer" => "id, code, name, notes, tags",
        "project" => "id, code, name, notes, tags, customer_id",
        "task" => "id, code, name, notes, tags, customer_id, project_id",
        "journal_target" => "id, customer_id, project_id, task_id, code, name, notes, tags",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };

    private static string ParameterNamesFor(string table) => table switch
    {
        "customer" => "@Id, @Code, @Name, @Notes, @Tags",
        "project" => "@Id, @Code, @Name, @Notes, @Tags, @CustomerId",
        "task" => "@Id, @Code, @Name, @Notes, @Tags, @CustomerId, @ProjectId",
        "journal_target" => "@Id, @CustomerId, @ProjectId, @TaskId, @Code, @Name, @Notes, @Tags",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };

    private static string Fmt(Uuid7 id) => Uuid7Text.Format(id);    private static string? FmtOrNull(Uuid7? id) => id.HasValue && id.Value != Uuid7.Empty ? Uuid7Text.Format(id.Value) : null;
    private static Uuid7 Parse(string text) => Uuid7Text.TryParse(text, out var id) ? id : Uuid7.Empty;
    private static Uuid7? ParseOrNull(string? text) => text == null ? null : Parse(text);

    public void Dispose() => _connection.Dispose();

    private class CustomerRow
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Notes { get; set; }
        public string? Tags { get; set; }
    }

    private class ProjectRow : CustomerRow
    {
        public string? CustomerId { get; set; }
    }

    private sealed class TaskRow : ProjectRow
    {
        public string? ProjectId { get; set; }
    }

    private sealed class JournalTargetRow
    {
        public string Id { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Notes { get; set; }
        public string? Tags { get; set; }
    }
}
