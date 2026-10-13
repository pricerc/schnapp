namespace Schnapp.Client.Linux.Capture;

using System;
using System.Collections.Generic;

/// <summary>
/// Builds discrete downscale steps for a monitor resolution for capture UI and post-capture resize.
/// </summary>
/// <remarks>
/// <para>
/// Same algorithm as Windows <c>Schnapp.Client.Windows.Capture.MonitorScaling</c>. Both return
/// steps in <b>ascending</b> order (smallest ratio first) so sliders show ~25% on the left and
/// 100% on the right without reversing the list.
/// </para>
/// <para>
/// Preferred algorithm: integer-friendly ratios from the GCD of width and height.
/// Fallback: even 5% steps from 25% to 100% when the GCD path is unusable.
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
    /// Order is always ascending (no list reverse). Avalonia sliders use percent values with
    /// Minimum=25 and Maximum=100; tick lists should also be ascending for consistent snap order.
    /// </para>
    /// <para>
    /// GCD path: ratios of the form <c>k/g</c> where <c>g = gcd(w,h)</c> produce pixel sizes
    /// that are integer multiples of the reduced base rectangle.
    /// </para>
    /// <para>
    /// Fallback: when <c>g ≤ 1</c> or fewer than two steps remain, use 5% increments from 25%-100%
    /// because coprime/VM sizes otherwise only expose a single 100% tick.
    /// </para>
    /// </remarks>
    /// <param name="width">Logical capture width in pixels (must be positive).</param>
    /// <param name="height">Logical capture height in pixels (must be positive).</param>
    /// <param name="minimum">Smallest allowed scale ratio (default 0.25 = 25%).</param>
    /// <param name="maximum">Largest allowed scale ratio (default 1.0 = 100%).</param>
    /// <returns>At least one <see cref="MonitorScale"/>, ordered smallest → largest.</returns>
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

        // Because g==1 only yields a single 100% integer step — not enough for a scale UI.
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

        // Because iterating k ascending produces smallest→largest without a later Reverse.
        for (int k = kMin; k <= kMax; k++)
        {
            double ratio = (double)k / g;

            int d = Gcd(k, g);
            string fraction = $"{k / d}/{g / d}";

            double percent = ratio * 100;
            string percentStr = $"{percent:0.##}%";

            results.Add((
                ratio,
                k * baseW,
                k * baseH,
                fraction,
                percent,
                percentStr
            ));
        }

        // Because odd sizes can leave a single step — fall back so the slider has range.
        if (results.Count < 2)
        {
            return BuildPercentFallbackScales(width, height, minimum, maximum);
        }

        return results;
    }

    /// <summary>
    /// Builds even 5% steps from 25% to 100% (clamped to min/max), smallest first.
    /// </summary>
    /// <param name="width">Full resolution width in pixels.</param>
    /// <param name="height">Full resolution height in pixels.</param>
    /// <param name="minimum">Inclusive lower ratio bound.</param>
    /// <param name="maximum">Inclusive upper ratio bound.</param>
    /// <returns>Non-empty list of scales, ascending by ratio.</returns>
    private static List<MonitorScale> BuildPercentFallbackScales(
        int width, int height, double minimum, double maximum)
    {
        var results = new List<MonitorScale>();

        // Because 5% steps from 25-100 are predictable and dense enough for a slider.
        for (int pct = 25; pct <= 100; pct += 5)
        {
            double ratio = pct / 100.0;
            if (ratio + 1e-9 < minimum || ratio - 1e-9 > maximum)
            {
                continue;
            }

            // Because percent steps are not base-tile multiples; Round is fine for encode resize.
            int w = Math.Max(1, (int)Math.Round(width * ratio));
            int h = Math.Max(1, (int)Math.Round(height * ratio));
            results.Add((
                ratio,
                w,
                h,
                pct + "/100",
                (double)pct,
                pct + "%"));
        }

        if (results.Count == 0)
        {
            // Because extreme min/max can exclude the 25-100 ladder; still return one usable step.
            double ratio = Math.Max(minimum, Math.Min(maximum, 1.0));
            int w = Math.Max(1, (int)Math.Round(width * ratio));
            int h = Math.Max(1, (int)Math.Round(height * ratio));
            double percent = ratio * 100;
            results.Add((
                ratio, w, h,
                $"{Math.Round(percent):0}/100",
                percent,
                $"{percent:0.##}%"));
        }

        return results;
    }
}

/// <summary>
/// One discrete scale option: ratio, resulting pixel size, and human-readable fraction/percent.
/// </summary>
/// <param name="Ratio">Linear scale relative to full resolution (0-1 typically).</param>
/// <param name="Width">Width in pixels after applying <paramref name="Ratio"/>.</param>
/// <param name="Height">Height in pixels after applying <paramref name="Ratio"/>.</param>
/// <param name="Fraction">Reduced fraction string (e.g. <c>3/4</c>).</param>
/// <param name="Percent">Ratio as a percentage number.</param>
/// <param name="PercentStr">Formatted percent label (e.g. <c>75%</c>).</param>
public record struct MonitorScale(double Ratio, int Width, int Height, string Fraction, double Percent, string PercentStr)
{
    /// <summary>Tuple conversion for call sites that unpack scale fields.</summary>
    /// <param name="value">Source scale.</param>
    /// <returns>Positional tuple of the same fields.</returns>
    public static implicit operator (double ratio, int width, int height, string fraction, double percent, string percentStr)(MonitorScale value)
    {
        return (value.Ratio, value.Width, value.Height, value.Fraction, value.Percent, value.PercentStr);
    }

    /// <summary>Builds a <see cref="MonitorScale"/> from a positional tuple.</summary>
    /// <param name="value">Field tuple.</param>
    /// <returns>Equivalent <see cref="MonitorScale"/>.</returns>
    public static implicit operator MonitorScale((double ratio, int width, int height, string fraction, double percent, string percentStr) value)
    {
        return new MonitorScale(value.ratio, value.width, value.height, value.fraction, value.percent, value.percentStr);
    }
}
