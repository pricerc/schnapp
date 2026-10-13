using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using Schnapp.Domain;
using Testcontainers.PostgreSql;
using Xunit;

using Medo;

namespace Schnapp.Api.Tests;

/// <summary>
/// Integration tests for tag dictionary collation and customer-scoped autocomplete.
/// </summary>
public class TagEndpointTests : IAsyncLifetime
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
    public async Task Writing_list_tags_collates_into_dictionary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var create = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"T{suffix}",
            Name = $"TagCust {suffix}",
            Tags = "alpha,beta;gamma"
        }, JsonTest.Options);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await create.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);
        customer.Should().NotBeNull();
        customer!.Tags.Should().Be("alpha beta gamma");

        var all = await _client.GetFromJsonAsync<List<TagResponse>>("/api/v1/lists/tags", JsonTest.Options);
        all.Should().NotBeNull();
        all!.Select(t => t.Tag).Should().Contain(new[] { "alpha", "beta", "gamma" });

        // Customer-scoped dictionary rows for tags written on the customer entity.
        var scoped = await _client.GetFromJsonAsync<List<TagResponse>>(
            $"/api/v1/lists/tags?customerId={Uri.EscapeDataString(Uuid7Text.Format(customer.Id))}",
            JsonTest.Options);
        scoped.Should().NotBeNull();
        scoped!.Where(t => t.CustomerId == customer.Id).Select(t => t.Tag)
            .Should().Contain(new[] { "alpha", "beta", "gamma" });
    }

    [Fact]
    public async Task Explicit_tag_create_and_customer_scope()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var custResp = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"C{suffix}",
            Name = $"Cust {suffix}"
        }, JsonTest.Options);
        custResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await custResp.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);

        var global = await _client.PostAsJsonAsync("/api/v1/lists/tags", new TagRequest
        {
            Tag = $"g{suffix}"
        }, JsonTest.Options);
        global.StatusCode.Should().Be(HttpStatusCode.Created);

        var scoped = await _client.PostAsJsonAsync("/api/v1/lists/tags", new TagRequest
        {
            Tag = $"c{suffix}",
            CustomerId = customer!.Id
        }, JsonTest.Options);
        scoped.StatusCode.Should().Be(HttpStatusCode.Created);

        var forCustomer = await _client.GetFromJsonAsync<List<TagResponse>>(
            $"/api/v1/lists/tags?customerId={Uri.EscapeDataString(Uuid7Text.Format(customer.Id))}",
            JsonTest.Options);
        forCustomer!.Select(t => t.Tag).Should().Contain(new[] { $"g{suffix}", $"c{suffix}" });

        // A different customer should see global but not the other customer's tag.
        var otherCust = await _client.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"D{suffix}",
            Name = $"Other {suffix}"
        }, JsonTest.Options);
        var other = await otherCust.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);
        var forOther = await _client.GetFromJsonAsync<List<TagResponse>>(
            $"/api/v1/lists/tags?customerId={Uri.EscapeDataString(Uuid7Text.Format(other!.Id))}",
            JsonTest.Options);
        forOther!.Select(t => t.Tag).Should().Contain($"g{suffix}");
        forOther.Select(t => t.Tag).Should().NotContain($"c{suffix}");
    }

    [Fact]
    public async Task Project_tags_use_customer_scope()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var custResp = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"PC{suffix}",
            Name = $"PCust {suffix}"
        }, JsonTest.Options);
        var customer = await custResp.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);

        var proj = await _client.PostAsJsonAsync("/api/v1/lists/projects", new ListItemRequest
        {
            Code = $"P{suffix}",
            Name = $"Proj {suffix}",
            CustomerId = customer!.Id,
            Tags = "projtag1,projtag2"
        }, JsonTest.Options);
        proj.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await proj.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);
        project!.Tags.Should().Be("projtag1 projtag2");

        var scoped = await _client.GetFromJsonAsync<List<TagResponse>>(
            $"/api/v1/lists/tags?customerId={Uri.EscapeDataString(Uuid7Text.Format(customer.Id))}",
            JsonTest.Options);
        scoped!.Where(t => t.CustomerId == customer.Id).Select(t => t.Tag)
            .Should().Contain(new[] { "projtag1", "projtag2" });
    }

    [Fact]
    public async Task Global_tag_blocks_customer_specific_duplicate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var tag = $"shared{suffix}";
        var custResp = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"G{suffix}",
            Name = $"GlobalBlock {suffix}"
        }, JsonTest.Options);
        var customer = await custResp.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);

        (await _client.PostAsJsonAsync("/api/v1/lists/tags", new TagRequest { Tag = tag }, JsonTest.Options))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var dup = await _client.PostAsJsonAsync("/api/v1/lists/tags", new TagRequest
        {
            Tag = tag,
            CustomerId = customer!.Id
        }, JsonTest.Options);
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Free-text collation must not create a customer copy either.
        await _client.PutAsJsonAsync($"/api/v1/lists/customers/{Uuid7Text.Format(customer.Id)}", new ListItemRequest
        {
            Code = customer.Code,
            Name = customer.Name,
            Tags = tag
        }, JsonTest.Options);

        var customerOnly = await _client.GetFromJsonAsync<List<TagResponse>>(
            $"/api/v1/lists/tags?customerId={Uri.EscapeDataString(Uuid7Text.Format(customer.Id))}&scope=customer",
            JsonTest.Options);
        customerOnly!.Select(t => t.Tag).Should().NotContain(tag);
    }

    [Fact]
    public async Task Assign_to_customer_and_make_global()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var tag = $"move{suffix}";
        var custResp = await _client!.PostAsJsonAsync("/api/v1/lists/customers", new ListItemRequest
        {
            Code = $"M{suffix}",
            Name = $"MoveCust {suffix}"
        }, JsonTest.Options);
        var customer = await custResp.Content.ReadFromJsonAsync<ListItemResponse>(JsonTest.Options);

        (await _client.PostAsJsonAsync("/api/v1/lists/tags", new TagRequest
        {
            Tag = tag,
            Notes = "hello"
        }, JsonTest.Options)).StatusCode.Should().Be(HttpStatusCode.Created);

        var assign = await _client.PostAsJsonAsync("/api/v1/lists/tags/assign-customer", new ReassignTagRequest
        {
            Tag = tag,
            CustomerId = customer!.Id
        }, JsonTest.Options);
        assign.StatusCode.Should().Be(HttpStatusCode.OK);
        var assigned = await assign.Content.ReadFromJsonAsync<TagResponse>(JsonTest.Options);
        assigned!.CustomerId.Should().Be(customer.Id);
        assigned.Notes.Should().Be("hello");

        var global = await _client.GetFromJsonAsync<List<TagResponse>>("/api/v1/lists/tags?scope=global", JsonTest.Options);
        global!.Select(t => t.Tag).Should().NotContain(tag);

        var makeGlobal = await _client.PostAsJsonAsync("/api/v1/lists/tags/make-global", new ReassignTagRequest
        {
            Tag = tag,
            CustomerId = customer.Id
        }, JsonTest.Options);
        makeGlobal.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await makeGlobal.Content.ReadFromJsonAsync<TagResponse>(JsonTest.Options);
        again!.CustomerId.Should().BeNull();

        var customerOnly = await _client.GetFromJsonAsync<List<TagResponse>>(
            $"/api/v1/lists/tags?customerId={Uri.EscapeDataString(Uuid7Text.Format(customer.Id))}&scope=customer",
            JsonTest.Options);
        customerOnly!.Select(t => t.Tag).Should().NotContain(tag);
    }

    private async Task LoginAdminAsync()
    {
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Login = _adminEmail,
            Password = AdminPassword
        }, JsonTest.Options);
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.Token);
    }
}
