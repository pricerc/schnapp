namespace Schnapp.Client.Core.Config;

/// <summary>
/// Saved top-left position and size of a client window.
/// </summary>
public sealed class SavedWindowBounds
{
    /// <summary>Pixel X of the window top-left.</summary>
    public int X { get; set; }

    /// <summary>Pixel Y of the window top-left.</summary>
    public int Y { get; set; }

    /// <summary>Logical width (DIPs).</summary>
    public double Width { get; set; }

    /// <summary>Logical height (DIPs).</summary>
    public double Height { get; set; }
}
