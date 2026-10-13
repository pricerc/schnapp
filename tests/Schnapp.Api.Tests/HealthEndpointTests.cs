using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Schnapp.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace Schnapp.Api.Tests;

/// <summary>
/// Integration tests for <c>GET /api/v1/health</c>.
/// </summary>
/// <remarks>
/// Unlike other API fixtures, this reuses <c>SCHNAPP_TEST_CONNECTION</c> as-is (no
/// <c>schnapp_t_*</c> temp DB) because health only needs a reachable migrated database.
/// Prefer Testcontainers when the env var is unset.
/// </remarks>
public class HealthEndpointTests : IAsyncLifetime
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

    /// <summary>Health is 200 with <c>status=ok</c> and <c>database=up</c> when Postgres is reachable.</summary>
    [Fact]
    public async Task Health_returns_ok_when_database_is_up()
    {
        var cs = _connectionString!;
        DatabaseMigrator.Migrate(cs);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Main", cs);
                builder.UseSetting("Schnapp:MigrateOnStartup", "false");
            });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonTest.Options);
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.Database.Should().Be("up");
    }
}
