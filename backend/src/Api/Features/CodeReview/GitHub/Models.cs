namespace Api.Features.CodeReview.GitHub;

/// <summary>Parsed coordinates of a GitHub pull request.</summary>
public sealed record PrCoordinates(string Owner, string Repo, int Number);

/// <summary>Subset of GitHub PR metadata used by the reviewer.</summary>
public sealed record PrMetadata(
    PrCoordinates Coordinates,
    string Title,
    string Body,
    string HeadSha,
    string BaseSha,
    string HeadRef,
    string BaseRef,
    string Author);

/// <summary>One changed file in a PR with its per-file unified-diff patch.</summary>
public sealed record PrFile(
    string Path,
    string Status,
    int Additions,
    int Deletions,
    string? Patch);

/// <summary>One hunk in a unified diff, parsed from a <c>@@ -a,b +c,d @@</c> header.</summary>
public sealed record DiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount);
