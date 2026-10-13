using System.Data;
using FirebirdSql.Data.FirebirdClient;

namespace Schnapp.Data;

/// <summary>Pinned Firebird 5 image and SuperServer defaults.</summary>
public static class FirebirdDefaults
{
    /// <summary>Testcontainers image (Firebird 5 SuperServer; not the v4 default).</summary>
    public const string TestImage = "docker.io/firebirdsql/firebird:5.0.2";

    /// <summary>
    /// Catalog page size. UTF8 <c>VARCHAR(255)</c> unique keys need more than FirebirdClient's
    /// <c>CreateDatabase</c> default of 4096 (max key = page/4).
    /// </summary>
    public const int PageSize = 16384;

    /// <summary>Official image data directory for <c>FIREBIRD_DATABASE</c>.</summary>
    public const string ImageDataDirectory = "/var/lib/firebird/data";

    /// <summary>
    /// Charset/dialect plus an absolute catalog path. Testcontainers passes a bare
    /// <c>schnapp.fdb</c> name; SuperServer opens that relative to its cwd, not the image data dir.
    /// </summary>
    public static string NormalizeConnectionString(string connectionString)
    {
        var b = new FbConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(b.Charset))
            b.Charset = "UTF8";
        b.Dialect = 3;
        if (!string.IsNullOrEmpty(b.Database) && !Path.IsPathRooted(b.Database))
            b.Database = ImageDataDirectory.TrimEnd('/') + "/" + b.Database.TrimStart('/');
        return b.ToString();
    }

    /// <summary>
    /// Waits until SuperServer accepts a connection to the catalog file.
    /// </summary>
    public static void WaitUntilCatalogOpen(string connectionString, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(45);
        var deadline = DateTime.UtcNow + limit;
        Exception? last = null;
        var probe = new FbConnectionStringBuilder(connectionString) { Pooling = false }.ToString();
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var conn = new FbConnection(probe);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM RDB$DATABASE";
                cmd.ExecuteScalar();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(250);
            }
        }

        throw new TimeoutException(
            "Firebird catalog did not become reachable within " + limit + ".", last);
    }
}


/// <summary>
/// <see cref="IDbConnectionFactory"/> for Firebird SuperServer via FirebirdClient.
/// </summary>
/// <remarks>
/// Opens with session time zone UTC so any leftover <c>CURRENT_TIMESTAMP</c> is UTC.
/// Instants are still written as naive UTC <c>TIMESTAMP</c> via Dapper handlers.
/// </remarks>
public sealed class FirebirdConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>Stores the Firebird connection string.</summary>
    /// <exception cref="ArgumentException">When the string is empty.</exception>
    public FirebirdConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection()
    {
        var conn = new FbConnection(_connectionString);
        conn.StateChange += (_, e) =>
        {
            if (e.CurrentState != ConnectionState.Open)
                return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SET TIME ZONE 'UTC'";
            cmd.ExecuteNonQuery();
        };
        return conn;
    }
}
