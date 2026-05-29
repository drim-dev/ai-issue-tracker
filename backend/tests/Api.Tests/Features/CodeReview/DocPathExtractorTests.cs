using Api.Features.CodeReview.Agent;
using FluentAssertions;

namespace Api.Tests.Features.CodeReview;

public class DocPathExtractorTests
{
    [Theory]
    [InlineData("See docs/designs/2026-05-27-ai-pr-reviewer.md for details",
        new[] { "docs/designs/2026-05-27-ai-pr-reviewer.md" })]
    [InlineData("Implements docs/plans/foo.md and docs/specs/bar.md.",
        new[] { "docs/plans/foo.md", "docs/specs/bar.md" })]
    [InlineData("Nothing referenced", new string[0])]
    [InlineData("Inline `docs/designs/x.md` in code", new[] { "docs/designs/x.md" })]
    [InlineData("Duplicate docs/plans/p.md and docs/plans/p.md", new[] { "docs/plans/p.md" })]
    public void ExtractFromBody_finds_doc_paths(string body, string[] expected)
    {
        var result = DocPathExtractor.ExtractFromBody(body);
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ExtractFromBody_ignores_non_doc_md_paths()
    {
        DocPathExtractor.ExtractFromBody("README.md and docs/other/x.md").Should().BeEmpty();
    }

    [Fact]
    public void ExtractFromBody_handles_null_or_empty()
    {
        DocPathExtractor.ExtractFromBody(null).Should().BeEmpty();
        DocPathExtractor.ExtractFromBody("").Should().BeEmpty();
    }

    [Fact]
    public void MatchByName_matches_via_title()
    {
        var docs = new[] { "docs/designs/auth.md", "docs/designs/issues.md" };
        var result = DocPathExtractor.MatchByName(
            docs,
            prTitle: "feat: implement Auth module",
            changedFiles: []);
        result.Should().BeEquivalentTo(["docs/designs/auth.md"]);
    }

    [Fact]
    public void MatchByName_matches_via_changed_files()
    {
        var docs = new[] { "docs/specs/billing.md", "docs/specs/auth.md" };
        var result = DocPathExtractor.MatchByName(
            docs,
            prTitle: "chore: misc",
            changedFiles: ["backend/src/Api/Features/Billing/Foo.cs"]);
        result.Should().BeEquivalentTo(["docs/specs/billing.md"]);
    }

    [Fact]
    public void MatchByName_returns_empty_when_no_signal()
    {
        var docs = new[] { "docs/designs/foo.md" };
        DocPathExtractor.MatchByName(docs, "unrelated", ["unrelated.cs"]).Should().BeEmpty();
    }
}
