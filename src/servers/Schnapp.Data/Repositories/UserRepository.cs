namespace Schnapp.Data.Repositories;

using Dapper;

using Microsoft.Extensions.Logging;

using Schnapp.Domain;
using Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Dapper data access for the <c>app_user</c> table.
/// </summary>
/// <remarks>
/// Registered scoped via <see cref="ServiceCollectionExtensions.AddSchnappData"/>.
/// Called from auth endpoints, bearer token middleware, and admin bootstrap.
/// One connection per method (short unit of work).
/// </remarks>
public sealed class UserRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDatabaseDialect _sql;
    private readonly ILogger<UserRepository> _logger;

    /// <summary>
    /// Creates a repository bound to the shared connection factory.
    /// </summary>
    /// <param name="db">Factory that opens catalog connections.</param>
    /// <param name="sql">Active catalog dialect.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public UserRepository(IDbConnectionFactory db, IDatabaseDialect sql, ILogger<UserRepository> logger)
    {
        _db = db;
        _sql = sql;
        _logger = logger;
    }

    /// <summary>
    /// Returns the number of users (used by admin bootstrap to detect an empty system).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of rows in <c>app_user</c>.</returns>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CAST(COUNT(*) AS INTEGER) FROM app_user",
            cancellationToken: ct));
    }

    /// <summary>
    /// Lists all users ordered by name (admin user directory).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<User>(new CommandDefinition(
            "SELECT * FROM app_user ORDER BY lower(name), lower(email)",
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Loads a user by primary key, or <see langword="null"/> if missing.
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<User?> GetByIdAsync(Uuid7 id, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var user = await conn.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            "SELECT * FROM app_user WHERE id = @id",
            new { id },
            cancellationToken: ct));
        if (user is null)
            _logger.LogWarning("User {Id} not found", id);
        return user;
    }

    /// <summary>
    /// Loads a user by email (case-insensitive), or <see langword="null"/> if missing.
    /// </summary>
    /// <param name="email">Login email.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>Used by login and admin create uniqueness checks.</remarks>
    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            "SELECT * FROM app_user WHERE lower(email) = lower(@email)",
            new { email },
            cancellationToken: ct));
    }

    /// <summary>
    /// Loads a user by email, code, or name (all unique, case-insensitive).
    /// </summary>
    /// <param name="identity">Email, code, or display name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching user, or null if none (or ambiguous — should not happen with unique indexes).</returns>
    /// <remarks>Primary login lookup for API, Web, CLI, and tray clients.</remarks>
    public async Task<User?> GetByLoginIdentityAsync(string identity, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            $"""
            SELECT * FROM app_user
            WHERE lower(email) = lower(@identity)
               OR lower(code) = lower(@identity)
               OR lower(name) = lower(@identity)
            {_sql.Limit(1)}
            """,
            new { identity },
            cancellationToken: ct));
    }

    /// <summary>
    /// Inserts a new user row. Caller must set id, password hash, and unique code/name/email.
    /// </summary>
    /// <param name="user">Fully populated entity to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Exception">Propagates unique constraint violations for the API to map to 409.</exception>
    public async Task InsertAsync(User user, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            INSERT INTO app_user (
                id, code, name, email, password_hash, {_sql.Ident("role")}, time_zone_id,
                tags, notes, status, colour_code, created_at, updated_at, last_login, last_seen)
            VALUES (
                @Id, @Code, @Name, @Email, @PasswordHash, @Role, @TimeZoneId,
                @Tags, @Notes, @Status, @ColourCode, @CreatedAt, @UpdatedAt, @LastLogin, @LastSeen)
            """,
            user,
            cancellationToken: ct));
        _logger.LogInformation("Inserted user {Id} ({Email})", user.Id, user.Email);
    }

    /// <summary>
    /// Updates mutable profile fields (not password or role).
    /// </summary>
    /// <param name="user">Entity with <see cref="User.Id"/> and new profile values.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateProfileAsync(User user, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE app_user SET
                code = @Code,
                name = @Name,
                time_zone_id = @TimeZoneId,
                tags = @Tags,
                notes = @Notes,
                status = @Status,
                colour_code = @ColourCode,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            user,
            cancellationToken: ct));
    }

    /// <summary>
    /// Admin update: code, name, email, role, timezone, tags, notes, status.
    /// </summary>
    public async Task UpdateAdminAsync(User user, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE app_user SET
                code = @Code,
                name = @Name,
                email = @Email,
                {_sql.Ident("role")} = @Role,
                time_zone_id = @TimeZoneId,
                tags = @Tags,
                notes = @Notes,
                status = @Status,
                colour_code = @ColourCode,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            user,
            cancellationToken: ct));
        _logger.LogInformation("Admin updated user {Id} ({Email})", user.Id, user.Email);
    }

    /// <summary>Deletes a user row. Caller must enforce business rules (self-delete, last admin).</summary>
    public async Task<bool> DeleteAsync(Uuid7 id, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM app_user WHERE id = @id",
            new { id },
            cancellationToken: ct));
        if (n > 0)
            _logger.LogInformation("Deleted user {Id}", id);
        return n > 0;
    }

    /// <summary>Count of users with Admin role.</summary>
    public async Task<int> CountAdminsAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT CAST(COUNT(*) AS INTEGER) FROM app_user WHERE {_sql.Ident("role")} = @role",
            new { role = UserRole.Admin },
            cancellationToken: ct));
    }

    /// <summary>
    /// Replaces the stored password hash (admin reset or future change-password flows).
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="passwordHash">Already-hashed password from <c>PasswordService</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdatePasswordHashAsync(Uuid7 id, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE app_user SET password_hash = @passwordHash WHERE id = @id",
            new { id, passwordHash },
            cancellationToken: ct));
    }

    /// <summary>
    /// Records a successful login time and treats it as recent activity (<c>last_seen</c>).
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="when">UTC timestamp of the login.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task TouchLoginAsync(Uuid7 id, DateTimeOffset when, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE app_user SET last_login = @when, last_seen = @when WHERE id = @id",
            new { id, when },
            cancellationToken: ct));
    }

    /// <summary>
    /// Updates <c>last_seen</c> on authenticated API traffic (bearer middleware).
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="when">UTC timestamp of the request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Feeds future journal auto-close rules that distinguish recent vs aged activity.
    /// </remarks>
    public async Task TouchSeenAsync(Uuid7 id, DateTimeOffset when, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE app_user SET last_seen = @when WHERE id = @id",
            new { id, when },
            cancellationToken: ct));
    }
}
