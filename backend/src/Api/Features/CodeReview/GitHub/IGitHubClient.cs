namespace Api.Features.CodeReview.GitHub;

/// <summary>Thin client over the GitHub REST API — only the endpoints the reviewer needs.</summary>
public interface IGitHubClient
{
    Task<PrMetadata> GetPullRequestAsync(PrCoordinates coords, CancellationToken ct);

    Task<IReadOnlyList<PrFile>> GetFilesAsync(PrCoordinates coords, CancellationToken ct);

    Task<string> GetUnifiedDiffAsync(PrCoordinates coords, CancellationToken ct);

    Task<string> GetFileContentAsync(PrCoordinates coords, string path, string @ref, CancellationToken ct);

    /// <summary>Login of the user the configured token authenticates as (<c>GET /user</c>).</summary>
    Task<string> GetAuthenticatedUserAsync(CancellationToken ct);

    Task PostReviewAsync(
        PrCoordinates coords,
        string commitSha,
        string body,
        string @event,
        IReadOnlyList<GitHubReviewComment> comments,
        CancellationToken ct);
}

/// <summary>Single inline comment posted via the GitHub Reviews API.</summary>
public sealed record GitHubReviewComment(string Path, int Line, string Side, string Body);
