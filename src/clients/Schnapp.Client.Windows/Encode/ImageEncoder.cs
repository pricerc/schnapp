namespace Schnapp.Client.Windows.Encode;

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

using Imazen.WebP;

using Schnapp.Client.Core.Config;

/// <summary>
/// GDI+ encode helpers: scale, max-edge clamp, JPEG/PNG via System.Drawing, WebP via Imazen.WebP.
/// </summary>
/// <remarks>
/// Called by <c>CaptureService</c> off the UI thread. WebP uses the <c>Imazen.WebP</c> NuGet package
/// (<see cref="SimpleEncoder"/> / <see cref="SimpleDecoder"/>) plus the win-x64 native runtime package.
/// </remarks>
public static class ImageEncoder
{
    private static readonly SimpleEncoder WebpEncoder = new();

    /// <summary>
    /// Returns content-type and file extension for a format.
    /// </summary>
    public static (string ContentType, string Extension, CaptureImageFormat Effective) ResolveFormat(CaptureImageFormat format)
    {
        return format switch
        {
            CaptureImageFormat.Png => ("image/png", "png", CaptureImageFormat.Png),
            CaptureImageFormat.Webp => ("image/webp", "webp", CaptureImageFormat.Webp),
            CaptureImageFormat.WebpLossless => ("image/webp", "webp", CaptureImageFormat.WebpLossless),
            _ => ("image/jpeg", "jpg", CaptureImageFormat.Jpeg),
        };
    }

    /// <summary>
    /// Downscales by multiply factor (e.g. 0.5 halves both axes). Returns a new bitmap when resized.
    /// </summary>
    public static Bitmap ApplyScale(Bitmap source, double scale)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (scale <= 0 || Math.Abs(scale - 1.0) < 0.001)
        {
            return source;
        }

        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        if (w == source.Width && h == source.Height)
        {
            return source;
        }

        return Resize(source, w, h, disposeSource: true);
    }

    /// <summary>
    /// Downscales so neither edge exceeds <paramref name="maxEdge"/>. May replace the bitmap.
    /// </summary>
    public static Bitmap ClampMaxEdge(Bitmap source, int maxEdge)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (maxEdge < 1 || (source.Width <= maxEdge && source.Height <= maxEdge))
        {
            return source;
        }

        var scale = Math.Min((double)maxEdge / source.Width, (double)maxEdge / source.Height);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        return Resize(source, w, h, disposeSource: true);
    }

    /// <summary>
    /// Encodes <paramref name="bitmap"/> to a byte array (JPEG, PNG, or WebP).
    /// </summary>
    public static byte[] Encode(Bitmap bitmap, CaptureImageFormat format, int quality)
    {
        if (bitmap == null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var resolved = ResolveFormat(format);
        using (var ms = new MemoryStream())
        {
            switch (resolved.Effective)
            {
                case CaptureImageFormat.Png:
                    bitmap.Save(ms, ImageFormat.Png);
                    break;
                case CaptureImageFormat.Webp:
                    // Imazen quality is 0-100 float; clamp for consistency with JPEG settings.
                    WebpEncoder.Encode(bitmap, ms, ClampQuality(quality));
                    break;
                case CaptureImageFormat.WebpLossless:
                    // Imazen.WebP: quality &lt; 0 selects lossless.
                    WebpEncoder.Encode(bitmap, ms, -1f);
                    break;
                default:
                    SaveJpeg(bitmap, ms, quality);
                    break;
            }

            return ms.ToArray();
        }
    }

    /// <summary>
    /// Decodes image bytes (JPEG/PNG via GDI+, WebP via Imazen.WebP) to a new <see cref="Bitmap"/>.
    /// </summary>
    public static Bitmap DecodeToBitmap(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            throw new ArgumentException("Image bytes are empty.", nameof(bytes));
        }

        if (IsWebp(bytes))
        {
            var decoder = new SimpleDecoder();
            // SimpleDecoder returns a Bitmap the caller owns.
            return decoder.DecodeFromBytes(bytes, bytes.LongLength);
        }

        using (var ms = new MemoryStream(bytes))
        using (var tmp = Image.FromStream(ms))
        {
            return new Bitmap(tmp);
        }
    }

    /// <summary>
    /// RIFF....WEBP magic-byte check (no full parse).
    /// </summary>
    public static bool IsWebp(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12)
        {
            return false;
        }

        // "RIFF" .... "WEBP"
        return bytes[0] == (byte)'R'
               && bytes[1] == (byte)'I'
               && bytes[2] == (byte)'F'
               && bytes[3] == (byte)'F'
               && bytes[8] == (byte)'W'
               && bytes[9] == (byte)'E'
               && bytes[10] == (byte)'B'
               && bytes[11] == (byte)'P';
    }

    private static float ClampQuality(int quality)
    {
        if (quality < 1)
        {
            return 1f;
        }

        if (quality > 100)
        {
            return 100f;
        }

        return quality;
    }

    private static Bitmap Resize(Bitmap source, int width, int height, bool disposeSource)
    {
        var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dest))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, width, height);
        }

        if (disposeSource)
        {
            source.Dispose();
        }

        return dest;
    }

    private static void SaveJpeg(Bitmap bitmap, Stream stream, int quality)
    {
        quality = quality < 1 ? 1 : (quality > 100 ? 100 : quality);
        var codec = GetEncoder(ImageFormat.Jpeg);
        if (codec == null)
        {
            bitmap.Save(stream, ImageFormat.Jpeg);
            return;
        }

        using (var ep = new EncoderParameters(1))
        {
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
            bitmap.Save(stream, codec, ep);
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
        {
            if (c.FormatID == format.Guid)
            {
                return c;
            }
        }

        return null;
    }
}
