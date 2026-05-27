using System.Text.RegularExpressions;

namespace Api.Features.CodeReview.GitHub;

/// <summary>
/// Parses unified-diff hunk headers (<c>@@ -a,b +c,d @@</c>) and answers whether a given
/// (line, side) lands inside a hunk — used to validate inline review comments before
/// publishing, since GitHub rejects the whole request when any comment is out of diff.
/// </summary>
public static partial class DiffParser
{
    [GeneratedRegex(
        @"^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@",
        RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeaderRegex();

    public static IReadOnlyList<DiffHunk> ParseHunks(string? patch)
    {
        if (string.IsNullOrEmpty(patch))
        {
            return [];
        }

        var hunks = new List<DiffHunk>();
        foreach (var line in patch.Split('\n'))
        {
            var match = HunkHeaderRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            hunks.Add(new DiffHunk(
                OldStart: int.Parse(match.Groups["oldStart"].Value),
                OldCount: match.Groups["oldCount"].Success ? int.Parse(match.Groups["oldCount"].Value) : 1,
                NewStart: int.Parse(match.Groups["newStart"].Value),
                NewCount: match.Groups["newCount"].Success ? int.Parse(match.Groups["newCount"].Value) : 1));
        }
        return hunks;
    }

    /// <summary>
    /// Walks the patch tracking actual file line numbers and reports whether the requested
    /// (line, side) is a line that was added (RIGHT) or removed (LEFT) inside a hunk.
    /// </summary>
    public static bool IsLineInDiff(string? patch, int line, DiffSide side)
    {
        if (string.IsNullOrEmpty(patch) || line <= 0)
        {
            return false;
        }

        int oldLine = 0;
        int newLine = 0;
        bool inHunk = false;

        foreach (var raw in patch.Split('\n'))
        {
            var header = HunkHeaderRegex().Match(raw);
            if (header.Success)
            {
                oldLine = int.Parse(header.Groups["oldStart"].Value);
                newLine = int.Parse(header.Groups["newStart"].Value);
                inHunk = true;
                continue;
            }

            if (!inHunk || raw.Length == 0)
            {
                continue;
            }

            var marker = raw[0];
            switch (marker)
            {
                case '+':
                    if (side == DiffSide.Right && newLine == line)
                    {
                        return true;
                    }
                    newLine++;
                    break;
                case '-':
                    if (side == DiffSide.Left && oldLine == line)
                    {
                        return true;
                    }
                    oldLine++;
                    break;
                case ' ':
                    oldLine++;
                    newLine++;
                    break;
                case '\\':
                    // "\ No newline at end of file" — does not advance counters.
                    break;
                default:
                    // diff metadata between hunks (e.g. "diff --git") — leave hunk scope.
                    inHunk = false;
                    break;
            }
        }
        return false;
    }
}
