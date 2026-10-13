namespace Schnapp.Imaging.Tests;

public class FrameFingerprintTests
{
    [Fact]
    public void All_black_is_black()
    {
        byte[] px = SolidGray(1920, 1080, 0);
        FrameFingerprint fp = FrameFingerprint.FromPixels(1920, 1080, px, 1920, PixelLayout.Gray8);
        fp.IsBlack.Should().BeTrue();
        fp.MaxLuma.Should().Be(0);
        fp.GridCols.Should().Be(16);
        fp.GridRows.Should().Be(8);
    }

    [Fact]
    public void Identical_frames_have_zero_dirty_cells()
    {
        byte[] px = SolidGray(1920, 1080, 40);
        FrameFingerprint a = FrameFingerprint.FromPixels(1920, 1080, px, 1920, PixelLayout.Gray8);
        FrameFingerprint b = FrameFingerprint.FromPixels(1920, 1080, px, 1920, PixelLayout.Gray8);
        a.Compare(b).DirtyCells.Should().Be(0);
        a.IsIdleDuplicateOf(b, maxDirtyCells: 2).Should().BeTrue();
    }

    [Fact]
    public void Clock_sized_corner_is_at_most_two_cells()
    {
        byte[] a = SolidGray(1920, 1080, 40);
        byte[] b = (byte[])a.Clone();
        FillRect(b, 1920, 1080, 1920 - 100, 1080 - 50, 100, 50, 200);
        FrameFingerprint fa = FrameFingerprint.FromPixels(1920, 1080, a, 1920, PixelLayout.Gray8);
        FrameFingerprint fb = FrameFingerprint.FromPixels(1920, 1080, b, 1920, PixelLayout.Gray8);
        fa.Compare(fb).DirtyCells.Should().BeLessThanOrEqualTo(2);
        fa.IsIdleDuplicateOf(fb, 2).Should().BeTrue();
    }

    [Fact]
    public void Explorer_scroll_block_dirties_more_than_two_cells()
    {
        byte[] a = SolidGray(1920, 1080, 40);
        byte[] b = (byte[])a.Clone();
        // ~400x400 listing pane — several 120×135 cells.
        FillRect(b, 1920, 1080, 80, 80, 400, 400, 180);
        FrameFingerprint fa = FrameFingerprint.FromPixels(1920, 1080, a, 1920, PixelLayout.Gray8);
        FrameFingerprint fb = FrameFingerprint.FromPixels(1920, 1080, b, 1920, PixelLayout.Gray8);
        fa.Compare(fb).DirtyCells.Should().BeGreaterThan(2);
        fa.IsIdleDuplicateOf(fb, 2).Should().BeFalse();
    }

    [Fact]
    public void Hashes_match_for_identical_frames()
    {
        byte[] px = SolidGray(640, 480, 90);
        FrameFingerprint a = FrameFingerprint.FromPixels(640, 480, px, 640, PixelLayout.Gray8);
        FrameFingerprint b = FrameFingerprint.FromPixels(640, 480, px, 640, PixelLayout.Gray8);
        FingerprintDelta d = a.Compare(b);
        d.AHashHamming.Should().Be(0);
        d.DHashHamming.Should().Be(0);
        d.PHashHamming.Should().Be(0);
    }

    [Fact]
    public void Rgba_and_bgra_agree_on_luma()
    {
        const int w = 64;
        const int h = 64;
        byte[] rgba = new byte[w * h * 4];
        byte[] bgra = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            rgba[i * 4] = 10;
            rgba[i * 4 + 1] = 20;
            rgba[i * 4 + 2] = 30;
            rgba[i * 4 + 3] = 255;
            bgra[i * 4] = 30;
            bgra[i * 4 + 1] = 20;
            bgra[i * 4 + 2] = 10;
            bgra[i * 4 + 3] = 255;
        }

        FrameFingerprint a = FrameFingerprint.FromPixels(w, h, rgba, w * 4, PixelLayout.Rgba32);
        FrameFingerprint b = FrameFingerprint.FromPixels(w, h, bgra, w * 4, PixelLayout.Bgra32);
        a.Compare(b).DirtyCells.Should().Be(0);
        a.MeanLuma.Should().BeApproximately(b.MeanLuma, 0.01);
    }

    private static byte[] SolidGray(int w, int h, byte value)
    {
        byte[] px = new byte[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = value;
        }

        return px;
    }

    private static void FillRect(byte[] px, int w, int h, int x0, int y0, int rw, int rh, byte value)
    {
        int x1 = Math.Min(w, x0 + rw);
        int y1 = Math.Min(h, y0 + rh);
        for (int y = Math.Max(0, y0); y < y1; y++)
        {
            int row = y * w;
            for (int x = Math.Max(0, x0); x < x1; x++)
            {
                px[row + x] = value;
            }
        }
    }
}
