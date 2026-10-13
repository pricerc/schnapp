namespace Schnapp.Client.Windows.Services;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

/// <summary>
/// Loads 16x16 tray-menu bitmaps from <c>Assets/menu/*.png</c> next to the executable.
/// </summary>
public static class MenuIcons
{
    /// <summary>
    /// Case-insensitive cache of loaded images (including null for missing files)
    /// so repeated menu rebuilds do not re-read disk.
    /// </summary>
    private static readonly Dictionary<string, Image?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a cached menu icon, or null if missing.</summary>
    /// <param name="name">File stem under <c>Assets/menu/</c> without extension (e.g. <c>pause</c>).</param>
    /// <returns>Cloned bitmap owned by the cache, or <c>null</c> when not found / unloadable.</returns>
    public static Image? Get(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            Image? img = null;
            try
            {
                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets", "menu", name + ".png");
                if (File.Exists(path))
                {
                    // Because clone so the file handle is not held open.
                    using (var fs = File.OpenRead(path))
                    using (var tmp = Image.FromStream(fs))
                    {
                        img = new Bitmap(tmp);
                    }
                }
            }
            catch
            {
                img = null;
            }

            Cache[name] = img;
            return img;
        }
    }

    /// <summary>Assigns a menu icon when the PNG exists.</summary>
    /// <param name="item">Menu item to decorate; null is ignored.</param>
    /// <param name="name">Icon stem passed to <see cref="Get"/>.</param>
    public static void Apply(ToolStripItem? item, string name)
    {
        if (item == null)
        {
            return;
        }

        var img = Get(name);
        if (img != null)
        {
            item.Image = img;
        }
    }
}
