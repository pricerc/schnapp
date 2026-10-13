using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Testcontainers.PostgreSql;
using Xunit;

namespace Schnapp.Api.Tests;

/// <summary>
/// Integration tests for machine create/list and find/link flows.
/// </summary>
/// <remarks>
/// Isolated temp DB via <see cref="PostgresTestSupport"/>; dispose drops host <c>schnapp_t_*</c>
/// databases (see drop failure notes on <see cref="PostgresTestSupport.DropDatabaseIfCreatedAsync"/>).
/// </remarks>
public class MachineEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";
    private string? _createdDatabase;
    private string? _baseConnectionForCleanup;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string _adminEmail = "";
    private const string AdminPassword = "AdminPass123!";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _baseConnectionForCleanup = Environment.GetEnvironmentVariable("SCHNAPP_TEST_CONNECTION");
        var (cs, container, createdDb) = await PostgresTestSupport.CreateMigratedDatabaseAsync();
        _connectionString = cs;
        _container = container;
        _createdDatabase = createdDb;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _adminEmail = $"admin-{suffix}@test.local";

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Main", _connectionString);
                builder.UseSetting("Schnapp:MigrateOnStartup", "false");
                builder.UseSetting("Schnapp:AdminEmail", _adminEmail);
                builder.UseSetting("Schnapp:AdminPassword", AdminPassword);
            });

        _client = _factory.CreateClient();
    }

    /// <summary>Disposes host/app first, then drops any host-side temp database.</summary>
    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
        if (_baseConnectionForCleanup is not null && _createdDatabase is not null)
            await PostgresTestSupport.DropDatabaseIfCreatedAsync(_baseConnectionForCleanup, _createdDatabase);
    }

    [Fact]
    public async Task Create_and_list_machine_for_current_user()
    {
        await LoginAsAdminAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/machines", new CreateMachineRequest
        {
            Name = "Dev Box",
            Fqdn = "dev.example.local",
            HardwareFingerprint = "fp-abc"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var machine = await create.Content.ReadFromJsonAsync<MachineResponse>(JsonTest.Options);
        machine!.Name.Should().Be("Dev Box");
        machine.HardwareFingerprint.Should().Be("fp-abc");

        var list = await _client!.GetFromJsonAsync<List<MachineResponse>>("/api/v1/machines", JsonTest.Options);
        list.Should().ContainSingle(m => m.Id == machine.Id);
    }

    [Fact]
    public async Task Find_matches_fingerprint_and_link_associates_user()
    {
        await LoginAsAdminAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/machines", new CreateMachineRequest
        {
            Name = "Laptop",
            Fqdn = "laptop.local",
            HardwareFingerprint = "fp-find-1"
        });
        create.EnsureSuccessStatusCode();
        var original = await create.Content.ReadFromJsonAsync<MachineResponse>(JsonTest.Options);

        // second user
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await _client!.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });

        _client!.DefaultRequestHeaders.Authorization = null;
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!"
        });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options))!.Token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var find = await _client!.PostAsJsonAsync("/api/v1/machines/find", new FindMachinesRequest
        {
            HardwareFingerprint = "fp-find-1",
            Fqdn = "laptop.local"
        });
        find.StatusCode.Should().Be(HttpStatusCode.OK);
        var candidates = await find.Content.ReadFromJsonAsync<FindMachinesResponse>(JsonTest.Options);
        candidates!.Candidates.Should().Contain(c => c.Id == original!.Id && c.Confidence == "high");

        var link = await _client!.PostAsJsonAsync($"/api/v1/machines/{Uuid7Text.Format(original!.Id)}/link", new CreateMachineRequest
        {
            HardwareFingerprint = "fp-find-1",
            Fqdn = "laptop.local"
        });
        link.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await _client!.GetFromJsonAsync<List<MachineResponse>>("/api/v1/machines", JsonTest.Options);
        list.Should().Contain(m => m.Id == original.Id);
    }

    [Fact]
    public async Task Machines_require_auth()
    {
        var response = await _client!.GetAsync("/api/v1/machines");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task LoginAsAdminAsync()
    {
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = _adminEmail,
            Password = AdminPassword
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.Token);
    }
}
