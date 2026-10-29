namespace Schnapp.Data;

using System.Data;

/// <summary>
/// Abstraction for creating ADO.NET connections (Npgsql or FirebirdClient).
/// </summary>
/// <remarks>
/// Keeps repositories free of hard-coded Npgsql types and simplifies testing.
/// </remarks>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new closed connection; caller opens, uses, and disposes it.
    /// </summary>
    /// <returns>A provider-specific <see cref="IDbConnection"/>.</returns>
    IDbConnection CreateConnection();
}
