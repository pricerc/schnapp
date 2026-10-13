using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Schnapp.Api.Screenshots;

/// <summary>
/// Generates gallery thumbnails with ImageSharp (JPEG, quality ~75).
/// </summary>
/// <remarks>
/// Registered as a singleton in <c>Program.cs</c>. Max edge length is passed per call
/// so it can come from org setting <c>screenshot.thumb_max_edge</c> without scoping issues.
/// </remarks>
public sealed class ThumbnailService
{
    /// <summary>
    /// Resizes the image stream to a JPEG thumbnail.
    /// </summary>
    /// <param name="imageStream">Source image bytes (rewound by caller if needed).</param>
    /// <param name="maxEdge">
    /// Maximum width or height in pixels (from setting <c>screenshot.thumb_max_edge</c>, default 640).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JPEG thumbnail bytes and the original decoded width/height.</returns>
    /// <remarks>
    /// Called from screenshot upload after magic-byte validation. Failures (undecodable images)
    /// propagate to the endpoint as a 400.
    /// </remarks>
    public async Task<(byte[] ThumbJpeg, int Width, int Height)> CreateThumbAsync(
        Stream imageStream, int maxEdge, CancellationToken ct = default)
    {
        if (maxEdge < 16)
            maxEdge = 16;

        using var image = await Image.LoadAsync(imageStream, ct);
        var width = image.Width;
        var height = image.Height;
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxEdge, maxEdge)
        }));
        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder
        {
            Quality = 75,
            ColorType = JpegColorType.YCbCrRatio420
        }, ct);
        return (ms.ToArray(), width, height);
    }
}
