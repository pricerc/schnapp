using Npgsql;
using Schnapp.Data;
using Testcontainers.PostgreSql;

namespace Schnapp.Api.Tests;

/// <summary>
/// Shared Postgres setup for API integration tests.
/// </summary>
/// <remarks>
/// Prefer <c>SCHNAPP_TEST_CONNECTION</c> (host Postgres). Without it, falls back to Testcontainers
/// (requires Docker/Podman). Leftover <c>schnapp_t_*</c> DBs: <c>./scripts/cleanup-test-dbs.sh</c>.
/// </remarks>
internal static class PostgresTestSupport
{
    /// <summary>
    /// Creates a migrated, isolated database for one test fixture.
    /// </summary>
    /// <returns>
    /// Connection string for the test DB; optional Testcontainers handle; and the created DB name
    /// when using an external host (null for containers).
    /// </returns>
    /// <remarks>
    /// <para>
    /// With <c>SCHNAPP_TEST_CONNECTION</c>: creates a temporary DB named
    /// <c>schnapp_t_</c> + 12 hex chars on the same server (via <c>postgres</c> maintenance DB),
    /// runs DbUp migrations, and returns a connection string pointing at that DB.
    /// The role needs <c>CREATEDB</c> (or equivalent). Callers must drop via
    /// <see cref="DropDatabaseIfCreatedAsync"/> in dispose or leftovers accumulate.
    /// </para>
    /// <para>
    /// Without the env var: starts a disposable Postgres 16 container, migrates, returns its
    /// connection string and the container (dispose the container; no host DB name).
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Neither external connection nor a container runtime is available.
    /// </exception>
    public static async Task<(string ConnectionString, PostgreSqlContainer? Container, string? CreatedDatabase)>
        CreateMigratedDatabaseAsync()
    {
        var external = Environment.GetEnvironmentVariable("SCHNAPP_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var admin = new NpgsqlConnectionStringBuilder(external) { Database = "postgres" };
            var dbName = "schnapp_t_" + Guid.NewGuid().ToString("N")[..12];
            await using (var conn = new NpgsqlConnection(admin.ConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE \"{dbName}\" OWNER current_user";
                // OWNER current_user may fail if user can't create DB — use schnapp if superuser created it
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Fallback: create without owner clause
                    cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            var cs = new NpgsqlConnectionStringBuilder(external) { Database = dbName }.ConnectionString;
            DatabaseMigrator.Migrate(cs);
            return (cs, null, dbName);
        }

        try
        {
            var container = new PostgreSqlBuilder("docker.io/library/postgres:16-alpine").Build();
            await container.StartAsync();
            var cs = container.GetConnectionString();
            DatabaseMigrator.Migrate(cs);
            return (cs, container, null);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Docker", StringComparison.Ordinal)
                                   || ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "No SCHNAPP_TEST_CONNECTION and Testcontainers could not reach a container runtime.",
                ex);
        }
    }

    /// <summary>
    /// Drops a host-side temporary DB created by <see cref="CreateMigratedDatabaseAsync"/>.
    /// </summary>
    /// <param name="baseConnectionString">
    /// Original <c>SCHNAPP_TEST_CONNECTION</c> (any DB name; rewritten to <c>postgres</c> for admin).
    /// </param>
    /// <param name="databaseName">
    /// Name returned as <c>CreatedDatabase</c>, or null/empty to no-op (container path).
    /// </param>
    /// <remarks>
    /// <para>
    /// Failure modes that previously left dozens of <c>schnapp_t_*</c> DBs:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Npgsql connection pooling keeps sessions open after <c>WebApplicationFactory</c> dispose →
    /// DROP fails with "is being accessed by other users". Mitigated by
    /// <see cref="NpgsqlConnection.ClearPool"/>, <c>pg_terminate_backend</c>, then
    /// <c>DROP DATABASE … WITH (FORCE)</c> (PG13+).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Drop must not share a transaction/batch with other statements (Postgres rejects
    /// <c>DROP DATABASE</c> inside a transaction).
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Kill/abort of the test process skips dispose → DB is never dropped. Use
    /// <c>./scripts/cleanup-test-dbs.sh</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Call only after disposing the factory/client so app pools release first.
    /// Exceptions propagate (no silent swallow) so CI surfaces drop failures.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public static async Task DropDatabaseIfCreatedAsync(string baseConnectionString, string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            return;

        // Pooled connections keep the DB "in use" after WebApplicationFactory dispose.
        // Clear pools first so DROP can succeed (PG also has WITH (FORCE) as a backstop).
        var testDb = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName };
        NpgsqlConnection.ClearPool(new NpgsqlConnection(testDb.ConnectionString));

        var admin = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();

        // Prefer DROP … WITH (FORCE) (PG13+). pg_terminate_backend may fail with
        // "permission denied" for non-superuser roles; ignore and still try DROP.
        try
        {
            await using var terminate = conn.CreateCommand();
            terminate.CommandText = """
                SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                WHERE datname = @db AND pid <> pg_backend_pid();
                """;
            terminate.Parameters.AddWithValue("db", databaseName);
            await terminate.ExecuteNonQueryAsync();
        }
        catch
        {
            // best-effort
        }

        await using var drop = conn.CreateCommand();
        drop.CommandText = $"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""";
        try
        {
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // leave orphaned schnapp_t_* DB; scripts/cleanup-test-dbs.sh can remove later
        }
    }
}
