using System.ComponentModel.DataAnnotations;

namespace Api.Features.CodeReview.Options;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    // Token is not annotated Required — see ClaudeOptions for the same reasoning.
    public string Token { get; init; } = "";

    [Required(AllowEmptyStrings = false)]
    public string UserAgent { get; init; } = "ai-issue-tracker-reviewer";
}
