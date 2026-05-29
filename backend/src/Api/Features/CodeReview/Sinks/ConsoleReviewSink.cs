using System.Text;

namespace Api.Features.CodeReview.Sinks;

/// <summary>Renders a review to <see cref="Console.Out"/> as plain markdown.</summary>
public sealed class ConsoleReviewSink : IReviewSink
{
    public Task PublishAsync(ReviewResult result, ReviewContext context, CancellationToken ct)
    {
        Console.Out.Write(Render(result, context));
        return Task.CompletedTask;
    }

    public static string Render(ReviewResult result, ReviewContext context)
    {
        var sb = new StringBuilder();
        var coords = context.Pr.Coordinates;
        sb.AppendLine($"# Review: {coords.Owner}/{coords.Repo}#{coords.Number} — {context.Pr.Title}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine(string.IsNullOrWhiteSpace(result.Summary) ? "(no summary)" : result.Summary);
        sb.AppendLine();

        sb.AppendLine("## Inline Comments");
        if (result.Comments.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            var byFile = result.Comments
                .GroupBy(c => c.Path, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var group in byFile)
            {
                sb.AppendLine($"### {group.Key}");
                foreach (var c in group.OrderBy(c => c.Line))
                {
                    var side = c.Side == DiffSide.Left ? "LEFT" : "RIGHT";
                    sb.AppendLine($"- [{Categorize(c.Category)}] line {c.Line} ({side}): {c.Body}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Verdict");
        sb.AppendLine(result.Verdict switch
        {
            ReviewVerdict.Approve => "approve",
            ReviewVerdict.RequestChanges => "request_changes",
            _ => "comment",
        });

        return sb.ToString();
    }

    private static string Categorize(ReviewCategory category) => category switch
    {
        ReviewCategory.SpecMismatch => "spec_mismatch",
        ReviewCategory.Bug => "bug",
        ReviewCategory.Convention => "convention",
        ReviewCategory.Tests => "tests",
        _ => "unknown",
    };
}
