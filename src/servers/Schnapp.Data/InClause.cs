namespace Schnapp.Data;

using Dapper;

using Medo;

/// <summary>
/// Builds <c>(@p0, @p1, …)</c> lists for <see cref="Uuid7"/> values. Dapper's own list expansion
/// skips the Uuid7 type handler, so Npgsql would receive the whole list as one unsupported parameter.
/// </summary>
internal static class InClause
{
    /// <summary>Appends one parameter per id to <paramref name="args"/> and returns the SQL list text.</summary>
    public static string Add(DynamicParameters args, IReadOnlyList<Uuid7> ids, string prefix)
    {
        var names = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            names[i] = "@" + prefix + i;
            args.Add(prefix + i, ids[i]);
        }

        return "(" + string.Join(", ", names) + ")";
    }
}
