using Medo;

namespace Schnapp.Data.Repositories;

/// <summary>
/// Screenshot plus user/machine labels for blob rewrite (EXIF/XMP backfill).
/// </summary>
public sealed class ScreenshotRewriteRow
{
    public Uuid7 Id { get; set; }
    public Uuid7 UserId { get; set; }
    public Uuid7 MachineId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? ClientTimeZoneId { get; set; }
    public string? MonitorId { get; set; }
    public string? MonitorName { get; set; }
    public string? WindowTitle { get; set; }
    public string? ProgramName { get; set; }
    public string? CommandLine { get; set; }
    public string ContentType { get; set; } = "";
    public long ByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Sha256 { get; set; }
    public string? Fingerprint { get; set; }
    public string StorageKey { get; set; } = "";
    public string ThumbStorageKey { get; set; } = "";
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public Uuid7? JournalEntryId { get; set; }
    public string UserCode { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string UserName { get; set; } = "";
    public string MachineCode { get; set; } = "";
    public string MachineName { get; set; } = "";
}

/// <summary>Screenshot fields needed to run idle-scan against blob storage.</summary>
public sealed class ScreenshotIdleRow
{
    public Uuid7 Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? ClientTimeZoneId { get; set; }
    public string? MonitorName { get; set; }
    public string StorageKey { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long ByteSize { get; set; }
}

/// <summary>Screenshot fields (plus machine label) needed to fingerprint and compare frames.</summary>
public sealed class ScreenshotDuplicateRow
{
    public Uuid7 Id { get; set; }
    public Uuid7 UserId { get; set; }
    public Uuid7 MachineId { get; set; }
    public string? MachineName { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? ClientTimeZoneId { get; set; }
    public string? MonitorId { get; set; }
    public string? MonitorName { get; set; }
    public string StorageKey { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Fingerprint { get; set; }
}

/// <summary>Monitor name histogram for idle-scan / rewrite prompts.</summary>
public sealed class ScreenshotMonitorCount
{
    public string? MonitorName { get; set; }
    public int N { get; set; }
}
