namespace Schnapp.Data;

/// <summary>
/// SQL fragments and error mapping for the active <see cref="DatabaseProvider"/>.
/// </summary>
public interface IDatabaseDialect
{
    /// <summary>Engine selected at startup.</summary>
    DatabaseProvider Provider { get; }

    /// <summary>Health ping (<c>SELECT 1</c> vs <c>SELECT 1 FROM RDB$DATABASE</c>).</summary>
    string PingSql { get; }

    /// <summary>
    /// Trailing row-limit clause with a literal count (Firebird cannot parameterize FETCH FIRST).
    /// </summary>
    string Limit(int count);

    /// <summary>
    /// Write expression for an opaque JSON parameter (PostgreSQL <c>jsonb</c> cast; Firebird text/BLOB).
    /// </summary>
    string JsonParam(string parameterName);

    /// <summary>
    /// Identifier that is reserved on Firebird (<c>key</c>, <c>value</c>, <c>role</c>).
    /// PostgreSQL returns the name unquoted.
    /// </summary>
    string Ident(string name);

    /// <summary>Unique constraint / unique index violation.</summary>
    bool IsUniqueViolation(Exception ex);

    /// <summary>Foreign-key violation.</summary>
    bool IsForeignKeyViolation(Exception ex);
}
