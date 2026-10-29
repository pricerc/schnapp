namespace Schnapp.Data;

using System.Reflection;

using DbUp;

using Microsoft.Extensions.Logging;

/// <summary>
/// Applies embedded SQL migrations (DbUp) for the selected <see cref="DatabaseProvider"/>.
/// </summary>
/// <remarks>
/// Invoked at API startup when <c>Schnapp:MigrateOnStartup</c> is true, and from integration tests
/// before exercising endpoints. PostgreSQL scripts stay at <c>Migrations/*.sql</c> (unchanged
/// DbUp names for existing installs). Firebird scripts live under <c>Migrations/Firebird/</c>.
/// </remarks>
public static class DatabaseMigrator
{
    /// <summary>
    /// Ensures the database exists and runs pending PostgreSQL migration scripts.
    /// </summary>
    /// <remarks>Kept for existing tests and callers; equivalent to <see cref="Migrate(DatabaseProvider,string,ILogger?)"/> with PostgreSQL.</remarks>
    public static void Migrate(string connectionString, ILogger? logger = null)
        => Migrate(DatabaseProvider.PostgreSQL, connectionString, logger);

    /// <summary>
    /// Ensures the database exists and runs pending scripts for <paramref name="provider"/>.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="connectionString"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">When a migration script fails.</exception>
    public static void Migrate(DatabaseProvider provider, string connectionString, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        var assembly = Assembly.GetExecutingAssembly();
        var result = provider switch
        {
            DatabaseProvider.Firebird => UpgradeFirebird(assembly, connectionString),
            _ => UpgradePostgres(assembly, connectionString)
        };

        if (!result.Successful)
        {
            logger?.LogError(result.Error, "Database migration failed");
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        logger?.LogInformation("Database migrations applied successfully ({Provider})", provider);
    }

    private static DbUp.Engine.DatabaseUpgradeResult UpgradePostgres(Assembly assembly, string connectionString)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);
        return DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, IsPostgreSqlScript)
            .LogToConsole()
            .Build()
            .PerformUpgrade();
    }

    private static DbUp.Engine.DatabaseUpgradeResult UpgradeFirebird(Assembly assembly, string connectionString)
    {
        // Do not call DbUp EnsureDatabase: FirebirdClient CreateDatabase defaults to PAGE_SIZE 4096,
        // which cannot hold UTF8 VARCHAR(255) unique indexes. The .fdb is created by
        // setup-database-firebird.sh or the test image (PAGE_SIZE 16384).
        return DeployChanges.To
            .FirebirdDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, IsFirebirdScript)
            .LogToConsole()
            .Build()
            .PerformUpgrade();
    }

    private static bool IsPostgreSqlScript(string resourceName) =>
        resourceName.EndsWith(".sql", StringComparison.Ordinal)
        && resourceName.Contains(".Migrations.", StringComparison.Ordinal)
        && !resourceName.Contains(".Migrations.Firebird.", StringComparison.Ordinal);

    private static bool IsFirebirdScript(string resourceName) =>
        resourceName.EndsWith(".sql", StringComparison.Ordinal)
        && resourceName.Contains(".Migrations.Firebird.", StringComparison.Ordinal);
}
