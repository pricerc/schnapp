using DotNet.Testcontainers.Builders;
using Schnapp.Data;
using Testcontainers.FirebirdSql;

namespace Schnapp.Data.Tests;

/// <summary>
/// Shared Firebird 5 SuperServer setup for data tests.
/// </summary>
/// <remarks>
/// Prefer <c>SCHNAPP_TEST_FIREBIRD_CONNECTION</c>. Without it, Testcontainers
/// <c>firebirdsql/firebird:5.0.2</c> (not the default v4 image). The official
/// image has no HEALTHCHECK, so wait on port 3050 rather than container health.
/// </remarks>
internal static class FirebirdTestSupport
{
    internal const string Image = FirebirdDefaults.TestImage;

    public static async Task<(string ConnectionString, FirebirdSqlContainer? Container)> CreateMigratedDatabaseAsync()
    {
        var external = Environment.GetEnvironmentVariable("SCHNAPP_TEST_FIREBIRD_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            FirebirdDefaults.WaitUntilCatalogOpen(external);
            DatabaseMigrator.Migrate(DatabaseProvider.Firebird, external);
            return (external, null);
        }

        try
        {
            var container = CreateContainer();
            await container.StartAsync();
            var cs = FirebirdDefaults.NormalizeConnectionString(container.GetConnectionString());
            FirebirdDefaults.WaitUntilCatalogOpen(cs);
            DatabaseMigrator.Migrate(DatabaseProvider.Firebird, cs);
            return (cs, container);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Docker", StringComparison.Ordinal)
                                   || ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "No SCHNAPP_TEST_FIREBIRD_CONNECTION and Testcontainers could not reach a container runtime.",
                ex);
        }
    }

    /// <summary>
    /// Official Firebird 5 SuperServer image with a created UTF8 database.
    /// </summary>
    internal static FirebirdSqlContainer CreateContainer() =>
        new FirebirdSqlBuilder(Image)
            .WithDatabase("schnapp.fdb")
            .WithUsername("schnapp")
            .WithPassword("schnapp")
            .WithEnvironment("FIREBIRD_ROOT_PASSWORD", "masterkey")
            .WithEnvironment("FIREBIRD_DATABASE_DEFAULT_CHARSET", "UTF8")
            .WithEnvironment("FIREBIRD_DATABASE_PAGE_SIZE", "16384")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3050))
            .Build();
}

/// <summary>One migrated Firebird 5 catalog shared by tests in <see cref="FirebirdCatalogCollection"/>.</summary>
public sealed class FirebirdCatalogFixture : IAsyncLifetime
{
    private FirebirdSqlContainer? _container;

    /// <summary>Connection string after migrations have been applied.</summary>
    public string ConnectionString { get; private set; } = "";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        (ConnectionString, _container) = await FirebirdTestSupport.CreateMigratedDatabaseAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

/// <summary>Serial Firebird catalog tests sharing one SuperServer container.</summary>
[CollectionDefinition("Firebird catalog")]
public sealed class FirebirdCatalogCollection : ICollectionFixture<FirebirdCatalogFixture>;
