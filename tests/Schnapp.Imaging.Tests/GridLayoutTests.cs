namespace Schnapp.Imaging.Tests;

public class GridLayoutTests
{
    [Fact]
    public void Hd_stays_16_by_8()
    {
        GridLayout.ColsFor(1920).Should().Be(16);
        GridLayout.RowsFor(1080).Should().Be(8);
        GridLayout.SliderMax(16, 8).Should().Be(10);
    }

    [Fact]
    public void Four_screen_ultrawide_uses_finer_grid()
    {
        int cols = GridLayout.ColsFor(5880);
        int rows = GridLayout.RowsFor(2160);
        cols.Should().Be(25);
        rows.Should().Be(16);
        (5880.0 / cols).Should().BeLessThanOrEqualTo(GridLayout.MaxCellWidth);
        (2160.0 / rows).Should().BeLessThanOrEqualTo(GridLayout.MaxCellHeight);
        GridLayout.SliderMax(cols, rows).Should().Be(20);
    }

    [Fact]
    public void Clamp_respects_slider_max()
    {
        GridLayout.ClampMaxDirtyCells(99, 16, 8).Should().Be(10);
        GridLayout.ClampMaxDirtyCells(99, 25, 16).Should().Be(20);
        GridLayout.ClampMaxDirtyCells(0, 16, 8).Should().Be(1);
    }
}
