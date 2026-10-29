namespace Schnapp.Screenshots;

using System.Xml.Linq;

using Schnapp.Contracts;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;

using Medo;

/// <summary>Capture fields written into stored JPEG EXIF (viewers) and XMP (import).</summary>
public sealed class ScreenshotEmbedContext
{
    public required Uuid7 ScreenshotId { get; init; }
    public required Uuid7 UserId { get; init; }
    public required string UserCode { get; init; }
    public required string UserEmail { get; init; }
    public required string UserName { get; init; }
    public required Uuid7 MachineId { get; init; }
    public required string MachineCode { get; init; }
    public required string MachineName { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public string? ClientTimeZoneId { get; init; }
    public string? MonitorId { get; init; }
    public string? MonitorName { get; init; }
    public string? WindowTitle { get; init; }
    public string? ProgramName { get; init; }
    public string? CommandLine { get; init; }
    public string? Notes { get; init; }
    public string? Tags { get; init; }
    public Uuid7? JournalEntryId { get; init; }
    public string? FingerprintJson { get; init; }
}

/// <summary>
/// Embeds Schnapp capture metadata into ImageSharp EXIF + XMP before JPEG encode.
/// </summary>
/// <remarks>
/// EXIF holds the stock-viewer fields (date taken, camera make/model, title).
/// XMP under <see cref="XmpNamespace"/> holds ids and the rest for import.
/// Received-at is omitted so a retry of the same capture encodes identically.
/// </remarks>
public static class ScreenshotFileMetadata
{
    /// <summary>XMP namespace for Schnapp screenshot properties (schema version 1).</summary>
    public const string XmpNamespace = "https://schnapp.price.org.nz/ns/1";

    /// <summary>ASCII marker written into JPEG APP1 XMP (fast skip for rewrite).</summary>
    public const string XmpNamespaceMarker = "schnapp.price.org.nz/ns/1";

    /// <summary>Replaces EXIF/XMP/IPTC on <paramref name="image"/> with Schnapp capture data.</summary>
    public static void Apply(Image image, ScreenshotEmbedContext ctx)
    {
        image.Metadata.IptcProfile = null;
        image.Metadata.ExifProfile = BuildExif(ctx);
        image.Metadata.XmpProfile = new XmpProfile(BuildXmp(ctx));
    }

    /// <summary>
    /// True when <paramref name="fileBytes"/> already contain Schnapp XMP (JPEG or other).
    /// </summary>
    public static bool ContainsSchnappNamespace(ReadOnlySpan<byte> fileBytes)
        => fileBytes.IndexOf("schnapp.price.org.nz/ns/1"u8) >= 0;

    internal static ExifProfile BuildExif(ScreenshotEmbedContext ctx)
    {
        var exif = new ExifProfile();
        var local = ToClientLocal(ctx.TimestampUtc, ctx.ClientTimeZoneId);
        var when = local.ToString("yyyy:MM:dd HH:mm:ss");
        var offset = local.ToString("zzz");

        SetAscii(exif, ExifTag.DateTimeOriginal, when);
        SetAscii(exif, ExifTag.DateTimeDigitized, when);
        SetAscii(exif, ExifTag.DateTime, when);
        SetAscii(exif, ExifTag.OffsetTimeOriginal, offset);
        SetAscii(exif, ExifTag.OffsetTimeDigitized, offset);
        SetAscii(exif, ExifTag.OffsetTime, offset);

        SetAscii(exif, ExifTag.Make, FirstNonEmpty(ctx.MachineName, ctx.MachineCode));
        SetAscii(exif, ExifTag.Model, MonitorLabel(ctx));
        SetAscii(exif, ExifTag.Software, "Schnapp");
        SetAscii(exif, ExifTag.Artist, FirstNonEmpty(ctx.UserEmail, ctx.UserCode, ctx.UserName));
        SetAscii(exif, ExifTag.ImageDescription, ctx.WindowTitle);
        SetAscii(exif, ExifTag.ImageUniqueID, ctx.ScreenshotId.ToString());

        var comment = string.Join(" / ", new[]
        {
            FirstNonEmpty(ctx.MachineName, ctx.MachineCode),
            MonitorLabel(ctx),
            ctx.ProgramName
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        // ASCII UserComment: libexif/Ristretto reject ImageSharp's default Unicode payload
        // ("unsupported unicode string"). Make/Model/Software already use ASCII tags.
        if (!string.IsNullOrWhiteSpace(comment))
        {
            exif.SetValue(
                ExifTag.UserComment,
                new EncodedString(EncodedString.CharacterCode.ASCII, comment));
        }

        SetAscii(exif, ExifTag.XPTitle, ctx.WindowTitle);
        SetAscii(exif, ExifTag.XPComment, comment);
        SetAscii(exif, ExifTag.XPAuthor, FirstNonEmpty(ctx.UserEmail, ctx.UserCode, ctx.UserName));

        return exif;
    }

    internal static XDocument BuildXmp(ScreenshotEmbedContext ctx)
    {
        XNamespace adobe = "adobe:ns:meta/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace schnapp = XmpNamespace;
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace xmp = "http://ns.adobe.com/xap/1.0/";
        XNamespace xmpMM = "http://ns.adobe.com/xap/1.0/mm/";

        var local = ToClientLocal(ctx.TimestampUtc, ctx.ClientTimeZoneId);
        var desc = new XElement(rdf + "Description",
            new XAttribute(XNamespace.Xmlns + "schnapp", schnapp),
            new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XAttribute(XNamespace.Xmlns + "xmp", xmp),
            new XAttribute(XNamespace.Xmlns + "xmpMM", xmpMM),
            new XAttribute(rdf + "about", ""),
            new XElement(schnapp + "schemaVersion", "1"),
            new XElement(schnapp + "id", ctx.ScreenshotId.ToString()),
            new XElement(schnapp + "userId", ctx.UserId.ToString()),
            new XElement(schnapp + "userCode", ctx.UserCode),
            new XElement(schnapp + "userEmail", ctx.UserEmail),
            new XElement(schnapp + "machineId", ctx.MachineId.ToString()),
            new XElement(schnapp + "machineCode", ctx.MachineCode),
            new XElement(schnapp + "machineName", ctx.MachineName),
            new XElement(xmp + "CreateDate", local.ToString("yyyy-MM-ddTHH:mm:sszzz")),
            new XElement(xmpMM + "DocumentID", "uuid:" + ctx.ScreenshotId.ToString()));

        AddIf(desc, schnapp + "clientTimeZoneId", ctx.ClientTimeZoneId);
        AddIf(desc, schnapp + "monitorId", ctx.MonitorId);
        AddIf(desc, schnapp + "monitorName", ctx.MonitorName);
        AddIf(desc, schnapp + "windowTitle", ctx.WindowTitle);
        AddIf(desc, schnapp + "programName", ctx.ProgramName);
        AddIf(desc, schnapp + "commandLine", ctx.CommandLine);
        AddIf(desc, schnapp + "notes", ctx.Notes);
        AddIf(desc, schnapp + "tags", ctx.Tags);
        AddIf(desc, dc + "title", ctx.WindowTitle);
        AddIf(desc, dc + "description", ctx.Notes);
        if (ctx.JournalEntryId is { } jid)
            desc.Add(new XElement(schnapp + "journalEntryId", jid.ToString()));
        AddIf(desc, schnapp + "fingerprint", ctx.FingerprintJson);

        var tagTokens = TagText.Split(ctx.Tags);
        if (tagTokens.Count > 0)
        {
            desc.Add(new XElement(dc + "subject",
                new XElement(rdf + "Bag",
                    tagTokens.Select(t => new XElement(rdf + "li", t)))));
        }

        return new XDocument(
            new XElement(adobe + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", adobe),
                new XElement(rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", rdf),
                    desc)));
    }

    private static void AddIf(XElement parent, XName name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parent.Add(new XElement(name, value));
    }

    private static void SetAscii(ExifProfile exif, ExifTag<string> tag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            exif.SetValue(tag, value);
    }

    internal static string MonitorLabel(ScreenshotEmbedContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.MonitorName) && !string.IsNullOrWhiteSpace(ctx.MonitorId))
            return ctx.MonitorName + " (" + ctx.MonitorId + ")";
        return FirstNonEmpty(ctx.MonitorName, ctx.MonitorId) ?? "";
    }

    internal static DateTimeOffset ToClientLocal(DateTimeOffset utc, string? timeZoneId)
    {
        var instant = utc.ToUniversalTime();
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return instant;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTime(instant, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            return instant;
        }
        catch (InvalidTimeZoneException)
        {
            return instant;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }
}
