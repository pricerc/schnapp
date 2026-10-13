using FirebirdSql.Data.FirebirdClient;

namespace Schnapp.Data;

/// <summary>Firebird 5 fragments and FbException mapping.</summary>
public sealed class FirebirdDialect : IDatabaseDialect
{
    // isc_unique_key_violation / isc_no_dup / isc_foreign_key
    private const int UniqueKeyViolation = 335544665;
    private const int NoDup = 335544349;
    private const int ForeignKey = 335544466;

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.Firebird;

    /// <inheritdoc />
    public string PingSql => "SELECT 1 FROM RDB$DATABASE";

    /// <inheritdoc />
    public string Limit(int count) => "FETCH FIRST " + Math.Max(0, count) + " ROWS ONLY";

    /// <inheritdoc />
    public string JsonParam(string parameterName) => "@" + parameterName;

    /// <inheritdoc />
    public string Ident(string name) => "\"" + name + "\"";

    /// <inheritdoc />
    public bool IsUniqueViolation(Exception ex) => HasError(ex, UniqueKeyViolation, NoDup);

    /// <inheritdoc />
    public bool IsForeignKeyViolation(Exception ex) => HasError(ex, ForeignKey);

    private static bool HasError(Exception ex, params int[] codes)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is not FbException fb)
                continue;
            if (codes.Contains(fb.ErrorCode))
                return true;
            foreach (FbError err in fb.Errors)
            {
                if (codes.Contains(err.Number))
                    return true;
            }
        }

        return false;
    }
}
