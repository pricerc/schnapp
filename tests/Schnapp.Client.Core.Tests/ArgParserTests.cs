namespace Schnapp.Client.Core.Tests;

using FluentAssertions;

using Schnapp.Cli;

using Xunit;

// Parser lives in Cli; tests reference Cli project only if we add ref — keep parser tests in Cli or move parser to Core.
// For now duplicate minimal check via reflection-free: add Cli ref to tests.
public class ArgParserTests
{
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    public void Parses_flags_and_bare_keywords()
    {
        var p = ArgParser.Parse(new[] { "shot.png", "--at", "13:15", "tags", "cli,demo", "/monitor", "m1" });
        p.Positionals.Should().ContainSingle("shot.png");
        p.Get("at").Should().Be("13:15");
        p.Get("tags").Should().Be("cli,demo");
        p.Get("monitor").Should().Be("m1");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    public void Absolute_unix_path_is_positional_not_switch()
    {
        var p = ArgParser.Parse(new[] { "/home/ryan/src/schnapp/scripts/test-image.png", "--at", "13:15" });
        p.Positionals.Should().ContainSingle("/home/ryan/src/schnapp/scripts/test-image.png");
        p.Get("at").Should().Be("13:15");
    }
}
