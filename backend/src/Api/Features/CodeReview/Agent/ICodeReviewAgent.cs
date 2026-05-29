namespace Api.Features.CodeReview.Agent;

/// <summary>Runs the Claude tool-use loop against the given review context and returns the parsed verdict.</summary>
public interface ICodeReviewAgent
{
    Task<ReviewResult> ReviewAsync(ReviewContext context, CancellationToken ct);
}
