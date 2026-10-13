namespace Schnapp.Client.Core.Tests;

using FluentAssertions;

using Schnapp.Contracts;

public sealed class ListCodeGeneratorTests
{
    [Theory]
    [InlineData("McCallum Industries", "MI")]
    [InlineData("Schnapp", "SCHN")]
    [InlineData("Schnapp Development", "SD")]
    [InlineData("A", "AX")]
    [InlineData("AB", "AB")]
    [InlineData("Advanced ERP Systems", "AES")]
    [InlineData("one two three four five", "OTTF")]
    [InlineData("-", "XX")]
    [InlineData("", "XX")]
    [InlineData(null, "XX")]
    public void StemFromName_is_2_to_4_chars(string? name, string expectedStem)
    {
        var stem = ListCodeGenerator.StemFromName(name);
        stem.Length.Should().BeInRange(2, 4);
        stem.Should().Be(expectedStem);
    }

    [Fact]
    public void Allocate_always_appends_3_digit_sequence()
    {
        var code = ListCodeGenerator.Allocate("McCallum Industries", Array.Empty<string>());
        code.Should().Be("MI001");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    public void Allocate_skips_taken_sequences()
    {
        var code = ListCodeGenerator.Allocate("McCallum Industries", new[] { "MI001", "MI002", "other" });
        code.Should().Be("MI003");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    public void Allocate_is_case_insensitive_for_taken()
    {
        var code = ListCodeGenerator.Allocate("Schnapp", new[] { "schn001" });
        code.Should().Be("SCHN002");
    }
}
