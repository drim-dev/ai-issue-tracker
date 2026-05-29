using System.Text.RegularExpressions;

namespace Api.Features.CodeReview.Agent;

/// <summary>
/// Finds <c>docs/{designs|plans|specs}/...md</c> paths in PR bodies, with a fallback that
/// matches available doc filenames against the PR title and changed-file names when the
/// body does not explicitly link them.
/// </summary>
public static partial class DocPathExtractor
{
    [GeneratedRegex(
        @"docs/(?:designs|plans|specs)/[\w\-./]+\.md",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocPathRegex();

    public static IReadOnlyList<string> ExtractFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        return DocPathRegex().Matches(body)
            .Select(m => m.Value.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Fallback: pick docs whose filename stem appears in the PR title or in any of the
    /// changed file paths. Matching is case-insensitive on the filename stem.
    /// </summary>
    public static IReadOnlyList<string> MatchByName(
        IEnumerable<string> docPaths,
        string prTitle,
        IEnumerable<string> changedFiles)
    {
        var title = prTitle ?? "";
        var files = changedFiles.ToArray();
        var matches = new List<string>();

        foreach (var path in docPaths)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(stem))
            {
                continue;
            }

            bool inTitle = title.Contains(stem, StringComparison.OrdinalIgnoreCase);
            bool inFiles = files.Any(f => f.Contains(stem, StringComparison.OrdinalIgnoreCase));
            if (inTitle || inFiles)
            {
                matches.Add(path);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
