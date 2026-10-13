namespace Schnapp.Client.Core.Tests;

using FluentAssertions;

using Schnapp.Client.Core.Tags;

public class TagTokenAutocompleteTests
{
    [Fact]
    public void TokenAt_finds_middle_token()
    {
        //              012345678901
        const string t = "foo bar baz";
        var span = TagTokenAutocomplete.TokenAt(t, 5); // on 'a' of bar
        span.Token.Should().Be("bar");
        span.Start.Should().Be(4);
        span.End.Should().Be(7);
    }

    private static readonly string[] candidates = new[] { "billing", "Bill", "meet" };

    [Fact]
    public void Filter_prefix_case_insensitive()
    {
        var matches = TagTokenAutocomplete.Filter(candidates, "bi");
        matches.Should().Equal("billing", "Bill");
    }

    [Fact]
    public void Apply_inserts_trailing_space()
    {
        var span = TagTokenAutocomplete.TokenAt("bi", 2);
        var (text, caret) = TagTokenAutocomplete.Apply("bi", span, "billing");
        text.Should().Be("billing ");
        caret.Should().Be("billing ".Length);
    }

    [Fact]
    public void Apply_preserves_following_tokens()
    {
        var span = TagTokenAutocomplete.TokenAt("foo bi baz", 5);
        var (text, _) = TagTokenAutocomplete.Apply("foo bi baz", span, "billing");
        text.Should().Be("foo billing baz");
    }
}
