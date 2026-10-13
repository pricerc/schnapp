using FluentAssertions;
using Medo;
using Schnapp.Contracts;
using Schnapp.Domain;

namespace Schnapp.Domain.Tests;

public class Uuid7TextTests
{
    [Fact]
    public void Format_is_id26_length_26()
    {
        var id = IdFactory.NewId();
        var s = Uuid7Text.Format(id);
        s.Should().HaveLength(26);
        Uuid7Text.TryParse(s, out var parsed).Should().BeTrue();
        parsed.Should().Be(id);
    }

    [Fact]
    public void Parse_accepts_legacy_rfc_d_and_n()
    {
        var id = IdFactory.NewId();
        Uuid7Text.Parse(id.ToString("D")).Should().Be(id);
        Uuid7Text.Parse(id.ToString("N")).Should().Be(id);
    }

    [Fact]
    public void Json_round_trips_as_id26()
    {
        var id = IdFactory.NewId();
        var json = System.Text.Json.JsonSerializer.Serialize(id, Uuid7Json.DefaultOptions);
        json.Should().Be("\"" + Uuid7Text.Format(id) + "\"");
        var back = System.Text.Json.JsonSerializer.Deserialize<Uuid7>(json, Uuid7Json.DefaultOptions);
        back.Should().Be(id);
    }

    [Fact]
    public void Json_reads_legacy_rfc()
    {
        var id = IdFactory.NewId();
        var json = "\"" + id.ToString("D") + "\"";
        var back = System.Text.Json.JsonSerializer.Deserialize<Uuid7>(json, Uuid7Json.DefaultOptions);
        back.Should().Be(id);
    }

    [Fact]
    public void Uuid7Param_try_parse_id26()
    {
        var id = IdFactory.NewId();
        Uuid7Param.TryParse(Uuid7Text.Format(id), out var p).Should().BeTrue();
        ((Uuid7)p).Should().Be(id);
        p.ToString().Should().Be(Uuid7Text.Format(id));
    }
}
