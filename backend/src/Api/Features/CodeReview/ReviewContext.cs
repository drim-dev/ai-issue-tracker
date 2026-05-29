using Api.Features.CodeReview.GitHub;

namespace Api.Features.CodeReview;

public sealed record ReviewContext(
    PrMetadata Pr,
    IReadOnlyList<PrFile> ChangedFiles,
    string UnifiedDiff,
    IReadOnlyList<string> DocPathsFromPrBody);
