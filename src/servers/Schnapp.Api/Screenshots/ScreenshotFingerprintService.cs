using System.Text.Json;
using Schnapp.Contracts;
using Schnapp.Data.Blobs;
using Schnapp.Data.Repositories;
using Schnapp.Domain.Entities;
using Schnapp.Imaging;

using Medo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Schnapp.Api.Screenshots;

/// <summary>Compute, persist, and query source-pixel fingerprints for screenshots.</summary>
public static class ScreenshotFingerprintService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static FrameFingerprint? TryToFingerprint(Screenshot shot)
        => TryToFingerprint(Deserialize(shot.Fingerprint), shot.Width, shot.Height);

    public static FrameFingerprint? TryToFingerprint(ScreenshotFingerprint? dto, int? width, int? height)
    {
        if (dto?.CellMeans is null || dto.GridCols < 1 || dto.GridRows < 1)
        {
            return null;
        }

        if (dto.CellMeans.Length != dto.GridCols * dto.GridRows)
        {
            return null;
        }

        return FrameFingerprint.FromStored(
            width ?? 0,
            height ?? 0,
            dto.GridCols,
            dto.GridRows,
            dto.CellMeans,
            dto.MeanLuma,
            dto.MaxLuma,
            ParseHash(dto.AHash),
            ParseHash(dto.DHash),
            ParseHash(dto.PHash));
    }

    public static ScreenshotFingerprint ToDto(FrameFingerprint fp) => new()
    {
        Version = fp.Version,
        GridCols = fp.GridCols,
        GridRows = fp.GridRows,
        CellMeans = fp.CellMeans,
        MeanLuma = fp.MeanLuma,
        MaxLuma = fp.MaxLuma,
        AHash = fp.AHash.ToString("x16"),
        DHash = fp.DHash.ToString("x16"),
        PHash = fp.PHash.ToString("x16")
    };

    public static string ToJson(FrameFingerprint fp) => JsonSerializer.Serialize(ToDto(fp), JsonOptions);

    public static ScreenshotFingerprint? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScreenshotFingerprint>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<FrameFingerprint?> ComputeFromBlobAsync(
        Screenshot shot, IImageBlobStore blobs, CancellationToken ct)
    {
        await using Stream? stream = await blobs.OpenReadAsync(shot.StorageKey, ct);
        if (stream is null)
        {
            return null;
        }

        try
        {
            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, ct);
            int w = image.Width;
            int h = image.Height;
            byte[] buf = new byte[checked(w * h * 4)];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    System.Runtime.InteropServices.MemoryMarshal.Cast<Rgba32, byte>(row)
                        .CopyTo(buf.AsSpan(y * w * 4));
                }
            });
            return FrameFingerprint.FromPixels(w, h, buf, w * 4, PixelLayout.Rgba32);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(int Backfilled, int Failed)> EnsureFingerprintsAsync(
        IReadOnlyList<Screenshot> shots,
        Uuid7 userId,
        ScreenshotRepository repo,
        IImageBlobStore blobs,
        CancellationToken ct)
    {
        int backfilled = 0;
        int failed = 0;
        foreach (Screenshot shot in shots)
        {
            if (!string.IsNullOrWhiteSpace(shot.Fingerprint))
            {
                continue;
            }

            FrameFingerprint? fp = await ComputeFromBlobAsync(shot, blobs, ct);
            if (fp is null)
            {
                failed++;
                continue;
            }

            string json = ToJson(fp);
            await repo.UpdateFingerprintAsync(shot.Id, userId, json, ct);
            shot.Fingerprint = json;
            if (shot.Width is null)
            {
                shot.Width = fp.Width;
            }

            if (shot.Height is null)
            {
                shot.Height = fp.Height;
            }

            backfilled++;
        }

        return (backfilled, failed);
    }

    public static string SeriesKey(Screenshot shot)
        => Uuid7Text.Format(shot.MachineId) + "\n" + (shot.MonitorName ?? shot.MonitorId ?? "");

    private static ulong ParseHash(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return 0;
        }

        return ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong v) ? v : 0;
    }
}
