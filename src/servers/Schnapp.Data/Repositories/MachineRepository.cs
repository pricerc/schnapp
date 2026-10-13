using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Schnapp.Domain.Entities;

using Medo;
namespace Schnapp.Data.Repositories;

/// <summary>
/// Dapper access for <c>machine</c> and <c>user_machine</c>.
/// </summary>
/// <remarks>
/// Machines are org-global rows; the user association is via <c>user_machine</c>
/// (many users may share one machine, e.g. RDP).
/// </remarks>
public sealed class MachineRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDatabaseDialect _sql;
    private readonly ILogger<MachineRepository> _logger;

    /// <summary>
    /// Creates a repository using the shared connection factory.
    /// </summary>
    /// <param name="db">Connection factory.</param>
    /// <param name="sql">Active catalog dialect.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public MachineRepository(IDbConnectionFactory db, IDatabaseDialect sql, ILogger<MachineRepository> logger)
    {
        _db = db;
        _sql = sql;
        _logger = logger;
    }

    /// <summary>
    /// Lists all machines org-wide, one row per user link, joined with the owning user's
    /// identity. Admin-only usage (see <c>RequireAdminFilter</c> at the endpoint).
    /// </summary>
    public async Task<IReadOnlyList<AdminMachineRow>> ListAllWithOwnersAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<AdminMachineRow>(new CommandDefinition(
            """
            SELECT
                m.id, m.code, m.name, m.fqdn, m.hardware_fingerprint, m.last_seen,
                m.notes, m.tags, m.status, m.colour_code, m.created_at, m.updated_at,
                u.id AS user_id, u.name AS user_name, u.email AS user_email
            FROM machine m
            INNER JOIN user_machine um ON um.machine_id = m.id
            INNER JOIN app_user u ON u.id = um.user_id
            ORDER BY u.name, m.name
            """,
            cancellationToken: ct))).ToList();
    }

    /// <summary>
    /// Lists machines linked to the given user (join user_machine).
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<(Machine Machine, UserMachine Link)>> ListForUserAsync(
        Uuid7 userId,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<MachineListRow>(new CommandDefinition(
            """
            SELECT
                m.id, m.code, m.name, m.fqdn, m.hardware_fingerprint, m.last_ip_addresses,
                m.optional_password_hash, m.notes, m.tags, m.status, m.colour_code, m.created_at, m.updated_at, m.last_seen,
                um.user_id, um.machine_id, um.registered_at, um.display_name AS link_display_name,
                um.notes AS link_notes, um.tags AS link_tags, um.status AS link_status,
                um.created_at AS link_created_at, um.updated_at AS link_updated_at,
                um.last_seen AS link_last_seen, um.last_updated, um.client_settings
            FROM machine m
            INNER JOIN user_machine um ON um.machine_id = m.id
            WHERE um.user_id = @userId
            ORDER BY m.name
            """,
            new { userId },
            cancellationToken: ct));

        return rows.Select(r => (
            new Machine
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                Fqdn = r.Fqdn,
                HardwareFingerprint = r.HardwareFingerprint,
                LastIpAddresses = r.LastIpAddresses,
                OptionalPasswordHash = r.OptionalPasswordHash,
                Notes = r.Notes,
                Tags = r.Tags,
                Status = r.Status,
                ColourCode = r.ColourCode,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                LastSeen = r.LastSeen
            },
            new UserMachine
            {
                UserId = r.UserId,
                MachineId = r.MachineId,
                RegisteredAt = r.RegisteredAt,
                DisplayName = r.LinkDisplayName,
                Notes = r.LinkNotes,
                Tags = r.LinkTags,
                Status = r.LinkStatus,
                CreatedAt = r.LinkCreatedAt,
                UpdatedAt = r.LinkUpdatedAt,
                LastSeen = r.LinkLastSeen,
                LastUpdated = r.LastUpdated,
                ClientSettings = r.ClientSettings
            })).ToList();
    }

    /// <summary>
    /// Loads a machine by id, or null.
    /// </summary>
    public async Task<Machine?> GetByIdAsync(Uuid7 id, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Machine>(new CommandDefinition(
            "SELECT * FROM machine WHERE id = @id",
            new { id },
            cancellationToken: ct));
    }

    /// <summary>Loads the user–machine link, or null if not linked.</summary>
    public async Task<UserMachine?> GetLinkAsync(Uuid7 userId, Uuid7 machineId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserMachine>(new CommandDefinition(
            """
            SELECT user_id, machine_id, registered_at, display_name, notes, tags,
                   status, created_at, updated_at, last_seen, last_updated, client_settings
            FROM user_machine
            WHERE user_id = @userId AND machine_id = @machineId
            """,
            new { userId, machineId },
            cancellationToken: ct));
    }

    /// <summary>Sets status on many machines at once.</summary>
    public async Task<int> UpdateStatusBulkAsync(IEnumerable<Uuid7> ids, int status, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0) return 0;
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var when = DateTimeOffset.UtcNow;
        var args = new DynamicParameters();
        args.Add("status", status);
        args.Add("when", when);
        var list = InClause.Add(args, idList, "id");
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE machine SET status = @status, updated_at = @when WHERE id IN {list}",
            args,
            cancellationToken: ct));
        _logger.LogInformation("Bulk-updated status={Status} on {Count} machine(s)", status, n);
        return n;
    }

    /// <summary>Updates editable machine fields.</summary>
    public async Task UpdateAsync(Machine machine, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE machine SET
                code = @Code,
                name = @Name,
                fqdn = @Fqdn,
                hardware_fingerprint = @HardwareFingerprint,
                notes = @Notes,
                tags = @Tags,
                status = @Status,
                colour_code = @ColourCode,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            machine,
            cancellationToken: ct));
        _logger.LogInformation("Updated machine {Id}", machine.Id);
    }

    /// <summary>Updates display name / notes / tags / status on a user–machine link.</summary>
    public async Task UpdateLinkAsync(UserMachine link, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE user_machine SET
                display_name = @DisplayName,
                notes = @Notes,
                tags = @Tags,
                status = @Status,
                updated_at = @UpdatedAt
            WHERE user_id = @UserId AND machine_id = @MachineId
            """,
            link,
            cancellationToken: ct));
    }

    /// <summary>
    /// Inserts a machine and links it to the user in one transaction.
    /// </summary>
    public async Task InsertWithUserLinkAsync(Machine machine, UserMachine link, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                $"""
                INSERT INTO machine (
                    id, code, name, fqdn, hardware_fingerprint, last_ip_addresses,
                    optional_password_hash, notes, tags, status, colour_code, created_at, updated_at, last_seen)
                VALUES (
                    @Id, @Code, @Name, @Fqdn, @HardwareFingerprint, {_sql.JsonParam("LastIpAddresses")},
                    @OptionalPasswordHash, @Notes, @Tags, @Status, @ColourCode, @CreatedAt, @UpdatedAt, @LastSeen)
                """,
                machine,
                transaction: tx,
                cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                $"""
                INSERT INTO user_machine (
                    user_id, machine_id, registered_at, display_name, notes, tags,
                    status, created_at, updated_at, last_seen, last_updated, client_settings)
                VALUES (
                    @UserId, @MachineId, @RegisteredAt, @DisplayName, @Notes, @Tags,
                    @Status, @CreatedAt, @UpdatedAt, @LastSeen, @LastUpdated, {_sql.JsonParam("ClientSettings")})
                """,
                link,
                transaction: tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            _logger.LogInformation("Inserted machine {Id} linked to user {UserId}", machine.Id, link.UserId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Ensures a user_machine link exists (after find + link / use).
    /// </summary>
    public async Task UpsertUserLinkAsync(UserMachine link, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        if (_sql.Provider == DatabaseProvider.Firebird)
        {
            var n = await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE user_machine SET
                    last_updated = @LastUpdated,
                    updated_at = @UpdatedAt,
                    status = @Status,
                    display_name = COALESCE(@DisplayName, display_name)
                WHERE user_id = @UserId AND machine_id = @MachineId
                """,
                link, cancellationToken: ct));
            if (n == 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    $"""
                    INSERT INTO user_machine (
                        user_id, machine_id, registered_at, display_name, notes, tags,
                        status, created_at, updated_at, last_seen, last_updated, client_settings)
                    VALUES (
                        @UserId, @MachineId, @RegisteredAt, @DisplayName, @Notes, @Tags,
                        @Status, @CreatedAt, @UpdatedAt, @LastSeen, @LastUpdated, {_sql.JsonParam("ClientSettings")})
                    """,
                    link, cancellationToken: ct));
            }
        }
        else
        {
            await conn.ExecuteAsync(new CommandDefinition(
                $"""
                INSERT INTO user_machine (
                    user_id, machine_id, registered_at, display_name, notes, tags,
                    status, created_at, updated_at, last_seen, last_updated, client_settings)
                VALUES (
                    @UserId, @MachineId, @RegisteredAt, @DisplayName, @Notes, @Tags,
                    @Status, @CreatedAt, @UpdatedAt, @LastSeen, @LastUpdated, {_sql.JsonParam("ClientSettings")})
                ON CONFLICT (user_id, machine_id) DO UPDATE SET
                    last_updated = EXCLUDED.last_updated,
                    updated_at = EXCLUDED.updated_at,
                    status = EXCLUDED.status,
                    display_name = COALESCE(EXCLUDED.display_name, user_machine.display_name)
                """,
                link, cancellationToken: ct));
        }
    }

    /// <summary>
    /// Updates identity fields on a machine (FQDN, fingerprint, last seen).
    /// </summary>
    public async Task UpdateIdentityAsync(
        Uuid7 id,
        string? fqdn,
        string? hardwareFingerprint,
        string? lastIpAddressesJson,
        DateTimeOffset when,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE machine SET
                fqdn = COALESCE(@fqdn, fqdn),
                hardware_fingerprint = COALESCE(@hardwareFingerprint, hardware_fingerprint),
                last_ip_addresses = COALESCE({_sql.JsonParam("lastIpAddressesJson")}, last_ip_addresses),
                updated_at = @when,
                last_seen = @when
            WHERE id = @id
            """,
            new { id, fqdn, hardwareFingerprint, lastIpAddressesJson, when },
            cancellationToken: ct));
    }

    /// <summary>
    /// Finds org machines that may match this PC (fingerprint and/or FQDN).
    /// </summary>
    public async Task<IReadOnlyList<Machine>> FindCandidatesAsync(
        string? fqdn,
        string? hardwareFingerprint,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Machine>(new CommandDefinition(
            $"""
            SELECT * FROM machine
            WHERE
                (@hardwareFingerprint IS NOT NULL AND hardware_fingerprint = @hardwareFingerprint)
                OR (@fqdn IS NOT NULL AND lower(fqdn) = lower(@fqdn))
            ORDER BY last_seen DESC NULLS LAST, name
            {_sql.Limit(20)}
            """,
            new { fqdn, hardwareFingerprint },
            cancellationToken: ct));
        return rows.ToList();
    }

    private sealed class MachineListRow
    {
        public Uuid7 Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Fqdn { get; set; }
        public string? HardwareFingerprint { get; set; }
        public string? LastIpAddresses { get; set; }
        public string? OptionalPasswordHash { get; set; }
        public string? Notes { get; set; }
        public string? Tags { get; set; }
        public int Status { get; set; }
        public string? ColourCode { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
        public Uuid7 UserId { get; set; }
        public Uuid7 MachineId { get; set; }
        public DateTimeOffset RegisteredAt { get; set; }
        public string? LinkDisplayName { get; set; }
        public string? LinkNotes { get; set; }
        public string? LinkTags { get; set; }
        public int LinkStatus { get; set; }
        public DateTimeOffset LinkCreatedAt { get; set; }
        public DateTimeOffset LinkUpdatedAt { get; set; }
        public DateTimeOffset? LinkLastSeen { get; set; }
        public DateTimeOffset? LastUpdated { get; set; }
        public string? ClientSettings { get; set; }
    }

    /// <summary>Row shape for <see cref="ListAllWithOwnersAsync"/> (machine + owning user).</summary>
    public sealed class AdminMachineRow
    {
        public Uuid7 Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Fqdn { get; set; }
        public string? HardwareFingerprint { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
        public string? Notes { get; set; }
        public string? Tags { get; set; }
        public int Status { get; set; }
        public string? ColourCode { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Uuid7 UserId { get; set; }
        public string UserName { get; set; } = "";
        public string UserEmail { get; set; } = "";
    }
}
