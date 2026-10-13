using System.Diagnostics;

namespace Schnapp.Utils;

/// <summary>
/// Thread-safe progress lines on stderr. Default is a terse line every <c>step</c> items;
/// <c>--progress</c> adds percent, rate and ETA, refreshed about once a second.
/// </summary>
internal sealed class ProgressReporter
{
    private readonly string _label;
    private readonly int _total;
    private readonly bool _detailed;
    private readonly int _step;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _inPlace = !Console.IsErrorRedirected;
    private readonly object _gate = new();
    private int _done;
    private int _failed;
    private long _lastPrintMs = -1000;

    public ProgressReporter(string label, int total, bool detailed, int step = 100)
    {
        _label = label;
        _total = total;
        _detailed = detailed;
        _step = Math.Max(1, step);
    }

    public void Tick(bool failed = false)
    {
        if (failed)
            Interlocked.Increment(ref _failed);

        var n = Interlocked.Increment(ref _done);
        var last = n == _total;
        if (_detailed ? !(last || _clock.ElapsedMilliseconds - Volatile.Read(ref _lastPrintMs) >= 1000)
                      : !(last || n % _step == 0))
            return;

        lock (_gate)
        {
            Volatile.Write(ref _lastPrintMs, _clock.ElapsedMilliseconds);
            Console.Error.Write(Format(n) + (_detailed && _inPlace && !last ? "\r" : "\n"));
        }
    }

    private string Format(int n)
    {
        var failed = Volatile.Read(ref _failed);
        var tail = failed > 0 ? $" (failed {failed})" : "";
        if (!_detailed)
            return $"{_label} {n}/{_total}{tail}";

        var secs = _clock.Elapsed.TotalSeconds;
        var rate = secs > 0 ? n / secs : 0;
        var eta = rate > 0 ? TimeSpan.FromSeconds((_total - n) / rate) : TimeSpan.Zero;
        // Trailing spaces overwrite a longer previous line when redrawing in place.
        return $"{_label} {n}/{_total} {100.0 * n / _total:0}%  {rate:0.0}/s  "
            + $"elapsed {_clock.Elapsed:mm\\:ss}  eta {eta:hh\\:mm\\:ss}{tail}   ";
    }
}
