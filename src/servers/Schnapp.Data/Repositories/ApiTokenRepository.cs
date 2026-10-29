namespace Schnapp.Data.Repositories;

using Dapper;

using Microsoft.Extensions.Logging;

using Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Dapper data access for the <c>api_token</c> table (long-lived bearer credentials).
/// </summary>
/// <remarks>
/// Only the SHA-256 hash of a token is stored. Plaintext is shown once at creation/login
/// and never written to the database. Used by auth endpoints and
/// bearer token middleware.
/// </remarks>
public sealed class ApiTokenRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ApiTokenRepository> _logger;

    /// <summary>
    /// Creates a repository bound to the shared connection factory.
    /// </summary>
    /// <param name="db">Factory that opens Npgsql connections.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public ApiTokenRepository(IDbConnectionFactory db, ILogger<ApiTokenRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Inserts a new token row (caller supplies id, user, hash, name, timestamps).
    /// </summary>
    /// <param name="token">Entity to insert; <see cref="ApiToken.TokenHash"/> must already be hashed.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InsertAsync(ApiToken token, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO api_token (
                id, user_id, token_hash, name, created_at, last_used_at, revoked_at, notes)
            VALUES (
                @Id, @UserId, @TokenHash, @Name, @CreatedAt, @LastUsedAt, @RevokedAt, @Notes)
            """,
            token,
            cancellationToken: ct));
    }

    /// <summary>
    /// Finds a non-revoked token by its stored hash.
    /// </summary>
    /// <param name="tokenHash">Lowercase hex SHA-256 of the plaintext bearer secret.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The token row, or <see langword="null"/> if missing or revoked.</returns>
    /// <remarks>Primary lookup path for every authenticated API request.</remarks>
    public async Task<ApiToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var token = await conn.QuerySingleOrDefaultAsync<ApiToken>(new CommandDefinition(
            """
            SELECT * FROM api_token
            WHERE token_hash = @tokenHash AND revoked_at IS NULL
            """,
            new { tokenHash },
            cancellationToken: ct));
        if (token is null)
            _logger.LogWarning("No active token found for provided hash");
        return token;
    }

    /// <summary>
    /// Loads a token by id only if it belongs to <paramref name="userId"/> (ownership check).
    /// </summary>
    /// <param name="id">Token id.</param>
    /// <param name="userId">Expected owner.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ApiToken?> GetByIdForUserAsync(Uuid7 id, Uuid7 userId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ApiToken>(new CommandDefinition(
            "SELECT * FROM api_token WHERE id = @id AND user_id = @userId",
            new { id, userId },
            cancellationToken: ct));
    }

    /// <summary>
    /// Updates <c>last_used_at</c> after a successful bearer authentication.
    /// </summary>
    /// <param name="id">Token id.</param>
    /// <param name="when">UTC timestamp of the request.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task TouchLastUsedAsync(Uuid7 id, DateTimeOffset when, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE api_token SET last_used_at = @when WHERE id = @id",
            new { id, when },
            cancellationToken: ct));
    }

    /// <summary>
    /// Soft-revokes a token owned by <paramref name="userId"/> by setting <c>revoked_at</c>.
    /// </summary>
    /// <param name="id">Token id.</param>
    /// <param name="userId">Owner; prevents revoking another user's token.</param>
    /// <param name="when">UTC revocation timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if a row was updated; <see langword="false"/> if not found or already revoked.</returns>
    public async Task<bool> RevokeAsync(Uuid7 id, Uuid7 userId, DateTimeOffset when, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE api_token
            SET revoked_at = @when
            WHERE id = @id AND user_id = @userId AND revoked_at IS NULL
            """,
            new { id, userId, when },
            cancellationToken: ct));
        return rows > 0;
    }
}
