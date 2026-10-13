namespace Schnapp.Imaging;

/// <summary>Packed pixel order of a tightly or stride-aligned buffer.</summary>
public enum PixelLayout
{
    /// <summary>One byte per pixel, 0 = black, 255 = white.</summary>
    Gray8 = 0,

    /// <summary>Blue, green, red, alpha (GDI+ <c>Format32bppArgb</c> in memory).</summary>
    Bgra32 = 1,

    /// <summary>Red, green, blue, alpha (ImageSharp <c>Rgba32</c>).</summary>
    Rgba32 = 2
}
