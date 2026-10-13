namespace Schnapp.Imaging.Tests;

public class DuplicateScanTests
{
    [Fact]
    public void Keeps_first_marks_identical_follow_up()
    {
        byte[] px = Solid(64, 64, 40);
        FrameFingerprint a = FrameFingerprint.FromPixels(64, 64, px, 64, PixelLayout.Gray8);
        FrameFingerprint b = FrameFingerprint.FromPixels(64, 64, px, 64, PixelLayout.Gray8);
        var items = new[]
        {
            new DuplicateScan.Item("m/desk", DateTimeOffset.UtcNow, a, 0),
            new DuplicateScan.Item("m/desk", DateTimeOffset.UtcNow.AddMinutes(5), b, 1)
        };
        DuplicateScan.SelectDiscardIndexes(items, maxDirtyCells: 2).Should().Equal(1);
    }

    [Fact]
    public void Marks_all_black_frames()
    {
        byte[] px = Solid(64, 64, 0);
        FrameFingerprint fp = FrameFingerprint.FromPixels(64, 64, px, 64, PixelLayout.Gray8);
        var items = new[]
        {
            new DuplicateScan.Item("m/desk", DateTimeOffset.UtcNow, fp, 7)
        };
        DuplicateScan.SelectDiscardIndexes(items, 2).Should().Equal(7);
    }

    [Fact]
    public void Separate_series_are_independent()
    {
        byte[] px = Solid(64, 64, 40);
        FrameFingerprint fp = FrameFingerprint.FromPixels(64, 64, px, 64, PixelLayout.Gray8);
        var items = new[]
        {
            new DuplicateScan.Item("m/a", DateTimeOffset.UtcNow, fp, 0),
            new DuplicateScan.Item("m/b", DateTimeOffset.UtcNow, fp, 1)
        };
        DuplicateScan.SelectDiscardIndexes(items, 2).Should().BeEmpty();
    }

    [Fact]
    public void Null_fingerprint_is_not_marked()
    {
        var items = new[]
        {
            new DuplicateScan.Item("m/desk", DateTimeOffset.UtcNow, null, 0)
        };
        DuplicateScan.SelectDiscardIndexes(items, 2).Should().BeEmpty();
    }

    private static byte[] Solid(int w, int h, byte v)
    {
        byte[] px = new byte[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = v;
        }

        return px;
    }
}
