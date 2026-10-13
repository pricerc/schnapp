namespace Schnapp.Data;

/// <summary>
/// Catalog engine for <see cref="Schnapp.Api"/>. Chosen with <c>Schnapp:DatabaseProvider</c>.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>PostgreSQL (default when the config key is omitted).</summary>
    PostgreSQL = 0,

    /// <summary>Firebird 5 SuperServer.</summary>
    Firebird = 1
}

/// <summary>Parses <c>Schnapp:DatabaseProvider</c> from configuration.</summary>
public static class DatabaseProviderParser
{
    /// <summary>
    /// Maps a config value to a provider. Null/empty is PostgreSQL.
    /// </summary>
    /// <exception cref="InvalidOperationException">Unknown provider name.</exception>
    public static DatabaseProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DatabaseProvider.PostgreSQL;

        if (value.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Npgsql", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.PostgreSQL;

        if (value.Equals("Firebird", StringComparison.OrdinalIgnoreCase)
            || value.Equals("FirebirdSql", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.Firebird;

        throw new InvalidOperationException(
            "Unknown Schnapp:DatabaseProvider '" + value + "'. Use PostgreSQL or Firebird.");
    }
}
