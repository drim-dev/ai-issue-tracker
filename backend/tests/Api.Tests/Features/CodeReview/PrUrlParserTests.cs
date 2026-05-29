using Api.Features.CodeReview;
using Api.Features.CodeReview.GitHub;
using FluentAssertions;

namespace Api.Tests.Features.CodeReview;

public class PrUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/anthropics/claude-code/pull/42", "anthropics", "claude-code", 42)]
    [InlineData("http://github.com/anthropics/claude-code/pull/1", "anthropics", "claude-code", 1)]
    [InlineData("https://github.com/o.r-g/r.e-po/pull/9999", "o.r-g", "r.e-po", 9999)]
    [InlineData("https://github.com/o/r/pull/7/files", "o", "r", 7)]
    [InlineData("https://github.com/o/r/pull/7?diff=split", "o", "r", 7)]
    [InlineData(" https://github.com/o/r/pull/7 ", "o", "r", 7)]
    public void Parses_valid_urls(string url, string owner, string repo, int number)
    {
        var coords = PrUrlParser.Parse(url);

        coords.Owner.Should().Be(owner);
        coords.Repo.Should().Be(repo);
        coords.Number.Should().Be(number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://gitlab.com/o/r/pull/1")]
    [InlineData("https://github.com/o/r/issues/1")]
    [InlineData("https://github.com/o/r/pull/")]
    [InlineData("https://github.com/o/r/pull/abc")]
    [InlineData("https://github.com/o/r/pull/0")]
    public void Rejects_invalid_urls(string url)
    {
        var act = () => PrUrlParser.Parse(url);
        act.Should().Throw<InvalidPrUrlException>();
    }

    [Fact]
    public void TryParse_returns_false_without_throwing()
    {
        PrUrlParser.TryParse("nope", out var coords).Should().BeFalse();
        coords.Should().BeNull();
    }
}
