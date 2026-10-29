namespace Schnapp.Data.Repositories;

using Dapper;

using Microsoft.Extensions.Logging;

using Schnapp.Domain;
using Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// CRUD for org-global customers, projects, tasks, journal targets, tags, and entity statuses.
/// </summary>
/// <remarks>
/// List entities are shared across the org (not user-scoped). Deletes refuse
/// <see cref="WellKnownIds.None"/>. Journal targets are get-or-create by
/// (customer, project, task) triple. Registered scoped via
/// <see cref="ServiceCollectionExtensions.AddSchnappData"/>.
/// </remarks>
public sealed class ListRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDatabaseDialect _sql;
    private readonly ILogger<ListRepository> _logger;

    /// <summary>
    /// Creates a repository bound to the shared connection factory.
    /// </summary>
    /// <param name="db">Factory that opens catalog connections.</param>
    /// <param name="sql">Active catalog dialect.</param>
    /// <param name="logger">Logger for repository diagnostics.</param>
    public ListRepository(IDbConnectionFactory db, IDatabaseDialect sql, ILogger<ListRepository> logger)
    {
        _db = db;
        _sql = sql;
        _logger = logger;
    }

    /// <summary>Lists all customers ordered by name.</summary>
    public async Task<IReadOnlyList<Customer>> ListCustomersAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Customer>(new CommandDefinition(
            "SELECT * FROM customer ORDER BY name", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Loads a customer by id, or <see langword="null"/>.</summary>
    public async Task<Customer?> GetCustomerAsync(Uuid7 id, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Customer>(new CommandDefinition(
            "SELECT * FROM customer WHERE id = @id", new { id }, cancellationToken: ct));
    }

    /// <summary>Inserts a customer row.</summary>
    public async Task InsertCustomerAsync(Customer c, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO customer (id, code, name, notes, tags, status, colour_code, created_at, updated_at)
            VALUES (@Id, @Code, @Name, @Notes, @Tags, @Status, @ColourCode, @CreatedAt, @UpdatedAt)
            """,
            c, cancellationToken: ct));
    }

    /// <summary>Updates an existing customer by id.</summary>
    public async Task UpdateCustomerAsync(Customer c, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE customer SET code=@Code, name=@Name, notes=@Notes, tags=@Tags,
                status=@Status, colour_code=@ColourCode, updated_at=@UpdatedAt
            WHERE id=@Id
            """,
            c, cancellationToken: ct));
    }

    /// <summary>Deletes a customer; returns false for missing or well-known none id.</summary>
    public async Task<bool> DeleteCustomerAsync(Uuid7 id, CancellationToken ct = default)
    {
        if (id == WellKnownIds.None) return false;
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM customer WHERE id = @id AND id <> @none",
            new { id, none = WellKnownIds.None }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>Lists all projects ordered by name.</summary>
    public async Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<Project>(new CommandDefinition(
            "SELECT * FROM project ORDER BY name", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Inserts a project row.</summary>
    public async Task InsertProjectAsync(Project p, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project (id, code, customer_id, name, notes, tags, status, colour_code, created_at, updated_at)
            VALUES (@Id, @Code, @CustomerId, @Name, @Notes, @Tags, @Status, @ColourCode, @CreatedAt, @UpdatedAt)
            """, p, cancellationToken: ct));
    }

    /// <summary>Updates an existing project by id.</summary>
    public async Task UpdateProjectAsync(Project p, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE project SET code=@Code, customer_id=@CustomerId, name=@Name, notes=@Notes, tags=@Tags,
                status=@Status, colour_code=@ColourCode, updated_at=@UpdatedAt
            WHERE id=@Id
            """, p, cancellationToken: ct));
    }

    /// <summary>Deletes a project; returns false for missing or well-known none id.</summary>
    public async Task<bool> DeleteProjectAsync(Uuid7 id, CancellationToken ct = default)
    {
        if (id == WellKnownIds.None) return false;
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM project WHERE id = @id AND id <> @none",
            new { id, none = WellKnownIds.None }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>Lists all tasks ordered by name.</summary>
    public async Task<IReadOnlyList<TaskItem>> ListTasksAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<TaskItem>(new CommandDefinition(
            "SELECT * FROM task ORDER BY name", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Inserts a task row.</summary>
    public async Task InsertTaskAsync(TaskItem t, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO task (id, code, customer_id, project_id, name, notes, tags, status, colour_code, created_at, updated_at)
            VALUES (@Id, @Code, @CustomerId, @ProjectId, @Name, @Notes, @Tags, @Status, @ColourCode, @CreatedAt, @UpdatedAt)
            """, t, cancellationToken: ct));
    }

    /// <summary>Updates an existing task by id.</summary>
    public async Task UpdateTaskAsync(TaskItem t, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE task SET code=@Code, customer_id=@CustomerId, project_id=@ProjectId,
                name=@Name, notes=@Notes, tags=@Tags, status=@Status, colour_code=@ColourCode, updated_at=@UpdatedAt
            WHERE id=@Id
            """, t, cancellationToken: ct));
    }

    /// <summary>
    /// Lists allowed status values for list entities; optional case-insensitive entity filter.
    /// </summary>
    public async Task<IReadOnlyList<EntityStatus>> ListEntityStatusesAsync(string? entity = null, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        if (string.IsNullOrWhiteSpace(entity))
        {
            var all = await conn.QueryAsync<EntityStatus>(new CommandDefinition(
                "SELECT entity, status, description, colour_code FROM entity_status ORDER BY entity, status",
                cancellationToken: ct));
            return all.ToList();
        }

        var rows = await conn.QueryAsync<EntityStatus>(new CommandDefinition(
            """
            SELECT entity, status, description, colour_code FROM entity_status
            WHERE lower(entity) = lower(@entity)
            ORDER BY status
            """,
            new { entity }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Deletes a task; returns false for missing or well-known none id.</summary>
    public async Task<bool> DeleteTaskAsync(Uuid7 id, CancellationToken ct = default)
    {
        if (id == WellKnownIds.None) return false;
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM task WHERE id = @id AND id <> @none",
            new { id, none = WellKnownIds.None }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>
    /// Returns the journal target for the (customer, project, task) triple, creating it if missing.
    /// </summary>
    /// <param name="customerId">Customer component (may be none).</param>
    /// <param name="projectId">Project component (may be none).</param>
    /// <param name="taskId">Task component (may be none).</param>
    /// <param name="code">Optional code; derived from entity codes when empty.</param>
    /// <param name="name">Optional display name; derived from entity names when empty.</param>
    /// <param name="preferredId">Optional client-supplied id for a new row.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<JournalTarget> GetOrCreateTargetAsync(
        Uuid7 customerId, Uuid7 projectId, Uuid7 taskId,
        string? code, string? name, Uuid7? preferredId, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var existing = await conn.QuerySingleOrDefaultAsync<JournalTarget>(new CommandDefinition(
            """
            SELECT * FROM journal_target
            WHERE customer_id = @customerId AND project_id = @projectId AND task_id = @taskId
            """,
            new { customerId, projectId, taskId }, cancellationToken: ct));
        if (existing is not null) return existing;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            var customer = customerId != WellKnownIds.None
                ? await conn.QuerySingleOrDefaultAsync<Customer>(new CommandDefinition(
                    "SELECT * FROM customer WHERE id = @customerId", new { customerId }, cancellationToken: ct))
                : null;
            var project = projectId != WellKnownIds.None
                ? await conn.QuerySingleOrDefaultAsync<Project>(new CommandDefinition(
                    "SELECT * FROM project WHERE id = @projectId", new { projectId }, cancellationToken: ct))
                : null;
            var task = taskId != WellKnownIds.None
                ? await conn.QuerySingleOrDefaultAsync<TaskItem>(new CommandDefinition(
                    "SELECT * FROM task WHERE id = @taskId", new { taskId }, cancellationToken: ct))
                : null;

            var codeParts = new[] { customer?.Code, project?.Code, task?.Code }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var nameParts = new[] { customer?.Name, project?.Name, task?.Name }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            if (string.IsNullOrWhiteSpace(code)) code = string.Join("-", codeParts);
            if (string.IsNullOrWhiteSpace(name)) name = string.Join(" / ", nameParts);
        }

        var target = new JournalTarget
        {
            Id = preferredId is { } pid && pid != WellKnownIds.None ? pid : IdFactory.NewId(),
            CustomerId = customerId,
            ProjectId = projectId,
            TaskId = taskId,
            Code = code ?? "",
            Name = name ?? "",
            Notes = null,
            Tags = null
        };
        var insertTarget = _sql.Provider == DatabaseProvider.Firebird
            ? """
            INSERT INTO journal_target (id, customer_id, project_id, task_id, code, name, notes, tags)
            SELECT @Id, @CustomerId, @ProjectId, @TaskId, @Code, @Name, @Notes, @Tags
            FROM RDB$DATABASE
            WHERE NOT EXISTS (
                SELECT 1 FROM journal_target
                WHERE customer_id = @CustomerId AND project_id = @ProjectId AND task_id = @TaskId)
            """
            : """
            INSERT INTO journal_target (id, customer_id, project_id, task_id, code, name, notes, tags)
            VALUES (@Id, @CustomerId, @ProjectId, @TaskId, @Code, @Name, @Notes, @Tags)
            ON CONFLICT (customer_id, project_id, task_id) DO NOTHING
            """;
        await conn.ExecuteAsync(new CommandDefinition(
            insertTarget,
            target, cancellationToken: ct));
        _logger.LogInformation("Created journal target {Id} ({CustomerId}/{ProjectId}/{TaskId})", target.Id, customerId, projectId, taskId);

        return await conn.QuerySingleAsync<JournalTarget>(new CommandDefinition(
            """
            SELECT * FROM journal_target
            WHERE customer_id = @customerId AND project_id = @projectId AND task_id = @taskId
            """,
            new { customerId, projectId, taskId }, cancellationToken: ct));
    }

    /// <summary>Lists all journal targets ordered by name then code.</summary>
    public async Task<IReadOnlyList<JournalTarget>> ListTargetsAsync(CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<JournalTarget>(new CommandDefinition(
            "SELECT * FROM journal_target ORDER BY name, code", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Loads a journal target by id, or <see langword="null"/>.</summary>
    public async Task<JournalTarget?> GetTargetAsync(Uuid7 id, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<JournalTarget>(new CommandDefinition(
            "SELECT * FROM journal_target WHERE id = @id", new { id }, cancellationToken: ct));
    }

    /// <summary>
    /// Partially updates a journal target; <see langword="null"/> fields leave existing values.
    /// </summary>
    /// <returns>Updated entity, or <see langword="null"/> if missing.</returns>
    public async Task<JournalTarget?> UpdateTargetAsync(
        Uuid7 id, string? code, string? name, string? notes, string? tags, CancellationToken ct = default)
    {
        var existing = await GetTargetAsync(id, ct);
        if (existing is null) return null;
        if (code is not null) existing.Code = code;
        if (name is not null) existing.Name = name;
        if (notes is not null) existing.Notes = notes;
        if (tags is not null) existing.Tags = tags;
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE journal_target
            SET code = @Code, name = @Name, notes = @Notes, tags = @Tags
            WHERE id = @Id
            """,
            existing, cancellationToken: ct));
        return existing;
    }

    /// <summary>
    /// How <see cref="ListTagsAsync"/> filters dictionary rows.
    /// </summary>
    public enum TagListScope
    {
        /// <summary>All rows, or global+customer when <c>customerId</c> is set.</summary>
        Default = 0,

        /// <summary>Only org-global rows (<c>customer_id IS NULL</c>).</summary>
        GlobalOnly = 1,

        /// <summary>Only rows for the given customer (requires non-null customer id).</summary>
        CustomerOnly = 2
    }

    /// <summary>
    /// Lists known tags ordered by tag text.
    /// </summary>
    /// <param name="customerId">
    /// With <see cref="TagListScope.Default"/>: filter to global + this customer.
    /// With <see cref="TagListScope.CustomerOnly"/>: only this customer's rows.
    /// Ignored for <see cref="TagListScope.GlobalOnly"/>.
    /// </param>
    /// <param name="scope">List filter mode.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<Tag>> ListTagsAsync(
        Uuid7? customerId = null,
        TagListScope scope = TagListScope.Default,
        CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);

        if (scope == TagListScope.GlobalOnly)
        {
            var global = await conn.QueryAsync<Tag>(new CommandDefinition(
                """
                SELECT tag AS TagValue, notes, customer_id AS CustomerId
                FROM tag
                WHERE customer_id IS NULL
                ORDER BY tag
                """,
                cancellationToken: ct));
            return global.ToList();
        }

        if (scope == TagListScope.CustomerOnly)
        {
            var cid = NormalizeCustomerId(customerId)
                ?? throw new ArgumentException("customerId is required for CustomerOnly scope.", nameof(customerId));
            var only = await conn.QueryAsync<Tag>(new CommandDefinition(
                """
                SELECT tag AS TagValue, notes, customer_id AS CustomerId
                FROM tag
                WHERE customer_id = @cid
                ORDER BY tag
                """,
                new { cid }, cancellationToken: ct));
            return only.ToList();
        }

        if (customerId is { } filterCid && filterCid != WellKnownIds.None)
        {
            var scoped = await conn.QueryAsync<Tag>(new CommandDefinition(
                """
                SELECT tag AS TagValue, notes, customer_id AS CustomerId
                FROM tag
                WHERE customer_id IS NULL OR customer_id = @cid
                ORDER BY tag, customer_id NULLS FIRST
                """,
                new { cid = filterCid }, cancellationToken: ct));
            return scoped.ToList();
        }

        var rows = await conn.QueryAsync<Tag>(new CommandDefinition(
            """
            SELECT tag AS TagValue, notes, customer_id AS CustomerId
            FROM tag
            ORDER BY tag, customer_id NULLS FIRST
            """,
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>True when a global (no customer) row exists for <paramref name="tag"/>.</summary>
    public async Task<bool> GlobalTagExistsAsync(string tag, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var n = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CAST(COUNT(*) AS INTEGER) FROM tag WHERE tag = @tag AND customer_id IS NULL",
            new { tag }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>
    /// Inserts a tag vocabulary row. Fails if the (tag, customer) pair already exists,
    /// or if inserting a customer-scoped tag when a global row already exists for the same text.
    /// </summary>
    /// <param name="tag">Canonical tag text (already trimmed).</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="customerId">Null or <see cref="WellKnownIds.None"/> for global.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InsertTagAsync(string tag, string? notes, Uuid7? customerId = null, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var cid = NormalizeCustomerId(customerId);
        if (cid is not null)
        {
            var globalExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CAST(COUNT(*) AS INTEGER) FROM tag WHERE tag = @tag AND customer_id IS NULL",
                new { tag }, cancellationToken: ct));
            if (globalExists > 0)
                throw new InvalidOperationException("Tag already exists as a global tag.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO tag (tag, notes, customer_id) VALUES (@tag, @notes, @cid)",
            new { tag, notes, cid }, cancellationToken: ct));
    }

    /// <summary>Updates notes for an existing tag row.</summary>
    public async Task<bool> UpdateTagNotesAsync(string tag, Uuid7? customerId, string? notes, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var cid = NormalizeCustomerId(customerId);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tag SET notes = @notes
            WHERE tag = @tag AND customer_id IS NOT DISTINCT FROM @cid
            """,
            new { tag, notes, cid }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>Deletes a dictionary row by tag text and scope.</summary>
    public async Task<bool> DeleteTagAsync(string tag, Uuid7? customerId = null, CancellationToken ct = default)
    {
        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var cid = NormalizeCustomerId(customerId);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM tag
            WHERE tag = @tag AND customer_id IS NOT DISTINCT FROM @cid
            """,
            new { tag, cid }, cancellationToken: ct));
        return n > 0;
    }

    /// <summary>
    /// Moves a global tag to a customer. If the customer already has the same tag text,
    /// deletes the global row instead (keeps customer notes).
    /// </summary>
    /// <returns>Updated/resulting row, or null if the global tag was missing.</returns>
    public async Task<Tag?> AssignTagToCustomerAsync(string tag, Uuid7 customerId, CancellationToken ct = default)
    {
        var cid = NormalizeCustomerId(customerId)
            ?? throw new ArgumentException("customerId is required.", nameof(customerId));

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);

        var global = await conn.QuerySingleOrDefaultAsync<Tag>(new CommandDefinition(
            """
            SELECT tag AS TagValue, notes, customer_id AS CustomerId
            FROM tag WHERE tag = @tag AND customer_id IS NULL
            """,
            new { tag }, cancellationToken: ct));
        if (global is null)
            return null;

        var customerRow = await conn.QuerySingleOrDefaultAsync<Tag>(new CommandDefinition(
            """
            SELECT tag AS TagValue, notes, customer_id AS CustomerId
            FROM tag WHERE tag = @tag AND customer_id = @cid
            """,
            new { tag, cid }, cancellationToken: ct));

        if (customerRow is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM tag WHERE tag = @tag AND customer_id IS NULL",
                new { tag }, cancellationToken: ct));
            return customerRow;
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE tag SET customer_id = @cid WHERE tag = @tag AND customer_id IS NULL",
            new { tag, cid }, cancellationToken: ct));
        global.CustomerId = cid;
        return global;
    }

    /// <summary>
    /// Moves a customer-scoped tag to the global list. If a global row already exists,
    /// deletes the customer row (global wins).
    /// </summary>
    /// <returns>Resulting global row, or null if the customer tag was missing.</returns>
    public async Task<Tag?> MakeTagGlobalAsync(string tag, Uuid7 customerId, CancellationToken ct = default)
    {
        var cid = NormalizeCustomerId(customerId)
            ?? throw new ArgumentException("customerId is required.", nameof(customerId));

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);

        var customerRow = await conn.QuerySingleOrDefaultAsync<Tag>(new CommandDefinition(
            """
            SELECT tag AS TagValue, notes, customer_id AS CustomerId
            FROM tag WHERE tag = @tag AND customer_id = @cid
            """,
            new { tag, cid }, cancellationToken: ct));
        if (customerRow is null)
            return null;

        var global = await conn.QuerySingleOrDefaultAsync<Tag>(new CommandDefinition(
            """
            SELECT tag AS TagValue, notes, customer_id AS CustomerId
            FROM tag WHERE tag = @tag AND customer_id IS NULL
            """,
            new { tag }, cancellationToken: ct));

        if (global is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM tag WHERE tag = @tag AND customer_id = @cid",
                new { tag, cid }, cancellationToken: ct));
            return global;
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE tag SET customer_id = NULL WHERE tag = @tag AND customer_id = @cid",
            new { tag, cid }, cancellationToken: ct));
        customerRow.CustomerId = null;
        return customerRow;
    }

    /// <summary>
    /// Ensures each distinct token from a free-text tags value exists in the dictionary.
    /// </summary>
    /// <param name="tagsText">Normalized or raw tags text (tokens extracted via whitespace/punctuation).</param>
    /// <param name="customerId">Customer scope for new rows; null/None → global.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Idempotent. Does not update notes on existing rows.
    /// Customer-scoped inserts are skipped when a global row already exists for the same text
    /// (global tags must not also exist as customer-specific).
    /// </remarks>
    public async Task EnsureTagsAsync(string? tagsText, Uuid7? customerId = null, CancellationToken ct = default)
    {
        var tokens = TagTokens(tagsText);
        if (tokens.Count == 0)
            return;

        await using var conn = (System.Data.Common.DbConnection)_db.CreateConnection();
        await conn.OpenAsync(ct);
        var cid = NormalizeCustomerId(customerId);
        // Firebird SELECT without FROM is invalid; RDB$DATABASE is a single-row system table.
        var fromOne = _sql.Provider == DatabaseProvider.Firebird ? "FROM RDB$DATABASE" : "";
        foreach (var token in tokens)
        {
            // Global exclusivity: if a global tag exists, never create a customer-specific copy.
            if (cid is not null)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    $"""
                    INSERT INTO tag (tag, notes, customer_id)
                    SELECT @tag, NULL, @cid
                    {fromOne}
                    WHERE NOT EXISTS (
                        SELECT 1 FROM tag t
                        WHERE t.tag = @tag AND t.customer_id IS NULL
                    )
                    AND NOT EXISTS (
                        SELECT 1 FROM tag t
                        WHERE t.tag = @tag AND t.customer_id IS NOT DISTINCT FROM @cid
                    )
                    """,
                    new { tag = token, cid }, cancellationToken: ct));
            }
            else
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    $"""
                    INSERT INTO tag (tag, notes, customer_id)
                    SELECT @tag, NULL, NULL
                    {fromOne}
                    WHERE NOT EXISTS (
                        SELECT 1 FROM tag t
                        WHERE t.tag = @tag AND t.customer_id IS NULL
                    )
                    """,
                    new { tag = token }, cancellationToken: ct));
                // Drop any customer-specific copies now that a global row exists.
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    DELETE FROM tag
                    WHERE tag = @tag AND customer_id IS NOT NULL
                      AND EXISTS (
                          SELECT 1 FROM tag g
                          WHERE g.tag = @tag AND g.customer_id IS NULL
                      )
                    """,
                    new { tag = token }, cancellationToken: ct));
            }
        }
    }

    /// <summary>Null or None → null (global dictionary scope).</summary>
    private static Uuid7? NormalizeCustomerId(Uuid7? customerId)
    {
        if (customerId is null) return null;
        if (customerId.Value == WellKnownIds.None) return null;
        return customerId;
    }

    /// <summary>Distinct non-empty tokens (case-sensitive, first-seen order).</summary>
    private static IReadOnlyList<string> TagTokens(string? tagsText)
    {
        if (string.IsNullOrWhiteSpace(tagsText))
            return Array.Empty<string>();

        var delimiters = new[] { ',', ';', '\t', ' ' };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var part in tagsText.Split(delimiters, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length > 0 && seen.Add(t))
                list.Add(t);
        }
        return list;
    }
}
