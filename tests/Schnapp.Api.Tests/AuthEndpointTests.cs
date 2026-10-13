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
/// Integration tests for auth, tokens, profile, and admin user endpoints.
/// </summary>
/// <remarks>
/// Uses <see cref="PostgresTestSupport.CreateMigratedDatabaseAsync"/> so each class run gets an
/// empty DB (admin bootstrap only seeds when zero users). Dispose drops host-side
/// <c>schnapp_t_*</c> DBs; if drop fails or the process is killed, run
/// <c>./scripts/cleanup-test-dbs.sh</c>.
/// </remarks>
public class AuthEndpointTests : IAsyncLifetime
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

    /// <summary>
    /// Disposes the factory/client first so Npgsql pools release, then drops the temp DB.
    /// Order matters: drop while app connections remain open often fails.
    /// </summary>
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
    public async Task Login_fails_with_bad_password()
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = "nobody@test.local",
            Password = "wrong"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_and_me_succeed_after_admin_bootstrap()
    {
        var login = await LoginAsync(_adminEmail, AdminPassword);
        login.Token.Should().NotBeNullOrWhiteSpace();
        login.User.Role.Should().Be("Admin");
        login.User.Email.Should().Be(_adminEmail);

        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var me = await _client.GetFromJsonAsync<UserProfileResponse>("/api/v1/me", JsonTest.Options);
        me.Should().NotBeNull();
        me!.Email.Should().Be(_adminEmail);
        me.TimeZoneId.Should().Be("UTC");
    }

    [Fact]
    public async Task Me_requires_token()
    {
        var response = await _client!.GetAsync("/api/v1/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_create_user_and_user_can_login()
    {
        var adminLogin = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminLogin.Token);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await _client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"user-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<UserProfileResponse>(JsonTest.Options);
        created!.Role.Should().Be("User");

        _client.DefaultRequestHeaders.Authorization = null;
        var userLogin = await LoginAsync($"user-{suffix}@test.local", "UserPass123!");
        userLogin.User.Code.Should().Be($"u{suffix}");
    }

    [Fact]
    public async Task Admin_can_list_users()
    {
        var adminLogin = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminLogin.Token);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await _client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"list-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"List {suffix}",
            Code = $"l{suffix}"
        });
        create.EnsureSuccessStatusCode();

        var list = await _client.GetFromJsonAsync<List<UserProfileResponse>>("/api/v1/admin/users", JsonTest.Options);
        list.Should().NotBeNull();
        list!.Should().Contain(u => u.Email == _adminEmail);
        list.Should().Contain(u => u.Email == $"list-{suffix}@test.local");
    }

    [Fact]
    public async Task Non_admin_cannot_list_users()
    {
        var adminLogin = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminLogin.Token);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await _client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"user-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });
        create.EnsureSuccessStatusCode();

        _client.DefaultRequestHeaders.Authorization = null;
        var userLogin = await LoginAsync($"user-{suffix}@test.local", "UserPass123!");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", userLogin.Token);

        var forbidden = await _client.GetAsync("/api/v1/admin/users");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_admin_cannot_create_user()
    {
        var adminLogin = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminLogin.Token);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await _client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"user-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });
        create.EnsureSuccessStatusCode();

        _client.DefaultRequestHeaders.Authorization = null;
        var userLogin = await LoginAsync($"user-{suffix}@test.local", "UserPass123!");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", userLogin.Token);

        var forbidden = await _client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"other-{suffix}@test.local",
            Password = "x",
            Name = "Other"
        });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_token_is_rejected()
    {
        var login = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var revoke = await _client.DeleteAsync($"/api/v1/auth/tokens/{login.TokenId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var me = await _client.GetAsync("/api/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patch_me_updates_timezone()
    {
        var login = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var response = await _client.PatchAsJsonAsync("/api/v1/me", new UpdateProfileRequest
        {
            TimeZoneId = "Pacific/Auckland"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonTest.Options);
        profile!.TimeZoneId.Should().Be("Pacific/Auckland");
    }

    [Fact]
    public async Task Login_by_code_and_passwordless_user()
    {
        var admin = await LoginAsync(_adminEmail, AdminPassword);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", admin.Token);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await _client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"pl-{suffix}@test.local",
            Password = null,
            Name = $"Passless {suffix}",
            Code = $"pl{suffix}"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());

        _client.DefaultRequestHeaders.Authorization = null;
        var byCode = await LoginAsync($"pl{suffix}", null);
        byCode.User.Code.Should().Be($"pl{suffix}");

        var byName = await LoginAsync($"Passless {suffix}", "");
        byName.User.Email.Should().Be($"pl-{suffix}@test.local");

        // Password set on account still required when hash exists — admin has password
        var bad = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Login = "admin",
            Password = null
        });
        // admin code is "admin" but password required
        // may collide if multiple admins — use email without password
        var badEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Login = _adminEmail
        });
        badEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<LoginResponse> LoginAsync(string identity, string? password)
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Login = identity,
            Password = password
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options);
        body.Should().NotBeNull();
        return body!;
    }
}
