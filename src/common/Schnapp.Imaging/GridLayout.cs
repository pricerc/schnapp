namespace Schnapp.Imaging;

/// <summary>
/// Source-pixel grid used for idle-duplicate detection.
/// Cells are never larger than <see cref="MaxCellWidth"/>x<see cref="MaxCellHeight"/>,
/// and never coarser than 16x8 (1080p stays 16x8).
/// </summary>
public static class GridLayout
{
    public const int Version = 1;
    public const int MaxCellWidth = 240;
    public const int MaxCellHeight = 135;
    public const int MinCols = 16;
    public const int MinRows = 8;

    /// <summary>Mean-luma delta that marks a cell dirty when comparing two fingerprints.</summary>
    public const int CellMeanDelta = 8;

    /// <summary>Default client slider value (clock + one extra chrome cell).</summary>
    public const int DefaultMaxDirtyCells = 2;

    public static int ColsFor(int width)
    {
        if (width < 1)
        {
            width = 1;
        }

        int cols = (width + MaxCellWidth - 1) / MaxCellWidth;
        return cols < MinCols ? MinCols : cols;
    }

    public static int RowsFor(int height)
    {
        if (height < 1)
        {
            height = 1;
        }

        int rows = (height + MaxCellHeight - 1) / MaxCellHeight;
        return rows < MinRows ? MinRows : rows;
    }

    /// <summary>
    /// Slider upper bound: 10 on 16x8 (1080p-class), 20 when the grid is larger than 128 cells.
    /// </summary>
    public static int SliderMax(int cols, int rows)
    {
        int cells = cols * rows;
        return cells > MinCols * MinRows ? 20 : 10;
    }

    public static int ClampMaxDirtyCells(int requested, int cols, int rows)
    {
        int max = SliderMax(cols, rows);
        if (requested < 1)
        {
            return 1;
        }

        return requested > max ? max : requested;
    }
}
