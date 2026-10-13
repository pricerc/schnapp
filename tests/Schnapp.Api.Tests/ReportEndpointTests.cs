using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Medo;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Schnapp.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace Schnapp.Api.Tests;

/// <summary>
/// Integration tests for <c>GET /api/v1/reports/time-summary</c>.
/// </summary>
public class ReportEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";
    private string? _createdDatabase;
    private string? _baseCs;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
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
    public async Task Time_summary_empty_range_returns_zero()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var summary = await _client!.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={today}&toDate={today}",
            JsonTest.Options);
        summary.Should().NotBeNull();
        summary!.Rows.Should().BeEmpty();
        summary.TotalSeconds.Should().Be(0);
        summary.FromDate.Should().Be(today);
        summary.ToDate.Should().Be(today);
    }

    [Fact]
    public async Task Time_summary_aggregates_closed_segment()
    {
        var machine = await CreateMachineAsync("RptBox");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var customer = await CreateListItemAsync("customers", $"C{suffix}", $"Customer {suffix}");
        var project = await CreateListItemAsync("projects", $"P{suffix}", $"Project {suffix}", customer.Id);
        var task = await CreateListItemAsync("tasks", $"T{suffix}", $"Task {suffix}", customer.Id, project.Id);

        var startUtc = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(9), TimeSpan.Zero);
        var stopUtc = startUtc.AddHours(2);

        var start = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            CustomerId = customer.Id,
            ProjectId = project.Id,
            TaskId = task.Id,
            StartUtc = startUtc
        }, JsonTest.Options);
        start.StatusCode.Should().Be(HttpStatusCode.Created, await start.Content.ReadAsStringAsync());
        var entry = await start.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);

        var stop = await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(entry!.Id)}/stop",
            new StopJournalRequest { StopUtc = stopUtc },
            JsonTest.Options);
        stop.EnsureSuccessStatusCode();

        var day = startUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var summary = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}",
            JsonTest.Options);

        summary.Should().NotBeNull();
        summary!.TotalSeconds.Should().Be(2 * 3600);
        summary.Rows.Should().ContainSingle();
        var row = summary.Rows[0];
        row.LocalDate.Should().Be(day);
        row.CustomerId.Should().Be(customer.Id);
        row.CustomerCode.Should().Be(customer.Code);
        row.ProjectId.Should().Be(project.Id);
        row.TaskId.Should().Be(task.Id);
        row.TotalSeconds.Should().Be(2 * 3600);
        row.EntryCount.Should().Be(1);
        row.FirstScreenshotUtc.Should().BeNull();
        row.LastScreenshotUtc.Should().BeNull();
    }

    [Fact]
    public async Task Time_summary_includes_first_and_last_screenshot_in_journal_window()
    {
        var machine = await CreateMachineAsync("ShotRpt");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var customer = await CreateListItemAsync("customers", $"S{suffix}", $"Shot {suffix}");
        var project = await CreateListItemAsync("projects", $"SP{suffix}", $"ShotP {suffix}", customer.Id);
        var task = await CreateListItemAsync("tasks", $"ST{suffix}", $"ShotT {suffix}", customer.Id, project.Id);

        var startUtc = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(9), TimeSpan.Zero);
        var stopUtc = startUtc.AddHours(2);
        var firstShot = startUtc.AddMinutes(10);
        var lastShot = startUtc.AddMinutes(90);
        var outside = startUtc.AddHours(3); // after journal stop — ignored

        var start = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            CustomerId = customer.Id,
            ProjectId = project.Id,
            TaskId = task.Id,
            StartUtc = startUtc
        }, JsonTest.Options);
        start.EnsureSuccessStatusCode();
        var entry = await start.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(entry!.Id)}/stop",
            new StopJournalRequest { StopUtc = stopUtc },
            JsonTest.Options);

        await UploadShotAsync(machine.Id, firstShot);
        await UploadShotAsync(machine.Id, lastShot);
        await UploadShotAsync(machine.Id, outside);

        var day = startUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var summary = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}",
            JsonTest.Options);

        summary.Should().NotBeNull();
        var row = summary!.Rows.Should().ContainSingle(r => r.CustomerId == customer.Id).Subject;
        row.FirstScreenshotUtc.Should().NotBeNull();
        row.LastScreenshotUtc.Should().NotBeNull();
        row.FirstScreenshotUtc!.Value.UtcDateTime.Should().Be(firstShot.UtcDateTime);
        row.LastScreenshotUtc!.Value.UtcDateTime.Should().Be(lastShot.UtcDateTime);
        row.ScreenshotSpanSeconds.Should().Be((long)(lastShot - firstShot).TotalSeconds);
    }

    [Fact]
    public async Task Time_summary_filters_by_customer_project_task()
    {
        var machine = await CreateMachineAsync("FiltRpt");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var c1 = await CreateListItemAsync("customers", $"F1{suffix}", $"Filt1 {suffix}");
        var c2 = await CreateListItemAsync("customers", $"F2{suffix}", $"Filt2 {suffix}");
        var p1 = await CreateListItemAsync("projects", $"FP1{suffix}", $"FiltP1 {suffix}", c1.Id);
        var t1 = await CreateListItemAsync("tasks", $"FT1{suffix}", $"FiltT1 {suffix}", c1.Id, p1.Id);

        var start1 = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(8), TimeSpan.Zero);
        var start2 = start1.AddHours(3);

        var s1 = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            CustomerId = c1.Id,
            ProjectId = p1.Id,
            TaskId = t1.Id,
            StartUtc = start1
        }, JsonTest.Options);
        s1.EnsureSuccessStatusCode();
        var e1 = await s1.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(e1!.Id)}/stop",
            new StopJournalRequest { StopUtc = start1.AddHours(1) },
            JsonTest.Options);

        var s2 = await _client.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            CustomerId = c2.Id,
            StartUtc = start2
        }, JsonTest.Options);
        s2.EnsureSuccessStatusCode();
        var e2 = await s2.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(e2!.Id)}/stop",
            new StopJournalRequest { StopUtc = start2.AddMinutes(30) },
            JsonTest.Options);

        var day = start1.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var byCustomer = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}" +
            $"&includeCustomer=true&includeCustomerValue={Uuid7Text.Format(c1.Id)}",
            JsonTest.Options);
        byCustomer!.Rows.Should().OnlyContain(r => r.CustomerId == c1.Id);
        byCustomer.Rows.Should().ContainSingle();
        byCustomer.TotalSeconds.Should().Be(3600);

        var byText = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}" +
            $"&includeCustomer=true&includeCustomerValue={Uri.EscapeDataString(c1.Code)}",
            JsonTest.Options);
        byText!.Rows.Should().ContainSingle(r => r.CustomerId == c1.Id);

        var byTriple = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}" +
            $"&includeCustomer=true&includeCustomerValue={Uuid7Text.Format(c1.Id)}" +
            $"&includeProject=true&includeProjectValue={Uuid7Text.Format(p1.Id)}" +
            $"&includeTask=true&includeTaskValue={Uuid7Text.Format(t1.Id)}",
            JsonTest.Options);
        byTriple!.Rows.Should().ContainSingle();
        byTriple.Rows[0].TaskId.Should().Be(t1.Id);

        // Include customer c1, exclude project p1 → no rows for that work
        var incEx = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}" +
            $"&includeCustomer=true&includeCustomerValue={Uuid7Text.Format(c1.Id)}" +
            $"&excludeProject=true&excludeProjectValue={Uuid7Text.Format(p1.Id)}",
            JsonTest.Options);
        incEx!.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Time_summary_groups_by_local_day()
    {
        var machine = await CreateMachineAsync("DayRpt");
        var day1 = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-2).AddHours(10), TimeSpan.Zero);
        var day2 = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(10), TimeSpan.Zero);

        await CreateClosedJournalAsync(machine.Id, day1, day1.AddHours(1));
        await CreateClosedJournalAsync(machine.Id, day2, day2.AddMinutes(30));

        var from = day1.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = day2.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var summary = await _client!.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={from}&toDate={to}",
            JsonTest.Options);

        summary.Should().NotBeNull();
        summary!.Rows.Should().HaveCount(2);
        summary.TotalSeconds.Should().Be(3600 + 1800);
        summary.Rows.Select(r => r.LocalDate).Should().BeEquivalentTo(new[]
        {
            day2.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            day1.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
    }

    [Fact]
    public async Task Time_summary_exclude_none_defaults_match_old_behavior()
    {
        var machine = await CreateMachineAsync("NoneRpt");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var customer = await CreateListItemAsync("customers", $"N{suffix}", $"Named {suffix}");

        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(8), TimeSpan.Zero);
        // NONE triple
        await CreateClosedJournalAsync(machine.Id, dayStart, dayStart.AddHours(1));
        // Named customer (project/task still NONE)
        var namedStart = dayStart.AddHours(2);
        var start = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            CustomerId = customer.Id,
            ProjectId = WellKnownIds.None,
            TaskId = WellKnownIds.None,
            StartUtc = namedStart
        }, JsonTest.Options);
        start.EnsureSuccessStatusCode();
        var entry = await start.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(entry!.Id)}/stop",
            new StopJournalRequest { StopUtc = namedStart.AddHours(1) },
            JsonTest.Options);

        // Fully named triple
        var project = await CreateListItemAsync("projects", $"PN{suffix}", $"Proj {suffix}", customer.Id);
        var task = await CreateListItemAsync("tasks", $"TN{suffix}", $"Task {suffix}", customer.Id, project.Id);
        var fullStart = namedStart.AddHours(3);
        var startFull = await _client.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machine.Id,
            CustomerId = customer.Id,
            ProjectId = project.Id,
            TaskId = task.Id,
            StartUtc = fullStart
        }, JsonTest.Options);
        startFull.EnsureSuccessStatusCode();
        var full = await startFull.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(full!.Id)}/stop",
            new StopJournalRequest { StopUtc = fullStart.AddMinutes(45) },
            JsonTest.Options);

        var day = dayStart.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var all = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}",
            JsonTest.Options);
        all!.Rows.Count.Should().BeGreaterThanOrEqualTo(2);

        // Exclude C/P/T when each is NONE (enabled independently) → only fully named triple remains
        var noneId = Uuid7Text.Format(WellKnownIds.None);
        var filtered = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}" +
            $"&excludeCustomer=true&excludeCustomerValue={noneId}" +
            $"&excludeProject=true&excludeProjectValue={noneId}" +
            $"&excludeTask=true&excludeTaskValue={noneId}",
            JsonTest.Options);
        filtered.Should().NotBeNull();
        filtered!.Rows.Should().ContainSingle();
        filtered.Rows[0].CustomerId.Should().Be(customer.Id);
        filtered.Rows[0].ProjectId.Should().Be(project.Id);
        filtered.Rows[0].TaskId.Should().Be(task.Id);
        filtered.TotalSeconds.Should().Be(45 * 60);
    }

    [Fact]
    public async Task Time_summary_requires_auth()
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/reports/time-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await LoginAdminAsync();
    }

    [Fact]
    public async Task Time_summary_rejects_invalid_range()
    {
        var bad = await _client!.GetAsync("/api/v1/reports/time-summary?fromDate=2026-07-20&toDate=2026-07-10");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var huge = await _client.GetAsync("/api/v1/reports/time-summary?fromDate=2020-01-01&toDate=2026-07-01");
        huge.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badFmt = await _client.GetAsync("/api/v1/reports/time-summary?fromDate=07-20-2026&toDate=2026-07-21");
        badFmt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Time_summary_is_user_scoped()
    {
        var machine = await CreateMachineAsync("ScopeRpt");
        var startUtc = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(11), TimeSpan.Zero);
        await CreateClosedJournalAsync(machine.Id, startUtc, startUtc.AddHours(3));

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await _client!.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });
        _client.DefaultRequestHeaders.Authorization = null;
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!"
        });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options))!.Token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var day = startUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var summary = await _client.GetFromJsonAsync<TimeSummaryResponse>(
            $"/api/v1/reports/time-summary?fromDate={day}&toDate={day}",
            JsonTest.Options);
        summary!.Rows.Should().BeEmpty();
        summary.TotalSeconds.Should().Be(0);

        await LoginAdminAsync();
    }

    private async Task CreateClosedJournalAsync(Uuid7 machineId, DateTimeOffset startUtc, DateTimeOffset stopUtc)
    {
        var start = await _client!.PostAsJsonAsync("/api/v1/journals/start", new StartJournalRequest
        {
            MachineId = machineId,
            StartUtc = startUtc
        }, JsonTest.Options);
        start.EnsureSuccessStatusCode();
        var entry = await start.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonTest.Options);
        var stop = await _client.PostAsJsonAsync(
            $"/api/v1/journals/{Uuid7Text.Format(entry!.Id)}/stop",
            new StopJournalRequest { StopUtc = stopUtc },
            JsonTest.Options);
        stop.EnsureSuccessStatusCode();
    }

    private async Task UploadShotAsync(Uuid7 machineId, DateTimeOffset timestampUtc)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(MakeTinyPng());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "r.png");
        content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
        {
            MachineId = machineId,
            Timestamp = timestampUtc,
            ClientTimeZoneId = "UTC",
            MonitorName = "report-test"
        }, JsonTest.Options)), "metadata");
        var response = await _client!.PostAsync("/api/v1/screenshots", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Minimal valid 1x1 PNG.</summary>
    private static byte[] MakeTinyPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task LoginAdminAsync()
    {
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = _adminEmail,
            Password = AdminPassword
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options);
        _client.DefaultRequestHeaders.Authorization =
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

    private async Task<ListItemResponse> CreateListItemAsync(
        string kind,
        string code,
        string name,
        Uuid7? customerId = null,
        Uuid7? projectId = null)
    {
        var create = await _client!.PostAsJsonAsync($"/api/v1/lists/{kind}", new ListItemRequest
        {
            Code = code,
            Name = name,
            CustomerId = customerId,
            ProjectId = projectId
        }, JsonTest.Options);
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        return (await create.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options))!;
    }
}
