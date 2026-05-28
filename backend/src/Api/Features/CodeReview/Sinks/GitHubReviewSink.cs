using Api.Features.CodeReview.GitHub;
using Microsoft.Extensions.Logging;

namespace Api.Features.CodeReview.Sinks;

/// <summary>Publishes the review as a GitHub pull-request review via <see cref="IGitHubClient"/>.</summary>
public sealed class GitHubReviewSink(IGitHubClient github, ILogger<GitHubReviewSink> logger) : IReviewSink
{
    public async Task PublishAsync(ReviewResult result, ReviewContext context, CancellationToken ct)
    {
        var @event = result.Verdict switch
        {
            ReviewVerdict.Approve => "APPROVE",
            ReviewVerdict.RequestChanges => "REQUEST_CHANGES",
            _ => "COMMENT",
        };

        // GitHub rejects APPROVE / REQUEST_CHANGES on a PR authored by the token's own user
        // with 422. Downgrade to COMMENT so the review still publishes.
        if (@event is "REQUEST_CHANGES" or "APPROVE")
        {
            var tokenUser = await github.GetAuthenticatedUserAsync(ct);
            if (!string.IsNullOrEmpty(tokenUser)
                && string.Equals(tokenUser, context.Pr.Author, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Token user '{User}' is the PR author; downgrading verdict '{Event}' to COMMENT.",
                    tokenUser, @event);
                @event = "COMMENT";
            }
        }

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
