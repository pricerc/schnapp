using System.Data;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Npgsql;
using NpgsqlTypes;

namespace Schnapp.Data;

/// <summary>
/// Dapper mapping for instants stored as UTC at rest.
/// </summary>
/// <remarks>
/// PostgreSQL <c>timestamptz</c> still uses Npgsql's <see cref="DateTimeOffset"/> mapping.
/// Firebird <c>TIMESTAMP</c> has no zone: writes <c>UtcDateTime</c>, reads as UTC.
/// IANA zones live on <c>app_user.time_zone_id</c> / <c>screenshot.client_time_zone_id</c>, not on these columns.
/// </remarks>
public sealed class DateTimeOffsetUtcHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    /// <summary>Shared singleton registered with Dapper.</summary>
    public static readonly DateTimeOffsetUtcHandler Instance = new();

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        if (parameter is FbParameter fb)
        {
            fb.FbDbType = FbDbType.TimeStamp;
            fb.Value = DateTime.SpecifyKind(utc.UtcDateTime, DateTimeKind.Unspecified);
            return;
        }

        if (parameter is NpgsqlParameter npgsql)
        {
            npgsql.NpgsqlDbType = NpgsqlDbType.TimestampTz;
            npgsql.Value = utc;
            return;
        }

        parameter.Value = utc;
    }

    /// <inheritdoc />
    public override DateTimeOffset Parse(object value)
    {
        if (value is null || value is DBNull)
            return default;

        if (value is DateTimeOffset dto)
            return dto.ToUniversalTime();

        if (value is DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Local)
                dt = dt.ToUniversalTime();
            else if (dt.Kind == DateTimeKind.Unspecified)
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        throw new DataException("Cannot convert " + value.GetType().FullName + " to DateTimeOffset.");
    }
}

/// <summary>Nullable counterpart of <see cref="DateTimeOffsetUtcHandler"/>.</summary>
public sealed class NullableDateTimeOffsetUtcHandler : SqlMapper.TypeHandler<DateTimeOffset?>
{
    /// <summary>Shared singleton registered with Dapper.</summary>
    public static readonly NullableDateTimeOffsetUtcHandler Instance = new();

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            if (parameter is FbParameter fb)
                fb.FbDbType = FbDbType.TimeStamp;
            else
                parameter.DbType = DbType.DateTimeOffset;
            return;
        }

        DateTimeOffsetUtcHandler.Instance.SetValue(parameter, value.Value);
    }

    /// <inheritdoc />
    public override DateTimeOffset? Parse(object value)
    {
        if (value is null || value is DBNull)
            return null;
        return DateTimeOffsetUtcHandler.Instance.Parse(value);
    }
}
