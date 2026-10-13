namespace Schnapp.Client.Windows.Capture;

using System;
using System.Collections.Generic;

/// <summary>
/// Builds discrete downscale steps for a monitor resolution for capture UI and post-capture resize.
/// </summary>
/// <remarks>
/// <para>
/// Port of Linux <c>Schnapp.Client.Linux.Capture.MonitorScaling</c>. Both platforms return
/// steps in <b>ascending</b> order (smallest ratio first) so sliders/trackbars can show
/// ~25% on the left and 100% on the right without reversing the list.
/// </para>
/// <para>
/// Preferred algorithm: integer-friendly ratios derived from the GCD of width and height.
/// Fallback: even 5% steps from 25% to 100% when the GCD path is unusable (coprime sizes,
/// or too few steps after filtering).
/// </para>
/// </remarks>
public static class MonitorScaling
{
    /// <summary>
    /// Greatest common divisor of two integers (iterative Euclidean algorithm).
    /// </summary>
    /// <param name="a">First integer (sign is ignored).</param>
    /// <param name="b">Second integer (sign is ignored).</param>
    /// <returns>Non-negative GCD; 0 only if both inputs are 0.</returns>
    private static int Gcd(int a, int b)
    {
        // Because negative dimensions should never reach here, but Abs keeps Euclidean safe.
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    /// <summary>
    /// Returns discrete scale steps for <paramref name="width"/>x<paramref name="height"/>
    /// within [<paramref name="minimum"/>, <paramref name="maximum"/>], smallest ratio first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order is always ascending (no list reverse). Callers that need 100% on the right of a
    /// control should use the last element / maximum trackbar index, not reverse the list.
    /// </para>
    /// <para>
    /// GCD path: ratios of the form <c>k/g</c> where <c>g = gcd(w,h)</c> produce pixel sizes
    /// that are integer multiples of the reduced base rectangle. Steps with reduced fraction
    /// denominators &gt; 32 are skipped because they clutter the UI with near-duplicates.
    /// </para>
    /// <para>
    /// Fallback path: when <c>g ≤ 1</c> (coprime / odd VM modes) only one integer step (100%)
    /// exists; when filtering leaves fewer than two steps, the UI would be useless — so we
    /// switch to 5% increments from 25% through 100%.
    /// </para>
    /// </remarks>
    /// <param name="width">Logical capture width in pixels (must be positive).</param>
    /// <param name="height">Logical capture height in pixels (must be positive).</param>
    /// <param name="minimum">Smallest allowed scale ratio (default 0.25 = 25%).</param>
    /// <param name="maximum">Largest allowed scale ratio (default 1.0 = 100%).</param>
    /// <returns>
    /// At least one <see cref="MonitorScale"/>; typically many steps from ~25% to 100%,
    /// ordered smallest → largest.
    /// </returns>
    /// <exception cref="ArgumentException">Width/height not positive, or invalid min/max.</exception>
    public static List<MonitorScale>
        GetMonitorScales(int width, int height, double minimum = 0.25, double maximum = 1.0)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Width and height must be positive.");
        }

        if (minimum <= 0 || maximum <= 0 || minimum > maximum)
        {
            throw new ArgumentException("Invalid min/max range.");
        }

        int g = Gcd(width, height);

        // Because g==1 (coprime dimensions) only yields k=1 at 100% — not enough UI steps.
        if (g <= 1)
        {
            return BuildPercentFallbackScales(width, height, minimum, maximum);
        }

        // Because dividing by g gives the reduced base tile; each k multiplies that tile.
        int baseW = width / g;
        int baseH = height / g;

        // Because k/g must stay inside [minimum, maximum]; k is the numerator in units of 1/g.
        int kMin = (int)Math.Ceiling(minimum * g);
        int kMax = (int)Math.Floor(maximum * g);

        kMin = Math.Max(1, kMin);
        kMax = Math.Max(kMin, kMax);

        var results = new List<MonitorScale>();

        // Because iterating k ascending produces smallest→largest ratios without a later Reverse.
        for (int k = kMin; k <= kMax; k++)
        {
            double ratio = (double)k / g;

            int d = Gcd(k, g);
            // Because the reduced denominator g/d labels the fraction; huge dens make ugly UI.
            int denom = g / d;
            if (denom > 32)
            {
                continue;
            }

            string fraction = (k / d) + "/" + denom;
            double percent = ratio * 100;
            string percentStr = percent.ToString("0.##") + "%";

            results.Add(new MonitorScale(
                ratio,
                k * baseW,
                k * baseH,
                fraction,
                percent,
                percentStr
            ));
        }

        // Because denom filtering can leave 0-1 steps on awkward sizes — fall back then.
        if (results.Count < 2)
        {
            return BuildPercentFallbackScales(width, height, minimum, maximum);
        }

        return results;
    }

    /// <summary>
    /// Builds even 5% steps from 25% to 100% (clamped to min/max), smallest first.
    /// </summary>
    /// <remarks>
    /// Used when the GCD-based path cannot produce a usable ladder. Pixel sizes are rounded
    /// from full resolution because there is no integer base-tile guarantee in this path.
    /// </remarks>
    /// <param name="width">Full resolution width in pixels.</param>
    /// <param name="height">Full resolution height in pixels.</param>
    /// <param name="minimum">Inclusive lower ratio bound.</param>
    /// <param name="maximum">Inclusive upper ratio bound.</param>
    /// <returns>Non-empty list of scales, ascending by ratio.</returns>
    private static List<MonitorScale> BuildPercentFallbackScales(
        int width, int height, double minimum, double maximum)
    {
        var results = new List<MonitorScale>();

        // Because 5% steps from 25-100 are predictable for users and dense enough for a slider.
        for (int pct = 25; pct <= 100; pct += 5)
        {
            double ratio = pct / 100.0;
            if (ratio + 1e-9 < minimum || ratio - 1e-9 > maximum)
            {
                continue;
            }

            // Because non-integer scales cannot use the GCD base tile; Round is good enough for display.
            int w = Math.Max(1, (int)Math.Round(width * ratio));
            int h = Math.Max(1, (int)Math.Round(height * ratio));
            results.Add(new MonitorScale(
                ratio,
                w,
                h,
                pct + "/100",
                pct,
                pct + "%"));
        }

        if (results.Count == 0)
        {
            // Because callers may pass a min/max window that excludes 25-100; still return one step.
            double ratio = Math.Max(minimum, Math.Min(maximum, 1.0));
            int w = Math.Max(1, (int)Math.Round(width * ratio));
            int h = Math.Max(1, (int)Math.Round(height * ratio));
            double percent = ratio * 100;
            results.Add(new MonitorScale(
                ratio, w, h,
                Math.Round(percent).ToString("0") + "/100",
                percent,
                percent.ToString("0.##") + "%"));
        }

        return results;
    }
}

/// <summary>
/// One discrete scale option: ratio, resulting pixel size, and human-readable labels.
/// </summary>
public readonly struct MonitorScale
{
    /// <summary>Linear scale relative to full resolution (typically 0.25-1.0).</summary>
    public double Ratio { get; }

    /// <summary>Width in pixels after applying <see cref="Ratio"/>.</summary>
    public int Width { get; }

    /// <summary>Height in pixels after applying <see cref="Ratio"/>.</summary>
    public int Height { get; }

    /// <summary>Reduced fraction string (e.g. <c>3/4</c> or <c>75/100</c> for percent steps).</summary>
    public string Fraction { get; }

    /// <summary>Ratio as a percentage number (e.g. 75 for 75%).</summary>
    public double Percent { get; }

    /// <summary>Formatted percent label (e.g. <c>75%</c>).</summary>
    public string PercentStr { get; }

    /// <summary>
    /// Creates a scale step.
    /// </summary>
    /// <param name="ratio">Linear scale factor.</param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    /// <param name="fraction">Human-readable fraction label.</param>
    /// <param name="percent">Percent numeric value.</param>
    /// <param name="percentStr">Percent display string.</param>
    public MonitorScale(double ratio, int width, int height, string fraction, double percent, string percentStr)
    {
        Ratio = ratio;
        Width = width;
        Height = height;
        Fraction = fraction ?? "";
        Percent = percent;
        PercentStr = percentStr ?? "";
    }
}
