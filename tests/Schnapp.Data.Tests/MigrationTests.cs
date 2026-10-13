using Dapper;
using FluentAssertions;
using Npgsql;
using Schnapp.Data;
using Schnapp.Domain;
using Testcontainers.PostgreSql;
using Xunit;

using Medo;
namespace Schnapp.Data.Tests;

/// <summary>
/// Verifies DbUp migrations and seed data against a real Postgres instance.
/// </summary>
/// <remarks>
/// Uses <c>SCHNAPP_TEST_CONNECTION</c> as-is (shared host DB) or a Testcontainers instance.
/// Unlike API fixtures, this does <b>not</b> create a <c>schnapp_t_*</c> temp database—migrations
/// are idempotent, but concurrent suite runs on a shared host DB can race. Prefer containers
/// or a dedicated test DB if that becomes an issue.
/// </remarks>
public class MigrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _connectionString;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("SCHNAPP_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _connectionString = external;
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder("docker.io/library/postgres:16-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Docker", StringComparison.Ordinal)
                                   || ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "No SCHNAPP_TEST_CONNECTION and Testcontainers could not reach Docker/Podman. " +
                "Start Postgres and set SCHNAPP_TEST_CONNECTION, or enable a container runtime.",
                ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Migration seeds NONE sentinels, default settings, and the open-journal unique index.</summary>
    [Fact]
    public async Task Migrate_creates_schema_and_seeds_none_rows()
    {
        DapperConfig.EnsureConfigured();
        var cs = _connectionString!;
        DatabaseMigrator.Migrate(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var customerCode = await conn.ExecuteScalarAsync<string>(
            "SELECT code FROM customer WHERE id = @id",
            new { id = WellKnownIds.None });
        customerCode.Should().Be("-");

        var projectCustomer = await conn.ExecuteScalarAsync<Uuid7>(
            "SELECT customer_id FROM project WHERE id = @id",
            new { id = WellKnownIds.None });
        projectCustomer.Should().Be(WellKnownIds.None);

        var setting = await conn.ExecuteScalarAsync<string>(
            "SELECT value FROM setting WHERE key = @key",
            new { key = "journal.recent_last_seen_minutes" });
        setting.Should().Be("30");

        var openIndex = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM pg_indexes
            WHERE indexname = 'ux_journal_one_open_per_user_machine'
            """);
        openIndex.Should().Be(1);

        var entityStatusCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM entity_status");
        entityStatusCount.Should().BeGreaterThanOrEqualTo(12);

        var customerStatus = await conn.ExecuteScalarAsync<int>(
            "SELECT status FROM customer WHERE id = @id",
            new { id = WellKnownIds.None });
        customerStatus.Should().Be(0);
    }
}
