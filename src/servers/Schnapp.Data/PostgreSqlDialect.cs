using Npgsql;

namespace Schnapp.Data;

/// <summary>PostgreSQL fragments and Npgsql SQLSTATE mapping.</summary>
public sealed class PostgreSqlDialect : IDatabaseDialect
{
    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.PostgreSQL;

    /// <inheritdoc />
    public string PingSql => "SELECT 1";

    /// <inheritdoc />
    public string Limit(int count) => "LIMIT " + Math.Max(0, count);

    /// <inheritdoc />
    public string JsonParam(string parameterName) => "CAST(@" + parameterName + " AS jsonb)";

    /// <inheritdoc />
    public string Ident(string name) => name;

    /// <inheritdoc />
    public bool IsUniqueViolation(Exception ex) => HasSqlState(ex, "23505");

    /// <inheritdoc />
    public bool IsForeignKeyViolation(Exception ex) => HasSqlState(ex, "23503");

    private static bool HasSqlState(Exception ex, string state)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg && pg.SqlState == state)
                return true;
        }

        return false;
    }
}
