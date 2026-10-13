using Dapper;
using FirebirdSql.Data.FirebirdClient;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Schnapp.Data;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;
using Xunit;

using Medo;

namespace Schnapp.Data.Tests;

/// <summary>DbUp Firebird 5 schema, seeds, UTC instants, Uuid7 byte order.</summary>
[Collection("Firebird catalog")]
public class FirebirdMigrationTests
{
    private readonly string _connectionString;

    public FirebirdMigrationTests(FirebirdCatalogFixture catalog)
    {
        _connectionString = catalog.ConnectionString;
    }

    [Fact]
    public async Task Migrate_creates_schema_and_seeds_none_rows()
    {
        DapperConfig.EnsureConfigured();
        var cs = _connectionString;

        await using var conn = new FbConnection(cs);
        await conn.OpenAsync();

        var customerCode = await conn.ExecuteScalarAsync<string>(
            "SELECT code FROM customer WHERE id = @id",
            new { id = WellKnownIds.None });
        customerCode.Should().Be("NONE");

        var setting = await conn.ExecuteScalarAsync<string>(
            """SELECT "value" FROM setting WHERE "key" = @key""",
            new { key = "journal.recent_last_seen_minutes" });
        setting.Should().Be("30");

        var openIndex = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CAST(COUNT(*) AS INTEGER) FROM RDB$INDICES
            WHERE TRIM(RDB$INDEX_NAME) = 'UX_JOURNAL_ONE_OPEN_PER_USER_MACHINE'
            """);
        openIndex.Should().Be(1);

        var entityStatusCount = await conn.ExecuteScalarAsync<int>(
            "SELECT CAST(COUNT(*) AS INTEGER) FROM entity_status");
        entityStatusCount.Should().BeGreaterThanOrEqualTo(12);

        var pageSize = await conn.ExecuteScalarAsync<int>(
            "SELECT MON$PAGE_SIZE FROM MON$DATABASE");
        pageSize.Should().BeGreaterThanOrEqualTo(FirebirdDefaults.PageSize);
    }

    [Fact]
    public async Task DateTimeOffset_round_trip_stores_utc_not_local_wall_clock()
    {
        DapperConfig.EnsureConfigured();
        var cs = _connectionString;
        var factory = new FirebirdConnectionFactory(cs);
        var dialect = new FirebirdDialect();
        var logger = NullLogger<UserRepository>.Instance;
        var users = new UserRepository(factory, dialect, logger);

        var local = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(10));
        var user = new User
        {
            Id = IdFactory.NewId(),
            Code = "utc1",
            Name = "UTC One",
            Email = "utc1@example.test",
            PasswordHash = "",
            Role = UserRole.User,
            TimeZoneId = "Pacific/Auckland",
            CreatedAt = local,
            UpdatedAt = local
        };
        await users.InsertAsync(user);

        var loaded = await users.GetByIdAsync(user.Id);
        loaded.Should().NotBeNull();
        loaded!.CreatedAt.UtcDateTime.Should().Be(local.UtcDateTime);
        loaded.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        loaded.TimeZoneId.Should().Be("Pacific/Auckland");
    }

    [Fact]
    public async Task Uuid7_binary_sort_matches_rfc_text_order()
    {
        DapperConfig.EnsureConfigured();
        var cs = _connectionString;
        await using var conn = new FbConnection(cs);
        await conn.OpenAsync();

        var a = Uuid7.Parse("01900000-0000-7000-8000-000000000001");
        var b = Uuid7.Parse("01900000-0000-7000-8000-000000000002");
        var c1 = new Customer
        {
            Id = a,
            Code = "UA",
            Name = "Uuid A",
            Status = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var c2 = new Customer
        {
            Id = b,
            Code = "UB",
            Name = "Uuid B",
            Status = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var factory = new FirebirdConnectionFactory(cs);
        var dialect = new FirebirdDialect();
        var lists = new ListRepository(factory, dialect, NullLogger<ListRepository>.Instance);
        await lists.InsertCustomerAsync(c1);
        await lists.InsertCustomerAsync(c2);

        var ordered = (await conn.QueryAsync<Uuid7>(
            "SELECT id FROM customer WHERE code STARTING WITH 'U' ORDER BY id")).ToList();
        ordered.Should().Equal(a, b);
    }

    [Fact]
    public async Task Setting_upsert_round_trips_quoted_key_and_value()
    {
        DapperConfig.EnsureConfigured();
        var factory = new FirebirdConnectionFactory(_connectionString);
        var dialect = new FirebirdDialect();
        var settings = new SettingRepository(factory, dialect, NullLogger<SettingRepository>.Instance);

        await settings.UpsertAsync("firebird.test.knob", "42", "notes", CancellationToken.None);
        var loaded = await settings.GetAsync("firebird.test.knob");
        loaded.Should().NotBeNull();
        loaded!.Value.Should().Be("42");
        loaded.Notes.Should().Be("notes");

        await settings.UpsertAsync("firebird.test.knob", "43", null, CancellationToken.None);
        loaded = await settings.GetAsync("firebird.test.knob");
        loaded!.Value.Should().Be("43");
        loaded.Notes.Should().Be("notes");
    }
}
