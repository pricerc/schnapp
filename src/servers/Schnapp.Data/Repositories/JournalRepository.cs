namespace Schnapp.Data.Repositories;

using System.Data;

using Dapper;

using Microsoft.Extensions.Logging;

using Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Dapper access for <c>journal_entry</c> (list, open lookup, insert, close, update).
/// </summary>
/// <remarks>
/// One open entry per (user, machine) is enforced at the API layer (auto-close on start),
/// not with a DB unique constraint on open rows. Registered scoped via
/// <see cref="ServiceCollectionExtensions.AddSchnappData"/>.
/// </remarks>
public sealed class JournalRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDatabaseDialect _sql;
    private readonly ILogger<JournalRepository> _logger;

    /// <summary>
    /// Creates a repository bound to the shared connection factory.
    /// </summary>
    /// <param name="db">Factory that opens catalog connections.</param>
    /// <param name="sql">Active catalog dialect.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public JournalRepository(IDbConnectionFactory db, IDatabaseDialect sql, ILogger<JournalRepository> logger)
    {
        _db = db;
        _sql = sql;
        _logger = logger;
    }

    /// <summary>
    /// Lists journal entries for a user, newest start time first.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="limit">Max rows (default 100).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<JournalEntry>> ListForUserAsync(
        Uuid7 userId, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<JournalEntry>(new CommandDefinition(
            $"""
            SELECT * FROM journal_entry
            WHERE user_id = @userId
            ORDER BY start_utc DESC
            {_sql.Limit(limit)}
            """,
            new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Lists journal entries for a user with optional start-date range and C/P/T/M include/exclude
    /// filters (same match rules as time-summary reports). Newest start first.
    /// </summary>
    /// <param name="rangeStartUtc">Inclusive lower bound on <c>start_utc</c>; null = no lower bound.</param>
    /// <param name="rangeEndUtc">Exclusive upper bound on <c>start_utc</c>; null = no upper bound.</param>
    /// <param name="noneId">NONE sentinel id for unused filter placeholders.</param>
    /// <param name="filters">Include/exclude customer/project/task/machine filters.</param>
    /// <param name="limit">Max rows (default 1000).</param>
    public async Task<IReadOnlyList<JournalEntry>> ListForUserFilteredAsync(
        Uuid7 userId,
        DateTimeOffset? rangeStartUtc,
        DateTimeOffset? rangeEndUtc,
        Uuid7 noneId,
        CptFilterSet? filters = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        filters ??= new CptFilterSet();
        var b = BindCpt(filters, noneId);
        var lc = ListLabelSql("c");
        var lp = ListLabelSql("p");
        var lt = ListLabelSql("t");
        var lm = ListLabelSql("m");
        var lpc = ListLabelSql("pc");
        var ltc = ListLabelSql("tc");
        var ltp = ListLabelSql("tp");
        var useStart = rangeStartUtc is not null;
        var useEnd = rangeEndUtc is not null;

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<JournalEntry>(new CommandDefinition(
            $"""
            SELECT je.*
            FROM journal_entry je
            INNER JOIN machine m ON m.id = je.machine_id
            INNER JOIN journal_target jt ON jt.id = je.journal_target_id
            INNER JOIN customer c ON c.id = jt.customer_id
            INNER JOIN project p ON p.id = jt.project_id
            INNER JOIN task t ON t.id = jt.task_id
            LEFT JOIN customer pc ON pc.id = p.customer_id
            LEFT JOIN customer tc ON tc.id = t.customer_id
            LEFT JOIN project tp ON tp.id = t.project_id
            WHERE je.user_id = @userId
              AND (@useStart = false OR je.start_utc >= @rangeStartUtc)
              AND (@useEnd = false OR je.start_utc < @rangeEndUtc)
            {CptWhereSql(lc, lp, lt, lm, lpc, ltc, ltp)}
            ORDER BY je.start_utc DESC
            {_sql.Limit(limit)}
            """,
            new
            {
                userId,
                useStart,
                useEnd,
                rangeStartUtc = rangeStartUtc ?? DateTimeOffset.UnixEpoch,
                rangeEndUtc = rangeEndUtc ?? DateTimeOffset.UnixEpoch,
                limit,
                incCustOn = b.IncCustOn,
                incCustById = b.IncCustById,
                incCustId = b.IncCustId,
                incCustPat = b.IncCustPat,
                incProjOn = b.IncProjOn,
                incProjById = b.IncProjById,
                incProjId = b.IncProjId,
                incProjPat = b.IncProjPat,
                incTaskOn = b.IncTaskOn,
                incTaskById = b.IncTaskById,
                incTaskId = b.IncTaskId,
                incTaskPat = b.IncTaskPat,
                incMachOn = b.IncMachOn,
                incMachById = b.IncMachById,
                incMachId = b.IncMachId,
                incMachPat = b.IncMachPat,
                excCustOn = b.ExcCustOn,
                excCustById = b.ExcCustById,
                excCustId = b.ExcCustId,
                excCustPat = b.ExcCustPat,
                excProjOn = b.ExcProjOn,
                excProjById = b.ExcProjById,
                excProjId = b.ExcProjId,
                excProjPat = b.ExcProjPat,
                excTaskOn = b.ExcTaskOn,
                excTaskById = b.ExcTaskById,
                excTaskId = b.ExcTaskId,
                excTaskPat = b.ExcTaskPat,
                excMachOn = b.ExcMachOn,
                excMachById = b.ExcMachById,
                excMachId = b.ExcMachId,
                excMachPat = b.ExcMachPat
            },
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Loads one journal entry owned by <paramref name="userId"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="id">Journal entry id.</param>
    /// <param name="userId">Owning user id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<JournalEntry?> GetForUserAsync(Uuid7 id, Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<JournalEntry>(new CommandDefinition(
            "SELECT * FROM journal_entry WHERE id = @id AND user_id = @userId",
            new { id, userId }, cancellationToken: ct));
    }

    /// <summary>
    /// Returns the open (no stop time) entry for the given user and machine, if any.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="machineId">Machine the open entry is tied to.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<JournalEntry?> GetOpenAsync(Uuid7 userId, Uuid7 machineId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<JournalEntry>(new CommandDefinition(
            """
            SELECT * FROM journal_entry
            WHERE user_id = @userId AND machine_id = @machineId AND stop_utc IS NULL
            """,
            new { userId, machineId }, cancellationToken: ct));
    }

    /// <summary>
    /// Returns all open (no stop time) journal entries for a user.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<JournalEntry>> ListOpenForUserAsync(Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<JournalEntry>(new CommandDefinition(
            """
            SELECT * FROM journal_entry
            WHERE user_id = @userId AND stop_utc IS NULL
            ORDER BY start_utc
            """,
            new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Inserts a new journal entry row.
    /// </summary>
    /// <param name="entry">Entity to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InsertAsync(JournalEntry entry, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO journal_entry (
                id, user_id, machine_id, start_utc, stop_utc, journal_target_id,
                notes, tags, created_at, updated_at)
            VALUES (
                @Id, @UserId, @MachineId, @StartUtc, @StopUtc, @JournalTargetId,
                @Notes, @Tags, @CreatedAt, @UpdatedAt)
            """,
            entry, cancellationToken: ct));
        _logger.LogInformation("Inserted journal entry {Id} for user {UserId}", entry.Id, entry.UserId);
    }

    /// <summary>
    /// Sets <c>stop_utc</c> on an open entry owned by the user (no-op if already closed).
    /// </summary>
    /// <param name="id">Journal entry id.</param>
    /// <param name="userId">Owning user id.</param>
    /// <param name="stopUtc">Close timestamp (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CloseAsync(Uuid7 id, Uuid7 userId, DateTimeOffset stopUtc, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE journal_entry
            SET stop_utc = @stopUtc, updated_at = @stopUtc
            WHERE id = @id AND user_id = @userId AND stop_utc IS NULL
            """,
            new { id, userId, stopUtc }, cancellationToken: ct));
    }

    /// <summary>
    /// Updates start/stop, target, notes, and tags for an entry owned by <see cref="JournalEntry.UserId"/>.
    /// </summary>
    /// <param name="entry">Entity with fields to persist (id + user id identify the row).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateAsync(JournalEntry entry, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE journal_entry SET
                start_utc = @StartUtc,
                stop_utc = @StopUtc,
                journal_target_id = @JournalTargetId,
                notes = @Notes,
                tags = @Tags,
                updated_at = @UpdatedAt
            WHERE id = @Id AND user_id = @UserId
            """,
            entry, cancellationToken: ct));
    }

    /// <summary>
    /// Deletes a journal entry owned by <paramref name="userId"/> after clearing screenshot links.
    /// </summary>
    /// <returns>True if a row was deleted.</returns>
    public async Task<bool> DeleteAsync(Uuid7 id, Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE screenshot
                SET journal_entry_id = NULL
                WHERE journal_entry_id = @id AND user_id = @userId
                """,
                new { id, userId }, transaction: tx, cancellationToken: ct));

            var n = await conn.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM journal_entry
                WHERE id = @id AND user_id = @userId
                """,
                new { id, userId }, transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            if (n > 0)
                _logger.LogInformation("Deleted journal entry {Id} for user {UserId}", id, userId);
            return n > 0;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Aggregates journal time for a user over a UTC half-open range, grouped by local start day
    /// and customer/project/task. Open segments use <paramref name="nowUtc"/> as stop.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="rangeStartUtc">Inclusive range start (UTC).</param>
    /// <param name="rangeEndUtc">Exclusive range end (UTC).</param>
    /// <param name="timeZoneId">IANA zone for local date of <c>start_utc</c>.</param>
    /// <param name="nowUtc">Current time used for open entries and overlap.</param>
    /// <param name="noneId">NONE list id (placeholder for unused id params).</param>
    /// <param name="filters">Include/exclude C/P/T field filters (AND of all active clauses).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<JournalTimeSummaryRow>> SummarizeTimeAsync(
        Uuid7 userId,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        string timeZoneId,
        DateTimeOffset nowUtc,
        Uuid7 noneId,
        CptFilterSet? filters = null,
        CancellationToken ct = default)
    {
        filters ??= new CptFilterSet();
        var b = BindCpt(filters, noneId);

        // Label expressions aligned with Web FormatListLabel / FormatPath for free-text path match.
        var lc = ListLabelSql("c");
        var lp = ListLabelSql("p");
        var lt = ListLabelSql("t");
        var lm = ListLabelSql("m");
        var lpc = ListLabelSql("pc");
        var ltc = ListLabelSql("tc");
        var ltp = ListLabelSql("tp");

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        if (_sql.Provider == DatabaseProvider.Firebird)
            return await SummarizeTimeFirebirdAsync(conn, userId, rangeStartUtc, rangeEndUtc, timeZoneId, nowUtc, b, lc, lp, lt, lm, lpc, ltc, ltp, ct);

        var rows = await conn.QueryAsync<JournalTimeSummaryRow>(new CommandDefinition(
            $"""
            SELECT
                to_char(
                    ((je.start_utc AT TIME ZONE 'UTC') AT TIME ZONE @timeZoneId)::date,
                    'YYYY-MM-DD') AS LocalDate,
                m.id AS MachineId,
                m.code AS MachineCode,
                m.name AS MachineName,
                c.id AS CustomerId,
                c.code AS CustomerCode,
                c.name AS CustomerName,
                p.id AS ProjectId,
                p.code AS ProjectCode,
                p.name AS ProjectName,
                t.id AS TaskId,
                t.code AS TaskCode,
                t.name AS TaskName,
                SUM(GREATEST(0, EXTRACT(EPOCH FROM (
                    COALESCE(je.stop_utc, @nowUtc) - je.start_utc
                ))))::bigint AS TotalSeconds,
                COUNT(*)::int AS EntryCount,
                MIN(shots.first_ts) AS FirstScreenshotUtc,
                MAX(shots.last_ts) AS LastScreenshotUtc
            FROM journal_entry je
            INNER JOIN machine m ON m.id = je.machine_id
            INNER JOIN journal_target jt ON jt.id = je.journal_target_id
            INNER JOIN customer c ON c.id = jt.customer_id
            INNER JOIN project p ON p.id = jt.project_id
            INNER JOIN task t ON t.id = jt.task_id
            -- Parent list rows for hierarchical free-text path matching (C › P › T).
            LEFT JOIN customer pc ON pc.id = p.customer_id
            LEFT JOIN customer tc ON tc.id = t.customer_id
            LEFT JOIN project tp ON tp.id = t.project_id
            LEFT JOIN LATERAL (
                SELECT
                    MIN(s.timestamp_utc) AS first_ts,
                    MAX(s.timestamp_utc) AS last_ts
                FROM screenshot s
                WHERE s.user_id = je.user_id
                  AND s.deleted_at IS NULL
                  AND s.timestamp_utc >= je.start_utc
                  AND s.timestamp_utc <= COALESCE(je.stop_utc, @nowUtc)
            ) shots ON true
            WHERE je.user_id = @userId
              AND je.start_utc < @rangeEndUtc
              AND COALESCE(je.stop_utc, @nowUtc) > @rangeStartUtc
            {CptWhereSql(lc, lp, lt, lm, lpc, ltc, ltp)}
            GROUP BY
                ((je.start_utc AT TIME ZONE 'UTC') AT TIME ZONE @timeZoneId)::date,
                m.id, m.code, m.name,
                c.id, c.code, c.name,
                p.id, p.code, p.name,
                t.id, t.code, t.name
            ORDER BY
                ((je.start_utc AT TIME ZONE 'UTC') AT TIME ZONE @timeZoneId)::date DESC,
                m.code, c.code, p.code, t.code
            """,
            new
            {
                userId,
                rangeStartUtc,
                rangeEndUtc,
                timeZoneId,
                nowUtc,
                incCustOn = b.IncCustOn,
                incCustById = b.IncCustById,
                incCustId = b.IncCustId,
                incCustPat = b.IncCustPat,
                incProjOn = b.IncProjOn,
                incProjById = b.IncProjById,
                incProjId = b.IncProjId,
                incProjPat = b.IncProjPat,
                incTaskOn = b.IncTaskOn,
                incTaskById = b.IncTaskById,
                incTaskId = b.IncTaskId,
                incTaskPat = b.IncTaskPat,
                incMachOn = b.IncMachOn,
                incMachById = b.IncMachById,
                incMachId = b.IncMachId,
                incMachPat = b.IncMachPat,
                excCustOn = b.ExcCustOn,
                excCustById = b.ExcCustById,
                excCustId = b.ExcCustId,
                excCustPat = b.ExcCustPat,
                excProjOn = b.ExcProjOn,
                excProjById = b.ExcProjById,
                excProjId = b.ExcProjId,
                excProjPat = b.ExcProjPat,
                excTaskOn = b.ExcTaskOn,
                excTaskById = b.ExcTaskById,
                excTaskId = b.ExcTaskId,
                excTaskPat = b.ExcTaskPat,
                excMachOn = b.ExcMachOn,
                excMachById = b.ExcMachById,
                excMachId = b.ExcMachId,
                excMachPat = b.ExcMachPat
            },
            cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<IReadOnlyList<JournalTimeSummaryRow>> SummarizeTimeFirebirdAsync(
        System.Data.Common.DbConnection conn,
        Uuid7 userId,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        string timeZoneId,
        DateTimeOffset nowUtc,
        CptSqlBind b,
        string lc, string lp, string lt, string lm, string lpc, string ltc, string ltp,
        CancellationToken ct)
    {
        var detail = await conn.QueryAsync<FirebirdTimeSummaryDetail>(new CommandDefinition(
            $"""
            SELECT
                je.start_utc AS StartUtc,
                m.id AS MachineId,
                m.code AS MachineCode,
                m.name AS MachineName,
                c.id AS CustomerId,
                c.code AS CustomerCode,
                c.name AS CustomerName,
                p.id AS ProjectId,
                p.code AS ProjectCode,
                p.name AS ProjectName,
                t.id AS TaskId,
                t.code AS TaskCode,
                t.name AS TaskName,
                MAXVALUE(0, DATEDIFF(SECOND FROM je.start_utc TO COALESCE(je.stop_utc, @nowUtc))) AS TotalSeconds,
                (SELECT MIN(s.timestamp_utc) FROM screenshot s
                 WHERE s.user_id = je.user_id
                   AND s.deleted_at IS NULL
                   AND s.timestamp_utc >= je.start_utc
                   AND s.timestamp_utc <= COALESCE(je.stop_utc, @nowUtc)) AS FirstScreenshotUtc,
                (SELECT MAX(s.timestamp_utc) FROM screenshot s
                 WHERE s.user_id = je.user_id
                   AND s.deleted_at IS NULL
                   AND s.timestamp_utc >= je.start_utc
                   AND s.timestamp_utc <= COALESCE(je.stop_utc, @nowUtc)) AS LastScreenshotUtc
            FROM journal_entry je
            INNER JOIN machine m ON m.id = je.machine_id
            INNER JOIN journal_target jt ON jt.id = je.journal_target_id
            INNER JOIN customer c ON c.id = jt.customer_id
            INNER JOIN project p ON p.id = jt.project_id
            INNER JOIN task t ON t.id = jt.task_id
            LEFT JOIN customer pc ON pc.id = p.customer_id
            LEFT JOIN customer tc ON tc.id = t.customer_id
            LEFT JOIN project tp ON tp.id = t.project_id
            WHERE je.user_id = @userId
              AND je.start_utc < @rangeEndUtc
              AND COALESCE(je.stop_utc, @nowUtc) > @rangeStartUtc
            {CptWhereSql(lc, lp, lt, lm, lpc, ltc, ltp)}
            """,
            new
            {
                userId,
                rangeStartUtc,
                rangeEndUtc,
                nowUtc,
                incCustOn = b.IncCustOn,
                incCustById = b.IncCustById,
                incCustId = b.IncCustId,
                incCustPat = b.IncCustPat,
                incProjOn = b.IncProjOn,
                incProjById = b.IncProjById,
                incProjId = b.IncProjId,
                incProjPat = b.IncProjPat,
                incTaskOn = b.IncTaskOn,
                incTaskById = b.IncTaskById,
                incTaskId = b.IncTaskId,
                incTaskPat = b.IncTaskPat,
                incMachOn = b.IncMachOn,
                incMachById = b.IncMachById,
                incMachId = b.IncMachId,
                incMachPat = b.IncMachPat,
                excCustOn = b.ExcCustOn,
                excCustById = b.ExcCustById,
                excCustId = b.ExcCustId,
                excCustPat = b.ExcCustPat,
                excProjOn = b.ExcProjOn,
                excProjById = b.ExcProjById,
                excProjId = b.ExcProjId,
                excProjPat = b.ExcProjPat,
                excTaskOn = b.ExcTaskOn,
                excTaskById = b.ExcTaskById,
                excTaskId = b.ExcTaskId,
                excTaskPat = b.ExcTaskPat,
                excMachOn = b.ExcMachOn,
                excMachById = b.ExcMachById,
                excMachId = b.ExcMachId,
                excMachPat = b.ExcMachPat
            },
            cancellationToken: ct));

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        return detail
            .GroupBy(r => new
            {
                LocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.StartUtc.UtcDateTime, tz))
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                r.MachineId, r.MachineCode, r.MachineName,
                r.CustomerId, r.CustomerCode, r.CustomerName,
                r.ProjectId, r.ProjectCode, r.ProjectName,
                r.TaskId, r.TaskCode, r.TaskName
            })
            .Select(g => new JournalTimeSummaryRow
            {
                LocalDate = g.Key.LocalDate,
                MachineId = g.Key.MachineId,
                MachineCode = g.Key.MachineCode,
                MachineName = g.Key.MachineName,
                CustomerId = g.Key.CustomerId,
                CustomerCode = g.Key.CustomerCode,
                CustomerName = g.Key.CustomerName,
                ProjectId = g.Key.ProjectId,
                ProjectCode = g.Key.ProjectCode,
                ProjectName = g.Key.ProjectName,
                TaskId = g.Key.TaskId,
                TaskCode = g.Key.TaskCode,
                TaskName = g.Key.TaskName,
                TotalSeconds = g.Sum(x => x.TotalSeconds),
                EntryCount = g.Count(),
                FirstScreenshotUtc = g.Min(x => x.FirstScreenshotUtc),
                LastScreenshotUtc = g.Max(x => x.LastScreenshotUtc)
            })
            .OrderByDescending(r => r.LocalDate)
            .ThenBy(r => r.MachineCode)
            .ThenBy(r => r.CustomerCode)
            .ThenBy(r => r.ProjectCode)
            .ThenBy(r => r.TaskCode)
            .ToList();
    }

    private sealed class FirebirdTimeSummaryDetail
    {
        public DateTimeOffset StartUtc { get; set; }
        public Uuid7 MachineId { get; set; }
        public string MachineCode { get; set; } = "";
        public string MachineName { get; set; } = "";
        public Uuid7 CustomerId { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public Uuid7 ProjectId { get; set; }
        public string ProjectCode { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public Uuid7 TaskId { get; set; }
        public string TaskCode { get; set; } = "";
        public string TaskName { get; set; } = "";
        public long TotalSeconds { get; set; }
        public DateTimeOffset? FirstScreenshotUtc { get; set; }
        public DateTimeOffset? LastScreenshotUtc { get; set; }
    }

    /// <summary>
    /// SQL expression matching Web <c>FormatListLabel</c> for alias <paramref name="a"/> (code/name columns).
    /// </summary>
    private static string ListLabelSql(string a) =>
        $"""
        CASE
          WHEN (coalesce(nullif(trim({a}.code), ''), null) IS NULL)
           AND (coalesce(nullif(trim({a}.name), ''), null) IS NULL) THEN '(none)'
          WHEN coalesce(nullif(trim({a}.code), ''), null) IS NULL THEN {a}.name
          WHEN coalesce(nullif(trim({a}.name), ''), null) IS NULL THEN {a}.code
          WHEN {a}.code = {a}.name THEN {a}.name
          ELSE {a}.code || ' — ' || {a}.name
        END
        """;

    private sealed class CptSqlBind
    {
        public bool IncCustOn, IncCustById; public Uuid7 IncCustId; public string IncCustPat = "%";
        public bool IncProjOn, IncProjById; public Uuid7 IncProjId; public string IncProjPat = "%";
        public bool IncTaskOn, IncTaskById; public Uuid7 IncTaskId; public string IncTaskPat = "%";
        public bool IncMachOn, IncMachById; public Uuid7 IncMachId; public string IncMachPat = "%";
        public bool ExcCustOn, ExcCustById; public Uuid7 ExcCustId; public string ExcCustPat = "%";
        public bool ExcProjOn, ExcProjById; public Uuid7 ExcProjId; public string ExcProjPat = "%";
        public bool ExcTaskOn, ExcTaskById; public Uuid7 ExcTaskId; public string ExcTaskPat = "%";
        public bool ExcMachOn, ExcMachById; public Uuid7 ExcMachId; public string ExcMachPat = "%";
    }

    private static CptSqlBind BindCpt(CptFilterSet f, Uuid7 noneId)
    {
        static void One(CptFieldFilter x, Uuid7 none, out bool on, out bool byId, out Uuid7 id, out string pat)
        {
            if (!x.IsActive)
            {
                on = false; byId = false; id = none; pat = "%";
                return;
            }
            if (x.Id is { } exact)
            {
                on = true; byId = true; id = exact; pat = "%";
                return;
            }
            var raw = (x.Text ?? "").Trim().ToLowerInvariant()
                .Replace("%", "", StringComparison.Ordinal)
                .Replace("_", "", StringComparison.Ordinal);
            on = true; byId = false; id = none; pat = "%" + raw + "%";
        }

        var b = new CptSqlBind();
        One(f.IncludeCustomer, noneId, out b.IncCustOn, out b.IncCustById, out b.IncCustId, out b.IncCustPat);
        One(f.IncludeProject, noneId, out b.IncProjOn, out b.IncProjById, out b.IncProjId, out b.IncProjPat);
        One(f.IncludeTask, noneId, out b.IncTaskOn, out b.IncTaskById, out b.IncTaskId, out b.IncTaskPat);
        One(f.IncludeMachine, noneId, out b.IncMachOn, out b.IncMachById, out b.IncMachId, out b.IncMachPat);
        One(f.ExcludeCustomer, noneId, out b.ExcCustOn, out b.ExcCustById, out b.ExcCustId, out b.ExcCustPat);
        One(f.ExcludeProject, noneId, out b.ExcProjOn, out b.ExcProjById, out b.ExcProjId, out b.ExcProjPat);
        One(f.ExcludeTask, noneId, out b.ExcTaskOn, out b.ExcTaskById, out b.ExcTaskId, out b.ExcTaskPat);
        One(f.ExcludeMachine, noneId, out b.ExcMachOn, out b.ExcMachById, out b.ExcMachId, out b.ExcMachPat);
        return b;
    }

    /// <summary>SQL WHERE clauses for include/exclude on C/P/T/M (aliases c,p,t,m + path parents).</summary>
    private static string CptWhereSql(string lc, string lp, string lt, string lm, string lpc, string ltc, string ltp) =>
        $"""
              AND (
                    @incCustOn = false
                    OR (@incCustById = true AND c.id = @incCustId)
                    OR (@incCustById = false AND (
                          lower(c.code) LIKE @incCustPat
                          OR lower(c.name) LIKE @incCustPat
                          OR lower({lc}) LIKE @incCustPat
                        ))
                  )
              AND (
                    @incProjOn = false
                    OR (@incProjById = true AND p.id = @incProjId)
                    OR (@incProjById = false AND (
                          lower(p.code) LIKE @incProjPat
                          OR lower(p.name) LIKE @incProjPat
                          OR lower({lp}) LIKE @incProjPat
                          OR lower({lpc} || ' › ' || {lp}) LIKE @incProjPat
                        ))
                  )
              AND (
                    @incTaskOn = false
                    OR (@incTaskById = true AND t.id = @incTaskId)
                    OR (@incTaskById = false AND (
                          lower(t.code) LIKE @incTaskPat
                          OR lower(t.name) LIKE @incTaskPat
                          OR lower({lt}) LIKE @incTaskPat
                          OR lower({ltc} || ' › ' || {ltp} || ' › ' || {lt}) LIKE @incTaskPat
                        ))
                  )
              AND (
                    @incMachOn = false
                    OR (@incMachById = true AND m.id = @incMachId)
                    OR (@incMachById = false AND (
                          lower(m.code) LIKE @incMachPat
                          OR lower(m.name) LIKE @incMachPat
                          OR lower({lm}) LIKE @incMachPat
                        ))
                  )
              AND (
                    @excCustOn = false
                    OR NOT (
                      (@excCustById = true AND c.id = @excCustId)
                      OR (@excCustById = false AND (
                            lower(c.code) LIKE @excCustPat
                            OR lower(c.name) LIKE @excCustPat
                            OR lower({lc}) LIKE @excCustPat
                          ))
                    )
                  )
              AND (
                    @excProjOn = false
                    OR NOT (
                      (@excProjById = true AND p.id = @excProjId)
                      OR (@excProjById = false AND (
                            lower(p.code) LIKE @excProjPat
                            OR lower(p.name) LIKE @excProjPat
                            OR lower({lp}) LIKE @excProjPat
                            OR lower({lpc} || ' › ' || {lp}) LIKE @excProjPat
                          ))
                    )
                  )
              AND (
                    @excTaskOn = false
                    OR NOT (
                      (@excTaskById = true AND t.id = @excTaskId)
                      OR (@excTaskById = false AND (
                            lower(t.code) LIKE @excTaskPat
                            OR lower(t.name) LIKE @excTaskPat
                            OR lower({lt}) LIKE @excTaskPat
                            OR lower({ltc} || ' › ' || {ltp} || ' › ' || {lt}) LIKE @excTaskPat
                          ))
                    )
                  )
              AND (
                    @excMachOn = false
                    OR NOT (
                      (@excMachById = true AND m.id = @excMachId)
                      OR (@excMachById = false AND (
                            lower(m.code) LIKE @excMachPat
                            OR lower(m.name) LIKE @excMachPat
                            OR lower({lm}) LIKE @excMachPat
                          ))
                    )
                  )
        """;
}

/// <summary>Dapper row for <see cref="JournalRepository.SummarizeTimeAsync"/>.</summary>
public sealed class JournalTimeSummaryRow
{
    public string LocalDate { get; set; } = "";
    public Uuid7 MachineId { get; set; }
    public string MachineCode { get; set; } = "";
    public string MachineName { get; set; } = "";
    public Uuid7 CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public Uuid7 ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public Uuid7 TaskId { get; set; }
    public string TaskCode { get; set; } = "";
    public string TaskName { get; set; } = "";
    public long TotalSeconds { get; set; }
    public int EntryCount { get; set; }
    public DateTimeOffset? FirstScreenshotUtc { get; set; }
    public DateTimeOffset? LastScreenshotUtc { get; set; }
}

