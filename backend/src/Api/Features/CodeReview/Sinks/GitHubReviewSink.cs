using Api.Features.CodeReview.GitHub;

namespace Api.Features.CodeReview.Sinks;

/// <summary>Publishes the review as a GitHub pull-request review via <see cref="IGitHubClient"/>.</summary>
public sealed class GitHubReviewSink(IGitHubClient github) : IReviewSink
{
    public async Task PublishAsync(ReviewResult result, ReviewContext context, CancellationToken ct)
    {
        var @event = result.Verdict switch
        {
            ReviewVerdict.Approve => "APPROVE",
            ReviewVerdict.RequestChanges => "REQUEST_CHANGES",
            _ => "COMMENT",
        };

        var comments = result.Comments
            .Select(c => new GitHubReviewComment(
                Path: c.Path,
                Line: c.Line,
                Side: c.Side == DiffSide.Left ? "LEFT" : "RIGHT",
                Body: c.Body))
            .ToArray();

        await github.PostReviewAsync(
            coords: context.Pr.Coordinates,
            commitSha: context.Pr.HeadSha,
            body: result.Summary,
            @event: @event,
            comments: comments,
            ct: ct);
    }
}
