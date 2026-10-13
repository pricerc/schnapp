namespace Schnapp.Client.Linux.Services;

using Avalonia.Media.Imaging;
using Avalonia.Platform;

using System;
using System.Collections.Generic;

/// <summary>Loads 16x16 tray-menu bitmaps from Assets/menu.</summary>
public static class MenuIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? Get(string name)
    {
        if (Cache.TryGetValue(name, out var cached))
        {
            return cached;
        }


        try
        {
            var assembly = typeof(MenuIcons).Assembly;
            var assemblyName = assembly.GetName().Name ?? "schnapp-client-linux";
            var uri = new Uri($"avares://{assemblyName}/Assets/menu/{name}.png");
            using var stream = AssetLoader.Open(uri);
            cached = new Bitmap(stream);
        }
        catch
        {
            cached = null;
        }

        Cache[name] = cached;
        return cached;
    }

    public static void Apply(Avalonia.Controls.NativeMenuItem? item, string name)
    {
        if (item == null)
        {
            return;
        }


        var bmp = Get(name);
        if (bmp != null)
        {

            item.Icon = bmp;
        }

    }
}
