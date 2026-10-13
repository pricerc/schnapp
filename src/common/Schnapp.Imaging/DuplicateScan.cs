namespace Schnapp.Imaging;

/// <summary>
/// Picks frames that the capture client would discard: all-black, or idle vs the last
/// <em>kept</em> frame in the same machine/monitor series.
/// </summary>
public static class DuplicateScan
{
    /// <summary>One screenshot in time order (caller may pass an unsorted list).</summary>
    public readonly struct Item
    {
        public Item(string seriesKey, DateTimeOffset timestamp, FrameFingerprint? fingerprint, int index)
        {
            SeriesKey = seriesKey ?? "";
            Timestamp = timestamp;
            Fingerprint = fingerprint;
            Index = index;
        }

        public string SeriesKey { get; }
        public DateTimeOffset Timestamp { get; }
        public FrameFingerprint? Fingerprint { get; }
        public int Index { get; }
    }

    /// <summary>Why a frame was marked, and (for idle) which kept frame it matched.</summary>
    public readonly struct Verdict
    {
        public Verdict(int index, bool isBlack, int anchorIndex, int dirtyCells, int totalCells)
        {
            Index = index;
            IsBlack = isBlack;
            AnchorIndex = anchorIndex;
            DirtyCells = dirtyCells;
            TotalCells = totalCells;
        }

        /// <summary>The marked <see cref="Item.Index"/>.</summary>
        public int Index { get; }

        public bool IsBlack { get; }

        /// <summary><see cref="Item.Index"/> of the last kept frame it duplicates; -1 when black.</summary>
        public int AnchorIndex { get; }

        public int DirtyCells { get; }
        public int TotalCells { get; }
    }

    /// <summary>
    /// Returns <see cref="Item.Index"/> values that are blank or idle-duplicates.
    /// Items with a null fingerprint are left unmarked.
    /// </summary>
    public static List<int> SelectDiscardIndexes(IReadOnlyList<Item> items, int maxDirtyCells)
    {
        List<int> marked = new List<int>();
        foreach (Verdict v in Analyze(items, maxDirtyCells))
        {
            marked.Add(v.Index);
        }

        return marked;
    }

    /// <summary>Like <see cref="SelectDiscardIndexes"/> but reports reason and anchor per marked frame.</summary>
    public static List<Verdict> Analyze(IReadOnlyList<Item> items, int maxDirtyCells)
    {
        List<Verdict> marked = new List<Verdict>();
        if (items is null || items.Count == 0)
        {
            return marked;
        }

        List<Item> ordered = new List<Item>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            ordered.Add(items[i]);
        }

        ordered.Sort(static (a, b) =>
        {
            int c = string.CompareOrdinal(a.SeriesKey, b.SeriesKey);
            if (c != 0)
            {
                return c;
            }

            c = a.Timestamp.CompareTo(b.Timestamp);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        string? series = null;
        FrameFingerprint? kept = null;
        int keptIndex = -1;
        for (int i = 0; i < ordered.Count; i++)
        {
            Item item = ordered[i];
            if (item.Fingerprint is null)
            {
                continue;
            }

            if (series is null || !string.Equals(series, item.SeriesKey, StringComparison.Ordinal))
            {
                series = item.SeriesKey;
                kept = null;
                keptIndex = -1;
            }

            FrameFingerprint fp = item.Fingerprint;
            if (fp.IsBlack)
            {
                marked.Add(new Verdict(item.Index, true, -1, 0, 0));
                continue;
            }

            if (kept is not null)
            {
                FingerprintDelta delta = fp.Compare(kept);
                int cap = GridLayout.ClampMaxDirtyCells(maxDirtyCells, fp.GridCols, fp.GridRows);
                if (delta.DirtyCells <= cap)
                {
                    marked.Add(new Verdict(item.Index, false, keptIndex, delta.DirtyCells, delta.TotalCells));
                    continue;
                }
            }

            kept = fp;
            keptIndex = item.Index;
        }

        return marked;
    }
}
