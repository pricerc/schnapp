using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Schnapp.Data;
using Testcontainers.FirebirdSql;
using Xunit;

namespace Schnapp.Api.Tests;

/// <summary>Health check against Firebird 5 SuperServer.</summary>
public class FirebirdHealthEndpointTests : IAsyncLifetime
{
    private FirebirdSqlContainer? _container;
    private string? _connectionString;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("SCHNAPP_TEST_FIREBIRD_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _connectionString = external;
            DatabaseMigrator.Migrate(DatabaseProvider.Firebird, external);
            return;
        }

        try
        {
            _container = new FirebirdSqlBuilder(FirebirdDefaults.TestImage)
                .WithDatabase("schnapp.fdb")
                .WithUsername("schnapp")
                .WithPassword("schnapp")
                .WithEnvironment("FIREBIRD_ROOT_PASSWORD", "masterkey")
                .WithEnvironment("FIREBIRD_DATABASE_DEFAULT_CHARSET", "UTF8")
                .WithEnvironment("FIREBIRD_DATABASE_PAGE_SIZE", "16384")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3050))
                .Build();
            await _container.StartAsync();
            _connectionString = FirebirdDefaults.NormalizeConnectionString(_container.GetConnectionString());
            FirebirdDefaults.WaitUntilCatalogOpen(_connectionString);
            DatabaseMigrator.Migrate(DatabaseProvider.Firebird, _connectionString);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Docker", StringComparison.Ordinal)
                                   || ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "No SCHNAPP_TEST_FIREBIRD_CONNECTION and Testcontainers could not reach Docker/Podman.",
                ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    [Fact]
    public async Task Health_returns_ok_when_firebird_is_up()
    {
        var cs = _connectionString!;
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Main", cs);
                builder.UseSetting("Schnapp:DatabaseProvider", "Firebird");
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
