using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Schnapp.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace Schnapp.Api.Tests;

/// <summary>
/// Integration tests for list CRUD (customers/projects/tasks), journal targets, and settings.
/// </summary>
/// <remarks>
/// Isolated temp DB via <see cref="PostgresTestSupport"/>; logs in as bootstrapped admin in
/// <see cref="InitializeAsync"/>. Dispose must drop host <c>schnapp_t_*</c> DBs or they leak.
/// </remarks>
public class ListEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";
    private string? _createdDatabase;
    private string? _baseCs;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string _adminEmail = "";
    private const string AdminPassword = "AdminPass123!";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _baseCs = Environment.GetEnvironmentVariable("SCHNAPP_TEST_CONNECTION");
        var (cs, container, createdDb) = await PostgresTestSupport.CreateMigratedDatabaseAsync();
        _connectionString = cs;
        _container = container;
        _createdDatabase = createdDb;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _adminEmail = $"admin-{suffix}@test.local";
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Main", _connectionString);
            b.UseSetting("Schnapp:MigrateOnStartup", "false");
            b.UseSetting("Schnapp:AdminEmail", _adminEmail);
            b.UseSetting("Schnapp:AdminPassword", AdminPassword);
        });
        _client = _factory.CreateClient();
        await LoginAdminAsync();
    }

    /// <summary>Disposes host/app first, then drops any host-side temp database.</summary>
    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        if (_baseCs is not null && _createdDatabase is not null)
            await PostgresTestSupport.DropDatabaseIfCreatedAsync(_baseCs, _createdDatabase);
    }

    [Fact]
    public async Task Customers_include_none_and_create_unique()
    {
        var list = await _client!.GetFromJsonAsync<List<ListItemResponse>>("/api/v1/lists/customers", JsonTest.Options);
        list.Should().Contain(c => c.Id == WellKnownIds.None && c.Code == "-");

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var create = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"C{suffix}",
            Name = $"Customer {suffix}"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var dup = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"C{suffix}",
            Name = $"Other {suffix}"
        });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var delNone = await _client!.DeleteAsync($"/api/v1/lists/customers/{Uuid7Text.Format(WellKnownIds.None)}");
        delNone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Project_defaults_customer_to_none()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var create = await _client!.PostAsJsonAsync("/api/v1/lists/projects", new ListItemRequest
        {
            Code = $"P{suffix}",
            Name = $"Project {suffix}"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var p = await create.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);
        p!.CustomerId.Should().Be(WellKnownIds.None);
    }

    [Fact]
    public async Task Journal_target_get_or_create_is_idempotent()
    {
        var body = new JournalTargetRequest
        {
            CustomerId = WellKnownIds.None,
            ProjectId = WellKnownIds.None,
            TaskId = WellKnownIds.None,
            Code = "-",
            Name = "(none)"
        };
        var a = await _client!.PostAsJsonAsync("/api/v1/lists/journal-targets", body, JsonTest.Options);
        var b = await _client!.PostAsJsonAsync("/api/v1/lists/journal-targets", body, JsonTest.Options);
        a.EnsureSuccessStatusCode();
        b.EnsureSuccessStatusCode();
        var ta = await a.Content.ReadFromJsonAsync<JournalTargetResponse>(JsonTest.Options);
        var tb = await b.Content.ReadFromJsonAsync<JournalTargetResponse>(JsonTest.Options);
        ta!.Id.Should().Be(tb!.Id);
    }

    [Fact]
    public async Task Settings_seeded_and_admin_can_patch()
    {
        var list = await _client!.GetFromJsonAsync<List<SettingResponse>>("/api/v1/settings", JsonTest.Options);
        list.Should().Contain(s => s.Key == "journal.recent_last_seen_minutes");
        list.Should().Contain(s => s.Key == "screenshot.max_upload_bytes");
        list.Should().Contain(s => s.Key == "screenshot.thumb_max_edge");
        list.Should().Contain(s => s.Key == "screenshot.jpeg_quality");

        var patch = await _client!.PatchAsJsonAsync(
            "/api/v1/settings/journal.recent_last_seen_minutes", new UpdateSettingRequest { Value = "45" }, JsonTest.Options);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var s = await patch.Content.ReadFromJsonAsync<SettingResponse>(JsonTest.Options);
        s!.Value.Should().Be("45");
    }

    private async Task LoginAdminAsync()
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
