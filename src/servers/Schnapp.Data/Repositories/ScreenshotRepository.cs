using Dapper;
using Microsoft.Extensions.Logging;
using Schnapp.Domain.Entities;

using Medo;
namespace Schnapp.Data.Repositories;

/// <summary>
/// Dapper access for the <c>screenshot</c> table (insert, user-scoped query, metadata update, delete).
/// </summary>
/// <remarks>
/// Registered scoped via <see cref="ServiceCollectionExtensions.AddSchnappData"/>.
/// Upload conflict handling uses <see cref="GetByIdAsync"/> (not user-scoped) so a client-supplied
/// id can be checked org-wide; listing and mutations for gallery/API stay user-scoped.
/// </remarks>
public sealed class ScreenshotRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDatabaseDialect _sql;
    private readonly ILogger<ScreenshotRepository> _logger;

    /// <summary>
    /// Creates a repository bound to the shared connection factory.
    /// </summary>
    /// <param name="db">Factory that opens catalog connections.</param>
    /// <param name="sql">Active catalog dialect.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public ScreenshotRepository(IDbConnectionFactory db, IDatabaseDialect sql, ILogger<ScreenshotRepository> logger)
    {
        _db = db;
        _sql = sql;
        _logger = logger;
    }

    /// <summary>
    /// Inserts a new screenshot row. Caller supplies a unique <see cref="Screenshot.Id"/>
    /// (client-supplied or server-minted).
    /// </summary>
    /// <param name="shot">Entity to insert (storage keys and hash already computed).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InsertAsync(Screenshot shot, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Inserting screenshot {Id} for user {UserId} (storageKey={StorageKey}, thumbStorageKey={ThumbStorageKey}, contentType={ContentType}, bytes={ByteSize})",
            shot.Id, shot.UserId, shot.StorageKey, shot.ThumbStorageKey, shot.ContentType, shot.ByteSize);
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            INSERT INTO screenshot (
                id, user_id, machine_id, timestamp_utc, client_time_zone_id, received_at_utc,
                monitor_id, monitor_name, window_title, program_name, command_line,
                content_type, byte_size, width, height, sha256, fingerprint,
                storage_key, thumb_storage_key,
                notes, tags, journal_entry_id, created_at)
            VALUES (
                @Id, @UserId, @MachineId, @TimestampUtc, @ClientTimeZoneId, @ReceivedAtUtc,
                @MonitorId, @MonitorName, @WindowTitle, @ProgramName, @CommandLine,
                @ContentType, @ByteSize, @Width, @Height, @Sha256, {_sql.JsonParam("Fingerprint")},
                @StorageKey, @ThumbStorageKey,
                @Notes, @Tags, @JournalEntryId, @CreatedAt)
            """,
            shot, cancellationToken: ct));
        _logger.LogInformation("Inserted screenshot {Id} for user {UserId}", shot.Id, shot.UserId);
    }

    /// <summary>
    /// Loads a screenshot owned by <paramref name="userId"/>, or <see langword="null"/> if missing.
    /// </summary>
    /// <param name="id">Screenshot primary key.</param>
    /// <param name="userId">Owning user id (authorization scope).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Screenshot?> GetForUserAsync(Uuid7 id, Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var shot = await conn.QuerySingleOrDefaultAsync<Screenshot>(new CommandDefinition(
            "SELECT * FROM screenshot WHERE id = @id AND user_id = @userId",
            new { id, userId }, cancellationToken: ct));
        if (shot is null)
            _logger.LogWarning("Screenshot {Id} not found for user {UserId}", id, userId);
        else
            _logger.LogDebug("Loaded screenshot {Id} for user {UserId} (storageKey={StorageKey})", id, userId, shot.StorageKey);
        return shot;
    }

    /// <summary>
    /// Loads a screenshot by primary key only (no user filter).
    /// Used on upload when the client supplies an id: detect existing rows so true duplicates
    /// can return the existing record and non-duplicates can mint a new id.
    /// </summary>
    /// <param name="id">Screenshot primary key (often the client's spool item id).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Screenshot?> GetByIdAsync(Uuid7 id, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Screenshot>(new CommandDefinition(
            "SELECT * FROM screenshot WHERE id = @id",
            new { id }, cancellationToken: ct));
    }

    /// <summary>
    /// Lists screenshots for a user in a half-open UTC time range, optionally filtered by monitor name.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="fromUtc">Range start (inclusive).</param>
    /// <param name="toUtc">Range end (exclusive).</param>
    /// <param name="monitorName">Optional exact monitor name filter; <see langword="null"/> = all.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<Screenshot>> ListForUserRangeAsync(
        Uuid7 userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, string? monitorName, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Screenshot>(new CommandDefinition(
            """
            SELECT * FROM screenshot
            WHERE user_id = @userId
              AND deleted_at IS NULL
              AND timestamp_utc >= @fromUtc
              AND timestamp_utc < @toUtc
              AND (@monitorName IS NULL OR monitor_name = @monitorName)
            ORDER BY timestamp_utc, id
            """,
            new { userId, fromUtc, toUtc, monitorName }, cancellationToken: ct));
        var list = rows.ToList();
        _logger.LogDebug(
            "ListForUserRangeAsync user={UserId} from={FromUtc} to={ToUtc} monitor={MonitorName} -> {Count} rows",
            userId, fromUtc, toUtc, monitorName, list.Count);
        return list;
    }

    /// <summary>Loads owned screenshots by id (order not guaranteed).</summary>
    public async Task<IReadOnlyList<Screenshot>> ListForUserByIdsAsync(
        Uuid7 userId, IReadOnlyList<Uuid7> ids, CancellationToken ct = default)
    {
        if (ids is null || ids.Count == 0)
        {
            return Array.Empty<Screenshot>();
        }

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var result = new List<Screenshot>(ids.Count);
        // Chunked: Firebird caps IN lists at 1500 entries.
        for (var offset = 0; offset < ids.Count; offset += 500)
        {
            var chunk = ids.Skip(offset).Take(500).ToList();
            var args = new DynamicParameters();
            args.Add("userId", userId);
            var list = InClause.Add(args, chunk, "id");
            var rows = await conn.QueryAsync<Screenshot>(new CommandDefinition(
                $"SELECT * FROM screenshot WHERE user_id = @userId AND id IN {list}",
                args, cancellationToken: ct));
            result.AddRange(rows);
        }

        return result;
    }

    /// <summary>Owned screenshots in a range that still lack a fingerprint, oldest first.</summary>
    public async Task<IReadOnlyList<Screenshot>> ListMissingFingerprintAsync(
        Uuid7 userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken ct = default)
    {
        if (limit < 1)
        {
            limit = 1;
        }

        if (limit > 500)
        {
            limit = 500;
        }

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Screenshot>(new CommandDefinition(
            $"""
            SELECT * FROM screenshot
            WHERE user_id = @userId
              AND deleted_at IS NULL
              AND timestamp_utc >= @fromUtc
              AND timestamp_utc < @toUtc
              AND fingerprint IS NULL
            ORDER BY timestamp_utc, id
            {_sql.Limit(limit)}
            """,
            new { userId, fromUtc, toUtc, limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>How many owned rows in the range still have no fingerprint.</summary>
    public async Task<int> CountMissingFingerprintAsync(
        Uuid7 userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CAST(COUNT(*) AS INTEGER) FROM screenshot
            WHERE user_id = @userId
              AND deleted_at IS NULL
              AND timestamp_utc >= @fromUtc
              AND timestamp_utc < @toUtc
              AND fingerprint IS NULL
            """,
            new { userId, fromUtc, toUtc }, cancellationToken: ct));
    }

    /// <summary>Stores fingerprint JSON for an owned screenshot.</summary>
    public async Task UpdateFingerprintAsync(Uuid7 id, Uuid7 userId, string json, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE screenshot SET fingerprint = {_sql.JsonParam("json")}
            WHERE id = @id AND user_id = @userId
            """,
            new { id, userId, json }, cancellationToken: ct));
    }

    /// <summary>
    /// Updates notes and tags only (does not change the linked journal entry).
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="userId">Owning user id.</param>
    /// <param name="notes">New notes value (may be null).</param>
    /// <param name="tags">New tags value (may be null).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateNotesTagsAsync(Uuid7 id, Uuid7 userId, string? notes, string? tags, CancellationToken ct = default)
        => await UpdateMetadataAsync(id, userId, notes, tags, setJournal: false, journalEntryId: null, ct);

    /// <summary>
    /// Updates notes/tags and optionally the linked journal entry for a user-owned screenshot.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="userId">Owning user id.</param>
    /// <param name="notes">New notes value (may be null).</param>
    /// <param name="tags">New tags value (may be null).</param>
    /// <param name="setJournal">When true, also set <paramref name="journalEntryId"/> on the row.</param>
    /// <param name="journalEntryId">Journal link when <paramref name="setJournal"/> is true.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateMetadataAsync(
        Uuid7 id,
        Uuid7 userId,
        string? notes,
        string? tags,
        bool setJournal,
        Uuid7? journalEntryId,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        if (setJournal)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE screenshot SET notes = @notes, tags = @tags, journal_entry_id = @journalEntryId
                WHERE id = @id AND user_id = @userId
                """,
                new { id, userId, notes, tags, journalEntryId }, cancellationToken: ct));
        }
        else
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE screenshot SET notes = @notes, tags = @tags
                WHERE id = @id AND user_id = @userId
                """,
                new { id, userId, notes, tags }, cancellationToken: ct));
        }
        _logger.LogDebug("Updated metadata for screenshot {Id} (user {UserId}, setJournal={SetJournal})", id, userId, setJournal);
    }

    /// <summary>Screenshots the user has soft-deleted, newest delete first.</summary>
    public async Task<IReadOnlyList<Screenshot>> ListPendingDeletesAsync(Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Screenshot>(new CommandDefinition(
            """
            SELECT * FROM screenshot
            WHERE user_id = @userId
              AND deleted_at IS NOT NULL
            ORDER BY deleted_at DESC, timestamp_utc DESC, id
            """,
            new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Hides owned screenshots from the gallery. Rows already pending are left unchanged.
    /// </summary>
    /// <returns>How many rows were newly soft-deleted.</returns>
    public async Task<int> SoftDeleteForUserAsync(Uuid7 userId, IReadOnlyCollection<Uuid7> ids, CancellationToken ct = default)
    {
        var distinct = DistinctIds(ids);
        if (distinct.Count == 0)
            return 0;

        var now = DateTimeOffset.UtcNow;
        return await ExecuteForIdsAsync(
            userId,
            distinct,
            """
            UPDATE screenshot
            SET deleted_at = @now
            WHERE user_id = @userId
              AND deleted_at IS NULL
              AND id IN {in}
            """,
            new { now },
            ct);
    }

    /// <summary>Puts owned pending screenshots back in the gallery.</summary>
    /// <returns>How many rows were restored.</returns>
    public async Task<int> RestoreForUserAsync(Uuid7 userId, IReadOnlyCollection<Uuid7> ids, CancellationToken ct = default)
    {
        var distinct = DistinctIds(ids);
        if (distinct.Count == 0)
            return 0;

        return await ExecuteForIdsAsync(
            userId,
            distinct,
            """
            UPDATE screenshot
            SET deleted_at = NULL
            WHERE user_id = @userId
              AND deleted_at IS NOT NULL
              AND id IN {in}
            """,
            extra: null,
            ct);
    }

    /// <summary>
    /// Deletes a pending screenshot row owned by <paramref name="userId"/>.
    /// Active (not soft-deleted) rows are left in place. Caller removes blobs when this returns true.
    /// </summary>
    public async Task<bool> PurgePendingForUserAsync(Uuid7 id, Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM screenshot
            WHERE id = @id AND user_id = @userId AND deleted_at IS NOT NULL
            """,
            new { id, userId }, cancellationToken: ct));
        var purged = n > 0;
        if (purged)
            _logger.LogInformation("Purged pending screenshot {Id} for user {UserId}", id, userId);
        return purged;
    }

    /// <summary>
    /// Deletes a screenshot row owned by <paramref name="userId"/>. Caller removes blobs.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="userId">Owning user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if a row was deleted.</returns>
    public async Task<bool> DeleteForUserAsync(Uuid7 id, Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM screenshot WHERE id = @id AND user_id = @userId",
            new { id, userId }, cancellationToken: ct));
        var deleted = n > 0;
        if (deleted)
            _logger.LogInformation("Deleted screenshot {Id} for user {UserId}", id, userId);
        else
            _logger.LogWarning("DeleteForUserAsync found no matching screenshot {Id} for user {UserId}", id, userId);
        return deleted;
    }

    private static List<Uuid7> DistinctIds(IReadOnlyCollection<Uuid7> ids) =>
        ids.Where(id => id != Uuid7.Empty).Distinct().ToList();

    /// <summary>
    /// Runs <paramref name="sql"/> once per id chunk. <c>{in}</c> is replaced with a bound id list.
    /// </summary>
    private async Task<int> ExecuteForIdsAsync(
        Uuid7 userId,
        IReadOnlyList<Uuid7> ids,
        string sql,
        object? extra,
        CancellationToken ct)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var total = 0;
        for (var offset = 0; offset < ids.Count; offset += 500)
        {
            var chunk = ids.Skip(offset).Take(500).ToList();
            var args = new DynamicParameters(extra);
            args.Add("userId", userId);
            var list = InClause.Add(args, chunk, "id");
            total += await conn.ExecuteAsync(new CommandDefinition(
                sql.Replace("{in}", list, StringComparison.Ordinal),
                args,
                cancellationToken: ct));
        }

        return total;
    }

    /// <summary>
    /// Next batch of screenshots (plus user/machine labels) after an optional keyset cursor.
    /// </summary>
    public async Task<IReadOnlyList<ScreenshotRewriteRow>> ListForRewriteAsync(
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        DateTimeOffset? afterTimestampUtc,
        Uuid7? afterId,
        int batch,
        CancellationToken ct = default)
    {
        if (batch < 1)
            batch = 1;
        if (batch > 1000)
            batch = 1000;

        var hasCursor = afterTimestampUtc is not null && afterId is not null ? 1 : 0;
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ScreenshotRewriteRow>(new CommandDefinition(
            $"""
            SELECT
                s.id, s.user_id, s.machine_id, s.timestamp_utc, s.client_time_zone_id,
                s.monitor_id, s.monitor_name, s.window_title, s.program_name, s.command_line,
                s.content_type, s.byte_size, s.width, s.height, s.sha256, s.fingerprint,
                s.storage_key, s.thumb_storage_key, s.notes, s.tags, s.journal_entry_id,
                u.code AS user_code, u.email AS user_email, u.name AS user_name,
                m.code AS machine_code, m.name AS machine_name
            FROM screenshot s
            INNER JOIN app_user u ON u.id = s.user_id
            INNER JOIN machine m ON m.id = s.machine_id
            WHERE s.timestamp_utc >= @sinceUtc
              AND s.timestamp_utc < @untilUtc
              AND (
                    @hasCursor = 0
                    OR s.timestamp_utc > @afterTs
                    OR (s.timestamp_utc = @afterTs AND s.id > @afterId)
                  )
            ORDER BY s.timestamp_utc, s.id
            {_sql.Limit(batch)}
            """,
            new
            {
                sinceUtc,
                untilUtc,
                hasCursor,
                afterTs = afterTimestampUtc ?? DateTimeOffset.UnixEpoch,
                afterId = afterId ?? default
            },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>How many screenshots fall in the rewrite window (same joins as <see cref="ListForRewriteAsync"/>).</summary>
    public async Task<int> CountForRewriteAsync(
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CAST(COUNT(*) AS INTEGER)
            FROM screenshot s
            INNER JOIN app_user u ON u.id = s.user_id
            INNER JOIN machine m ON m.id = s.machine_id
            WHERE s.timestamp_utc >= @sinceUtc
              AND s.timestamp_utc < @untilUtc
            """,
            new { sinceUtc, untilUtc },
            cancellationToken: ct));
    }

    /// <summary>Screenshots in a UTC range, optionally filtered by exact monitor name.</summary>
    public async Task<IReadOnlyList<ScreenshotIdleRow>> ListForIdleScanAsync(
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        string? monitorName,
        int? limit,
        CancellationToken ct = default)
    {
        var take = limit is > 0 ? Math.Min(limit.Value, 500_000) : 0;
        var limitSql = take > 0 ? _sql.Limit(take) : "";
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ScreenshotIdleRow>(new CommandDefinition(
            $"""
            SELECT
                id, timestamp_utc, client_time_zone_id, monitor_name,
                storage_key, width, height, byte_size
            FROM screenshot
            WHERE timestamp_utc >= @sinceUtc
              AND timestamp_utc < @untilUtc
              AND (@monitorName IS NULL OR monitor_name = @monitorName)
            ORDER BY timestamp_utc, id
            {limitSql}
            """,
            new { sinceUtc, untilUtc, monitorName },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>All users' screenshots in a UTC range with machine name and stored fingerprint.</summary>
    public async Task<IReadOnlyList<ScreenshotDuplicateRow>> ListForDuplicateScanAsync(
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        string? monitorName,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ScreenshotDuplicateRow>(new CommandDefinition(
            """
            SELECT
                s.id, s.user_id, s.machine_id, m.name AS machine_name, s.timestamp_utc,
                s.client_time_zone_id, s.monitor_id, s.monitor_name, s.storage_key,
                s.width, s.height, s.fingerprint
            FROM screenshot s
            LEFT JOIN machine m ON m.id = s.machine_id
            WHERE s.timestamp_utc >= @sinceUtc
              AND s.timestamp_utc < @untilUtc
              AND (@monitorName IS NULL OR s.monitor_name = @monitorName)
            ORDER BY s.timestamp_utc, s.id
            """,
            new { sinceUtc, untilUtc, monitorName },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Monitor names and row counts in a UTC range.</summary>
    public async Task<IReadOnlyList<ScreenshotMonitorCount>> ListMonitorCountsAsync(
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ScreenshotMonitorCount>(new CommandDefinition(
            """
            SELECT monitor_name, CAST(COUNT(*) AS INTEGER) AS n
            FROM screenshot
            WHERE timestamp_utc >= @sinceUtc
              AND timestamp_utc < @untilUtc
            GROUP BY monitor_name
            ORDER BY n DESC
            """,
            new { sinceUtc, untilUtc },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Points the row at its largest thumb tier (replaces a legacy <c>.thumb.jpg</c> key).</summary>
    public async Task UpdateThumbStorageKeyAsync(Uuid7 id, string thumbStorageKey, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE screenshot SET thumb_storage_key = @thumbStorageKey WHERE id = @id",
            new { id, thumbStorageKey },
            cancellationToken: ct));
    }

    /// <summary>Updates stored original/thumb keys, hash, size, and dimensions after rewrite.</summary>
    public async Task UpdateStoredBlobAsync(
        Uuid7 id,
        string contentType,
        long byteSize,
        int width,
        int height,
        string sha256,
        string storageKey,
        string thumbStorageKey,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE screenshot
            SET content_type = @contentType,
                byte_size = @byteSize,
                width = @width,
                height = @height,
                sha256 = @sha256,
                storage_key = @storageKey,
                thumb_storage_key = @thumbStorageKey
            WHERE id = @id
            """,
            new { id, contentType, byteSize, width, height, sha256, storageKey, thumbStorageKey },
            cancellationToken: ct));
        if (n == 0)
            _logger.LogWarning("UpdateStoredBlobAsync found no screenshot {Id}", id);
        else
            _logger.LogDebug("Updated stored blob for screenshot {Id} key={StorageKey} bytes={ByteSize}", id, storageKey, byteSize);
    }
}

