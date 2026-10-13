namespace Schnapp.Utils;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

/// <summary>
/// Decodes each original once and stores a box-downsampled grayscale thumbnail.
/// Detectors all run against this cache so JPEG/WebP artifacts are at least consistent.
/// </summary>
internal static class ThumbCache
{
    public static List<FrameRecord> Build(IReadOnlyList<SpoolMeta> items, int thumbWidth, bool detailedProgress = false)
    {
        FrameRecord?[] slots = new FrameRecord?[items.Count];
        object gate = new();
        ProgressReporter progress = new("Decoded", items.Count, detailedProgress, step: 50);

        Parallel.For(0, items.Count, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, i =>
        {
            SpoolMeta item = items[i];
            bool ok = true;
            try
            {
                slots[i] = DecodeOne(item, thumbWidth);
            }
            catch (Exception ex)
            {
                ok = false;
                lock (gate)
                {
                    Console.Error.WriteLine("Decode failed " + item.Id + ": " + ex.Message);
                }
            }

            progress.Tick(failed: !ok);
        });

        List<FrameRecord> frames = new(items.Count);
        foreach (FrameRecord? slot in slots)
        {
            if (slot is not null)
            {
                frames.Add(slot);
            }
        }

        return frames;
    }

    private static FrameRecord DecodeOne(SpoolMeta item, int thumbWidth)
    {
        DecoderOptions decoder = new()
        {
            TargetSize = new Size(Math.Max(32, thumbWidth * 2), Math.Max(32, thumbWidth * 2))
        };

        using Image<Rgba32> image = Image.Load<Rgba32>(decoder, item.ImagePath);
        int srcW = image.Width;
        int srcH = image.Height;
        int tw = Math.Max(16, thumbWidth);
        int th = Math.Max(8, (int)Math.Round(srcH * (tw / (double)srcW)));

        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(tw, th),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Box
            });
            ctx.Grayscale();
        });

        byte[] luma = new byte[image.Width * image.Height];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int offset = y * accessor.Width;
                for (int x = 0; x < row.Length; x++)
                {
                    luma[offset + x] = row[x].R;
                }
            }
        });

        return new FrameRecord
        {
            Id = item.Id,
            Directory = item.Directory,
            TimestampUtc = item.TimestampUtc,
            TimestampLocal = item.TimestampLocal,
            TimeZoneId = item.TimeZoneId,
            SourceWidth = item.Width > 0 ? item.Width : srcW,
            SourceHeight = item.Height > 0 ? item.Height : srcH,
            ByteSize = item.ByteSize,
            Luma = luma,
            ThumbWidth = image.Width,
            ThumbHeight = image.Height,
            WebpKind = item.WebpKind
        };
    }
}
