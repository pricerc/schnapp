namespace Schnapp.Data;

using Dapper;

/// <summary>
/// One-time Dapper global configuration for this process.
/// </summary>
public static class DapperConfig
{
    private static int _configured;

    /// <summary>
    /// Enables underscore-to-Pascal mapping and registers <see cref="Uuid7"/> type handlers.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="ServiceCollectionExtensions.AddSchnappData"/> before any queries run.
    /// Safe to call multiple times (idempotent).
    /// </remarks>
    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
            return;

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(Uuid7TypeHandler.Instance);
        SqlMapper.AddTypeHandler(NullableUuid7TypeHandler.Instance);
        // DateTimeOffset is a built-in Dapper type (DbType 27). FirebirdClient rejects that
        // DbType, so drop the default map and force the UTC TIMESTAMP handler.
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(typeof(DateTimeOffset), DateTimeOffsetUtcHandler.Instance);
        SqlMapper.AddTypeHandler(typeof(DateTimeOffset?), NullableDateTimeOffsetUtcHandler.Instance);
    }
}
