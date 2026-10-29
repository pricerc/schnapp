namespace Schnapp.Data;

using System.Data;

using Npgsql;

/// <summary>
/// Default <see cref="IDbConnectionFactory"/> that opens PostgreSQL connections via Npgsql.
/// </summary>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Stores the connection string used for every <see cref="CreateConnection"/> call.
    /// </summary>
    /// <param name="connectionString">Npgsql connection string (must be non-empty).</param>
    /// <exception cref="ArgumentException">When <paramref name="connectionString"/> is null or whitespace.</exception>
    public NpgsqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
