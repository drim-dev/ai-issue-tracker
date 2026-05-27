using System.ComponentModel.DataAnnotations;

namespace Api.Features.CodeReview.Options;

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    // ApiKey is not annotated Required — validation fires when the slice actually runs,
    // not at app startup, so unrelated tests (Auth, etc.) can boot the host without a secret.
    public string ApiKey { get; init; } = "";

    [Required(AllowEmptyStrings = false)]
    public string Model { get; init; } = "claude-opus-4-7";

    [Range(1, 100)]
    public int MaxTurns { get; init; } = 20;

    [Range(1, 32000)]
    public int MaxTokens { get; init; } = 8192;
}
