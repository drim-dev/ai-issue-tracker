namespace Api.Features.CodeReview.Sinks;

/// <summary>Publishes a completed review somewhere — stdout, GitHub, …</summary>
public interface IReviewSink
{
    Task PublishAsync(ReviewResult result, ReviewContext context, CancellationToken ct);
}
