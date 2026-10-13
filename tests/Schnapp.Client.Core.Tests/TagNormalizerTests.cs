namespace Schnapp.Client.Core.Tests;

using FluentAssertions;

using Schnapp.Client.Core.Tags;
using Schnapp.Contracts;
using Xunit;

public class TagNormalizerTests
{
    [Theory]
    [InlineData("cli,demo", "cli demo")]
    [InlineData("cli;demo\tfoo", "cli demo foo")]
    [InlineData("  a , b  ", "a b")]
    [InlineData("already space separated", "already space separated")]
    [InlineData("", "")]
    public void Normalize_splits_and_rejoins(string input, string expected)
    {
        TagNormalizer.Normalize(input).Should().Be(expected);
        TagText.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void DistinctTokens_dedupes_preserving_order()
    {
        TagText.DistinctTokens("a b a,c;b").Should().Equal("a", "b", "c");
    }
}
