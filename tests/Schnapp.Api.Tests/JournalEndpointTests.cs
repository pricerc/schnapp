using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Testcontainers.PostgreSql;
using Xunit;

namespace Schnapp.Api.Tests;

public class JournalEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";
    private string? _createdDatabase;
    private string? _baseCs;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client = null!;
    private string _adminEmail = "";
    private const string AdminPassword = "AdminPass123!";

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

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        if (_baseCs is not null && _createdDatabase is not null)
            await PostgresTestSupport.DropDatabaseIfCreatedAsync(_baseCs, _createdDatabase);
    }

    [Fact]
    public async Task Start_stop_and_second_start_closes_first()
    {
        var machine = await CreateMachineAsync("JBox");
        var start1 = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            Notes = "first"
        }, JsonTest.Options);
        start1.StatusCode.Should().Be(HttpStatusCode.Created, await start1.Content.ReadAsStringAsync());
        var j1 = await start1.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        j1!.IsOpen.Should().BeTrue();

        var start2 = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            Notes = "second"
        }, JsonTest.Options);
        start2.EnsureSuccessStatusCode();
        var j2 = await start2.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        j2!.IsOpen.Should().BeTrue();
        j2.Id.Should().NotBe(j1.Id);

        var closed = await _client!.GetFromJsonAsync<JournalEntryResponse>($"/api/v1/journals/{Uuid7Text.Format(j1.Id)}", JsonTest.Options);
        closed!.StopUtc.Should().NotBeNull();

        var stop = await _client!.PostAsJsonAsync($"/api/v1/journals/{Uuid7Text.Format(j2.Id)}/stop", new StopJournalRequest(), JsonTest.Options);
        stop.EnsureSuccessStatusCode();
        var stopped = await stop.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        stopped!.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task List_rolls_open_journal_to_today_clone()
    {
        var machine = await CreateMachineAsync("DayBox");
        // Explicit UTC offset — bare DateTime → DateTimeOffset uses the host local zone.
        var yesterday = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(10), TimeSpan.Zero);
        var start1 = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            Notes = "overnight",
            Tags = "day-boundary",
            StartUtc = yesterday
        }, JsonTest.Options);
        start1.StatusCode.Should().Be(HttpStatusCode.Created, await start1.Content.ReadAsStringAsync());
        var j1 = await start1.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        j1!.IsOpen.Should().BeTrue();
        j1.StartUtc.UtcDateTime.Should().Be(yesterday.UtcDateTime);

        var list = await _client!.GetFromJsonAsync<List<JournalEntryResponse>>("/api/v1/journals", JsonTest.Options);
        list.Should().NotBeNull();

        var closed = list!.First(j => j.Id == j1.Id);
        closed.IsOpen.Should().BeFalse();
        closed.StopUtc.Should().NotBeNull();
        closed.Notes.Should().Be("overnight");
        // Closed at end of start's local day (admin TZ is UTC → next UTC midnight).
        closed.StopUtc!.Value.UtcDateTime.Should().Be(yesterday.UtcDateTime.Date.AddDays(1));

        var open = list.Where(j => j.IsOpen && j.MachineId == machine.Id).ToList();
        open.Should().HaveCount(1);
        open[0].Id.Should().NotBe(j1.Id);
        open[0].Notes.Should().Be("overnight");
        open[0].Tags.Should().Be("day-boundary");
        open[0].JournalTargetId.Should().Be(j1.JournalTargetId);
        // Contiguous day-boundary chain: final open starts where the previous segment stopped.
        open[0].StartUtc.Should().Be(closed.StopUtc.Value);
        open[0].StartUtc.UtcDateTime.Date.Should().Be(DateTime.UtcNow.Date);
    }

    [Fact]
    public async Task Journals_are_user_scoped()
    {
        var machine = await CreateMachineAsync("ScopeBox");
        var start = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id
        }, JsonTest.Options);
        var j = await start.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await _client!.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });
        _client!.DefaultRequestHeaders.Authorization = null;
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!"
        });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options))!.Token;
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var get = await _client.GetAsync($"/api/v1/journals/{Uuid7Text.Format(j!.Id)}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private async Task<MachineResponse> CreateMachineAsync(string name)
    {
        var create = await _client!.PostAsJsonAsync("/api/v1/machines", new CreateMachineRequest
        {
            Name = name + "-" + Guid.NewGuid().ToString("N")[..6],
            HardwareFingerprint = "fp-" + Guid.NewGuid().ToString("N")[..8]
        });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<MachineResponse>(JsonTest.Options))!;
    }
}
