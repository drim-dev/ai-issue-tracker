namespace Api.Features.CodeReview;

public enum ReviewVerdict
{
    Comment,
    RequestChanges,
    Approve,
}

public enum ReviewCategory
{
    SpecMismatch,
    Bug,
    Convention,
    Tests,
}

public enum DiffSide
{
    Left,
    Right,
}

public sealed record LineComment(
    string Path,
    int Line,
    DiffSide Side,
    string Body,
    ReviewCategory Category);

public sealed record ReviewResult(
    string Summary,
    IReadOnlyList<LineComment> Comments,
    ReviewVerdict Verdict);
