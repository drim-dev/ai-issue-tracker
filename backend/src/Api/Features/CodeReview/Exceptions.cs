using Api.Common.Exceptions;

namespace Api.Features.CodeReview;

/// <summary>Thrown when the supplied PR URL does not match the expected GitHub format.</summary>
public sealed class InvalidPrUrlException(string url)
    : DomainException(
        $"'{url}' is not a valid GitHub pull request URL. Expected https://github.com/{{owner}}/{{repo}}/pull/{{number}}.",
        "code_review:pr_url:invalid",
        StatusCodes.Status400BadRequest);

/// <summary>Thrown when the agent loop fails — MaxTurns exceeded, unexpected stop_reason, malformed final JSON.</summary>
public sealed class ReviewAgentException(string message)
    : DomainException(message, "code_review:agent:failed", StatusCodes.Status502BadGateway);

/// <summary>Thrown when GitHub returns 403 with a depleted rate-limit window.</summary>
public sealed class GitHubRateLimitException(string message)
    : DomainException(message, "code_review:github:rate_limited", StatusCodes.Status429TooManyRequests);
