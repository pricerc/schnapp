namespace Schnapp.Contracts;

/// <summary>
/// Gallery thumbnail tiers (longest edge in px) and where each tier lives in blob storage.
/// </summary>
/// <remarks>
/// The Home thumbnail slider maps one step to one tier, so the browser can show each file at its
/// natural size. Tier files sit under a <c>thumbs/{size}/</c> root that mirrors the original's key.
/// </remarks>
public static class ThumbTiers
{
    /// <summary>Tier sizes, ascending.</summary>
    public static readonly IReadOnlyList<int> Sizes = [128, 256, 384, 512, 640];

    /// <summary>The largest tier.</summary>
    public static int Largest => Sizes[Sizes.Count - 1];

    /// <summary>Smallest tier that is at least <paramref name="requested"/>, or the largest tier.</summary>
    public static int Snap(int requested)
    {
        foreach (var size in Sizes)
        {
            if (size >= requested)
                return size;
        }
        return Largest;
    }

    /// <summary>
    /// Blob key for one tier of an original, e.g. <c>thumbs/256/{user}/{yyyy}/{MM}/{dd}/{id}.jpg</c>.
    /// </summary>
    public static string KeyFor(string storageKey, int size)
    {
        var key = storageKey.Replace('\\', '/');
        var slash = key.LastIndexOf('/');
        var dir = slash < 0 ? "" : key.Substring(0, slash);
        var name = Path.GetFileNameWithoutExtension(key);
        return dir.Length == 0 ? $"thumbs/{size}/{name}.jpg" : $"thumbs/{size}/{dir}/{name}.jpg";
    }
}
