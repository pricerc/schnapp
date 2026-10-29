namespace Schnapp.Data;

using System;
using System.Data;

using Dapper;

using FirebirdSql.Data.FirebirdClient;

using Medo;

using Npgsql;

using NpgsqlTypes;

/// <summary>
/// Dapper mapping for <see cref="Uuid7"/> using RFC network byte order (PG uuid, Firebird BINARY(16)).
/// </summary>
/// <remarks>
/// Writes via <see cref="Uuid7.ToByteArray()"/> (big-endian) as <see cref="NpgsqlDbType.Uuid"/> so
/// binary sort order in Postgres matches the textual UUID order. Reads accept Guid or byte[].
/// </remarks>
public sealed class Uuid7TypeHandler : SqlMapper.TypeHandler<Uuid7>
{
    /// <summary>Shared singleton registered with Dapper.</summary>
    public static readonly Uuid7TypeHandler Instance = new();

    /// <summary>Writes a Uuid7 as a PostgreSQL uuid (network/RFC byte order).</summary>
    public override void SetValue(IDbDataParameter parameter, Uuid7 value)
    {
        // Text-preserving Guid mapping: Npgsql sends PG uuid in RFC/network order,
        // matching Uuid7.ToString() / ToByteArray() sort order.
        if (parameter is NpgsqlParameter npgsql)
        {
            npgsql.NpgsqlDbType = NpgsqlDbType.Uuid;
            npgsql.Value = value.ToGuid();
            return;
        }

        if (parameter is FbParameter fb)
        {
            fb.FbDbType = FbDbType.Binary;
            fb.Size = 16;
            fb.Value = value.ToByteArray();
            return;
        }

        parameter.Value = value.ToGuid();
        parameter.DbType = DbType.Guid;
    }

    /// <summary>Parses Guid, string, or 16-byte array values into Uuid7.</summary>
    public override Uuid7 Parse(object value)
    {
        if (value is null || value is DBNull)
            return Uuid7.Empty;

        if (value is Uuid7 u)
            return u;

        if (value is Guid g)
            return Uuid7.FromGuid(g);

        if (value is string s)
            // Postgres uuid text is RFC layout; Id26 is app-layer only.
            return string.IsNullOrEmpty(s) ? Uuid7.Empty : Uuid7.Parse(s);

        if (value is byte[] bytes)
        {
            if (bytes.Length != 16)
                throw new DataException("Uuid7 byte[] must be 16 bytes.");
            return new Uuid7(bytes);
        }

        throw new DataException("Cannot convert " + value.GetType().FullName + " to Uuid7.");
    }
}

/// <summary>
/// Dapper handler for nullable <see cref="Uuid7"/>.
/// </summary>
public sealed class NullableUuid7TypeHandler : SqlMapper.TypeHandler<Uuid7?>
{
    /// <summary>Shared singleton registered with Dapper.</summary>
    public static readonly NullableUuid7TypeHandler Instance = new();

    /// <summary>Writes null as DBNull, otherwise delegates to <see cref="Uuid7TypeHandler"/>.</summary>
    public override void SetValue(IDbDataParameter parameter, Uuid7? value)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            if (parameter is NpgsqlParameter npgsql)
                npgsql.NpgsqlDbType = NpgsqlDbType.Uuid;
            else if (parameter is FbParameter fb)
            {
                fb.FbDbType = FbDbType.Binary;
                fb.Size = 16;
            }
            else
                parameter.DbType = DbType.Guid;
            return;
        }

        Uuid7TypeHandler.Instance.SetValue(parameter, value.Value);
    }

    /// <summary>Parses null/DBNull as null; otherwise same rules as <see cref="Uuid7TypeHandler"/>.</summary>
    public override Uuid7? Parse(object value)
    {
        if (value is null || value is DBNull)
            return null;
        return Uuid7TypeHandler.Instance.Parse(value);
    }
}
