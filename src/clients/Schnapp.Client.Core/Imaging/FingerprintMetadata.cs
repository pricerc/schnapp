namespace Schnapp.Client.Core.Imaging;

using Schnapp.Contracts;
using Schnapp.Imaging;

/// <summary>Maps <see cref="FrameFingerprint"/> onto upload metadata.</summary>
public static class FingerprintMetadata
{
    public static ScreenshotFingerprint ToDto(FrameFingerprint fp)
    {
        return new ScreenshotFingerprint
        {
            Version = fp.Version,
            GridCols = fp.GridCols,
            GridRows = fp.GridRows,
            CellMeans = fp.CellMeans,
            MeanLuma = fp.MeanLuma,
            MaxLuma = fp.MaxLuma,
            AHash = FormatHash(fp.AHash),
            DHash = FormatHash(fp.DHash),
            PHash = FormatHash(fp.PHash)
        };
    }

    private static string FormatHash(ulong value) => value.ToString("x16");
}
