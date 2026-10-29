namespace Schnapp.Data.Repositories;

using Dapper;

using Microsoft.Extensions.Logging;

using Schnapp.Domain.Entities;

/// <summary>
/// Org key/value settings stored in the <c>setting</c> table.
/// </summary>
/// <remarks>
/// Registered scoped via <see cref="ServiceCollectionExtensions.AddSchnappData"/>.
/// Well-known keys live in <see cref="Domain.SettingKeys"/>. Use typed helpers
/// (<see cref="GetIntAsync"/>, <see cref="GetLongAsync"/>) so callers get safe defaults
/// when a row is missing or unparsable.
/// </remarks>
public sealed class SettingRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDatabaseDialect _sql;
    private readonly ILogger<SettingRepository> _logger;

    /// <summary>
    /// Creates a repository bound to the shared connection factory.
    /// </summary>
    /// <param name="db">Connection factory for the Schnapp database.</param>
    /// <param name="sql">Active catalog dialect.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public SettingRepository(IDbConnectionFactory db, IDatabaseDialect sql, ILogger<SettingRepository> logger)
    {
        _db = db;
        _sql = sql;
        _logger = logger;
    }

    /// <summary>
    /// Lists all settings ordered by key.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All setting rows.</returns>
    public async Task<IReadOnlyList<Setting>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Setting>(new CommandDefinition(
            $"SELECT * FROM setting ORDER BY {_sql.Ident("key")}", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Loads one setting by key, or <see langword="null"/> if missing.
    /// </summary>
    /// <param name="key">Setting key (e.g. from <see cref="Domain.SettingKeys"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Setting?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var setting = await conn.QuerySingleOrDefaultAsync<Setting>(new CommandDefinition(
            $"SELECT * FROM setting WHERE {_sql.Ident("key")} = @key", new { key }, cancellationToken: ct));
        if (setting is null)
            _logger.LogDebug("Setting {Key} not found", key);
        return setting;
    }

    /// <summary>
    /// Reads a setting as <see cref="int"/>, or <paramref name="defaultValue"/> if missing/invalid.
    /// </summary>
    /// <param name="key">Setting key.</param>
    /// <param name="defaultValue">Fallback when the row is absent or not a positive/valid int.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="requirePositive">When true, non-positive parsed values fall back to default.</param>
    /// <returns>Parsed value or default.</returns>
    /// <remarks>
    /// Called from journal auto-close and thumbnail sizing so ops can tune without redeploy.
    /// </remarks>
    public async Task<int> GetIntAsync(
        string key, int defaultValue, CancellationToken ct = default, bool requirePositive = true)
    {
        var row = await GetAsync(key, ct);
        if (row is null || !int.TryParse(row.Value, out var n))
            return defaultValue;
        if (requirePositive && n <= 0)
            return defaultValue;
        return n;
    }

    /// <summary>
    /// Reads a setting as <see cref="long"/>, or <paramref name="defaultValue"/> if missing/invalid.
    /// </summary>
    /// <param name="key">Setting key.</param>
    /// <param name="defaultValue">Fallback when the row is absent or not a positive/valid long.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="requirePositive">When true, non-positive parsed values fall back to default.</param>
    /// <returns>Parsed value or default.</returns>
    /// <remarks>
    /// Used for <c>screenshot.max_upload_bytes</c> on each upload request.
    /// </remarks>
    public async Task<long> GetLongAsync(
        string key, long defaultValue, CancellationToken ct = default, bool requirePositive = true)
    {
        var row = await GetAsync(key, ct);
        if (row is null || !long.TryParse(row.Value, out var n))
            return defaultValue;
        if (requirePositive && n <= 0)
            return defaultValue;
        return n;
    }

    /// <summary>
    /// Inserts or updates a setting value (admin write path).
    /// </summary>
    /// <param name="key">Setting key.</param>
    /// <param name="value">New string value.</param>
    /// <param name="notes">Optional notes; null leaves existing notes unchanged on update.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpsertAsync(string key, string value, string? notes, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var updatedAt = DateTimeOffset.UtcNow;
        var k = _sql.Ident("key");
        var v = _sql.Ident("value");
        var args = new { key, value, notes, updatedAt };
        if (_sql.Provider == DatabaseProvider.Firebird)
        {
            // MERGE + untyped parameters yields SQL -804 (data type unknown) on Firebird 5.
            var n = await conn.ExecuteAsync(new CommandDefinition(
                $"""
                UPDATE setting SET {v} = @value, notes = COALESCE(@notes, notes), updated_at = @updatedAt
                WHERE {k} = @key
                """,
                args, cancellationToken: ct));
            if (n == 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    $"""
                    INSERT INTO setting ({k}, {v}, notes, updated_at)
                    VALUES (@key, @value, @notes, @updatedAt)
                    """,
                    args, cancellationToken: ct));
            }
        }
        else
        {
            await conn.ExecuteAsync(new CommandDefinition(
                $"""
                INSERT INTO setting ({k}, {v}, notes, updated_at)
                VALUES (@key, @value, @notes, @updatedAt)
                ON CONFLICT ({k}) DO UPDATE SET
                    {v} = EXCLUDED.{v},
                    notes = COALESCE(EXCLUDED.notes, setting.notes),
                    updated_at = @updatedAt
                """,
                args, cancellationToken: ct));
        }
        _logger.LogInformation("Upserted setting {Key} = {Value}", key, value);
    }
}
