namespace Schnapp.Client.Core.Tests;

using System;

using FluentAssertions;

using Schnapp.Client.Core.Time;

using Xunit;

public class TimestampParserTests
{
    [Fact]
    public void Parse_now_is_near_utc_now()
    {
        var t = TimestampParser.Parse("now");
        t.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_time_today_in_utc()
    {
        var t = TimestampParser.Parse("13:15", "UTC");
        t.Hour.Should().Be(13);
        t.Minute.Should().Be(15);
        t.Offset.Should().Be(TimeSpan.Zero);
    }
}
