namespace Schnapp.Data;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Schnapp.Data.Blobs;
using Schnapp.Data.Repositories;

/// <summary>
/// DI registration helpers for the data layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers connection factory, dialect, Dapper configuration, repositories, and blob store.
    /// PostgreSQL when <paramref name="provider"/> is omitted from older call sites.
    /// </summary>
    public static IServiceCollection AddSchnappData(
        this IServiceCollection services,
        string connectionString,
        string blobRoot = "/opt/schnapp/data/blobs")
        => AddSchnappData(services, DatabaseProvider.PostgreSQL, connectionString, blobRoot);

    /// <summary>
    /// Registers connection factory, dialect, Dapper configuration, repositories, and blob store
    /// for the chosen catalog engine.
    /// </summary>
    public static IServiceCollection AddSchnappData(
        this IServiceCollection services,
        DatabaseProvider provider,
        string connectionString,
        string blobRoot = "/opt/schnapp/data/blobs")
    {
        DapperConfig.EnsureConfigured();
        services.AddSingleton<IDatabaseDialect>(_ => provider switch
        {
            DatabaseProvider.Firebird => new FirebirdDialect(),
            _ => new PostgreSqlDialect()
        });
        services.AddSingleton<IDbConnectionFactory>(_ => provider switch
        {
            DatabaseProvider.Firebird => new FirebirdConnectionFactory(connectionString),
            _ => new NpgsqlConnectionFactory(connectionString)
        });
        services.AddSingleton<IImageBlobStore>(sp => new FileSystemImageBlobStore(
            blobRoot, sp.GetRequiredService<ILogger<FileSystemImageBlobStore>>()));
        services.AddScoped<UserRepository>();
        services.AddScoped<ApiTokenRepository>();
        services.AddScoped<MachineRepository>();
        services.AddScoped<ListRepository>();
        services.AddScoped<SettingRepository>();
        services.AddScoped<ScreenshotRepository>();
        services.AddScoped<JournalRepository>();
        return services;
    }
}
