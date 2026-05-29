using Api.Features.CodeReview;
using Api.Features.CodeReview.GitHub;
using FluentAssertions;

namespace Api.Tests.Features.CodeReview;

public class DiffParserTests
{
    private const string SimplePatch =
        "@@ -10,3 +10,4 @@\n" +
        " context_a\n" +
        "-removed_b\n" +
        "+added_b\n" +
        "+added_c\n" +
        " context_d\n";

    [Fact]
    public void ParseHunks_reads_header()
    {
        var hunks = DiffParser.ParseHunks(SimplePatch);

        hunks.Should().HaveCount(1);
        hunks[0].Should().BeEquivalentTo(new DiffHunk(10, 3, 10, 4));
    }

    [Fact]
    public void ParseHunks_defaults_counts_to_one_when_omitted()
    {
        var hunks = DiffParser.ParseHunks("@@ -5 +5 @@\n-x\n+y\n");
        hunks[0].Should().BeEquivalentTo(new DiffHunk(5, 1, 5, 1));
    }

    [Fact]
    public void ParseHunks_handles_empty_input()
    {
        DiffParser.ParseHunks(null).Should().BeEmpty();
        DiffParser.ParseHunks("").Should().BeEmpty();
    }

    [Theory]
    // RIGHT side: added lines 11 (added_b) and 12 (added_c)
    [InlineData(11, DiffSide.Right, true)]
    [InlineData(12, DiffSide.Right, true)]
    [InlineData(10, DiffSide.Right, false)]  // context line
    [InlineData(13, DiffSide.Right, false)]  // context line
    [InlineData(99, DiffSide.Right, false)]  // outside hunk
    // LEFT side: removed line 11 (removed_b)
    [InlineData(11, DiffSide.Left, true)]
    [InlineData(10, DiffSide.Left, false)]
    [InlineData(12, DiffSide.Left, false)]
    public void IsLineInDiff_works(int line, DiffSide side, bool expected)
    {
        DiffParser.IsLineInDiff(SimplePatch, line, side).Should().Be(expected);
    }

    [Fact]
    public void IsLineInDiff_handles_multiple_hunks()
    {
        const string patch =
            "@@ -1,2 +1,2 @@\n" +
            " a\n" +
            "-b\n" +
            "+B\n" +
            "@@ -100,2 +100,3 @@\n" +
            " ctx\n" +
            "+new_line\n";

        DiffParser.IsLineInDiff(patch, 1, DiffSide.Right).Should().BeFalse(); // context
        DiffParser.IsLineInDiff(patch, 2, DiffSide.Right).Should().BeTrue();  // +B
        DiffParser.IsLineInDiff(patch, 101, DiffSide.Right).Should().BeTrue(); // +new_line
        DiffParser.IsLineInDiff(patch, 2, DiffSide.Left).Should().BeTrue();    // -b
    }

    [Fact]
    public void IsLineInDiff_handles_empty_input()
    {
        DiffParser.IsLineInDiff(null, 1, DiffSide.Right).Should().BeFalse();
        DiffParser.IsLineInDiff("", 1, DiffSide.Right).Should().BeFalse();
        DiffParser.IsLineInDiff(SimplePatch, 0, DiffSide.Right).Should().BeFalse();
    }
}
