namespace Schnapp.Screenshots;

using Schnapp.Contracts;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

/// <summary>One encoded thumbnail tier (<paramref name="Size"/> is the tier's longest edge).</summary>
public readonly record struct ScreenshotThumb(int Size, byte[] Jpeg);

/// <summary>Decoded screenshot written as a metadata-bearing JPEG original plus gallery thumb tiers.</summary>
public readonly record struct ScreenshotEncodedOriginal(
    byte[] Jpeg, IReadOnlyList<ScreenshotThumb> Thumbs, int Width, int Height);

/// <summary>
/// One ImageSharp decode: EXIF/XMP JPEG original, then resized JPEG thumbs for every <see cref="ThumbTiers"/> size.
/// </summary>
public sealed class ScreenshotImageProcessor
{
    /// <summary>
    /// Decodes <paramref name="source"/>, embeds <paramref name="ctx"/>, and writes JPEG bytes.
    /// </summary>
    /// <exception cref="UnknownImageFormatException">When the stream is not a supported image.</exception>
    public async Task<ScreenshotEncodedOriginal> EncodeAsync(
        Stream source,
        ScreenshotEmbedContext ctx,
        int jpegQuality,
        CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(source, ct);
        var width = image.Width;
        var height = image.Height;
        ScreenshotFileMetadata.Apply(image, ctx);

        jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        await using var orig = new MemoryStream();
        await image.SaveAsJpegAsync(orig, new JpegEncoder
        {
            Quality = jpegQuality,
            ColorType = JpegColorType.YCbCrRatio444
        }, ct);

        var thumbs = await EncodeThumbsAsync(image, ThumbTiers.Sizes, ct);
        return new ScreenshotEncodedOriginal(orig.ToArray(), thumbs, width, height);
    }

    /// <summary>Decodes an existing image and encodes the requested thumb tiers (metadata stripped).</summary>
    public async Task<IReadOnlyList<ScreenshotThumb>> CreateThumbsAsync(
        Stream source, IEnumerable<int> sizes, CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(source, ct);
        return await EncodeThumbsAsync(image, sizes, ct);
    }

    // Mutates <paramref name="image"/>: resizes largest tier first, then each smaller tier from the previous one.
    private static async Task<IReadOnlyList<ScreenshotThumb>> EncodeThumbsAsync(
        Image image, IEnumerable<int> sizes, CancellationToken ct)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;

        var result = new List<ScreenshotThumb>();
        foreach (var size in sizes.Distinct().OrderByDescending(static s => s))
        {
            // Never upscale: a small source is stored at native size in every tier.
            if (Math.Max(image.Width, image.Height) > size)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(size, size)
                }));
            }

            await using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder
            {
                Quality = 75,
                ColorType = JpegColorType.YCbCrRatio420
            }, ct);
            result.Add(new ScreenshotThumb(size, ms.ToArray()));
        }

        return result;
    }
}
