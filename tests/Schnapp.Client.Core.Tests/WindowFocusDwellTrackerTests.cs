namespace Schnapp.Client.Core.Tests;

using Schnapp.Client.Core.Capture;

using FluentAssertions;

using Xunit;

public class WindowFocusDwellTrackerTests
{
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(10);
    private static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Fires_once_after_the_window_stays_focused()
    {
        var tracker = new WindowFocusDwellTracker();

        tracker.IsDue(42, T0, Dwell).Should().BeFalse();
        tracker.IsDue(42, T0.AddSeconds(9), Dwell).Should().BeFalse();
        tracker.IsDue(42, T0.AddSeconds(10), Dwell).Should().BeTrue();

        tracker.Commit(42, T0.AddSeconds(10));

        tracker.IsDue(42, T0.AddMinutes(5), Dwell).Should().BeFalse();
    }

    [Fact]
    public void Switching_windows_restarts_the_wait()
    {
        var tracker = new WindowFocusDwellTracker();
        tracker.IsDue(1, T0, Dwell);
        tracker.IsDue(1, T0.AddSeconds(9), Dwell);

        tracker.IsDue(2, T0.AddSeconds(9), Dwell).Should().BeFalse();
        tracker.IsDue(2, T0.AddSeconds(18), Dwell).Should().BeFalse();
        tracker.IsDue(2, T0.AddSeconds(19), Dwell).Should().BeTrue();
    }

    [Fact]
    public void Returning_to_a_window_starts_a_new_session()
    {
        var tracker = new WindowFocusDwellTracker();
        tracker.IsDue(1, T0, Dwell);
        tracker.Commit(1, T0.AddSeconds(10));

        tracker.IsDue(2, T0.AddSeconds(11), Dwell);
        tracker.IsDue(1, T0.AddSeconds(12), Dwell).Should().BeFalse();
        tracker.IsDue(1, T0.AddSeconds(22), Dwell).Should().BeTrue();
    }

    [Fact]
    public void No_window_or_zero_dwell_does_not_fire()
    {
        var tracker = new WindowFocusDwellTracker();
        tracker.IsDue(7, T0, Dwell);
        tracker.IsDue(null, T0.AddSeconds(20), Dwell).Should().BeFalse();
        tracker.IsDue(0, T0.AddSeconds(21), Dwell).Should().BeFalse();

        tracker.IsDue(7, T0.AddSeconds(22), TimeSpan.Zero).Should().BeFalse();
        tracker.IsDue(7, T0.AddSeconds(40), Dwell).Should().BeFalse();
        tracker.IsDue(7, T0.AddSeconds(50), Dwell).Should().BeTrue();
    }

    [Fact]
    public void Clear_restarts_the_session_and_keeps_dedupe()
    {
        var tracker = new WindowFocusDwellTracker();
        tracker.IsDue(7, T0, Dwell);
        tracker.NoteCaptured(7, T0.AddSeconds(1));
        tracker.Clear();

        tracker.IsDue(7, T0.AddSeconds(2), Dwell).Should().BeFalse();
        tracker.WasRecentlyCaptured(7, T0.AddSeconds(2)).Should().BeTrue();
        tracker.WasRecentlyCaptured(7, T0.AddSeconds(6)).Should().BeFalse();
    }

    [Fact]
    public void Recent_capture_is_per_window()
    {
        var tracker = new WindowFocusDwellTracker();
        tracker.NoteCaptured(7, T0);

        tracker.WasRecentlyCaptured(7, T0.AddSeconds(4)).Should().BeTrue();
        tracker.WasRecentlyCaptured(8, T0.AddSeconds(4)).Should().BeFalse();
    }
}
