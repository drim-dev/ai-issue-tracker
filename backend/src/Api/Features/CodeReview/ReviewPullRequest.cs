using Api.Features.CodeReview.Agent;
using Api.Features.CodeReview.GitHub;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Api.Features.CodeReview;

/// <summary>
/// Orchestrates an AI review of a GitHub pull request: fetches PR context, runs the agent,
/// then filters out inline comments that do not actually land inside the diff.
/// Sinks (console, GitHub) are invoked by the caller after the handler returns.
/// </summary>
public static class ReviewPullRequest
{
    public sealed record Request(string PrUrl, bool PostToGitHub) : IRequest<ReviewResult>;

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.PrUrl)
                .NotEmpty().WithMessage("PR URL is required")
                .WithErrorCode("code_review:pr_url:required");
        }
    }

    public class RequestHandler(
        IGitHubClient github,
        ICodeReviewAgent agent,
        ILogger<RequestHandler> logger) : IRequestHandler<Request, ReviewResult>
    {
        public async Task<ReviewResult> Handle(Request request, CancellationToken ct)
        {
            var coords = PrUrlParser.Parse(request.PrUrl);
            logger.LogInformation("Reviewing {Owner}/{Repo}#{Number}", coords.Owner, coords.Repo, coords.Number);

            var metadataTask = github.GetPullRequestAsync(coords, ct);
            var filesTask = github.GetFilesAsync(coords, ct);
            var diffTask = github.GetUnifiedDiffAsync(coords, ct);
            await Task.WhenAll(metadataTask, filesTask, diffTask);

            var metadata = await metadataTask;
            var files = await filesTask;
            var diff = await diffTask;

            var docPaths = ResolveDocPaths(metadata, files);

            var context = new ReviewContext(metadata, files, diff, docPaths);
            var review = await agent.ReviewAsync(context, ct);

            return FilterInvalidComments(review, files, logger);
        }

        private static IReadOnlyList<string> ResolveDocPaths(PrMetadata metadata, IReadOnlyList<PrFile> files)
        {
            var fromBody = DocPathExtractor.ExtractFromBody(metadata.Body);
            if (fromBody.Count > 0)
            {
                return fromBody;
            }

            // Fallback: scan changed doc files themselves; nothing else to infer from at handler level
            // (the `list_docs` tool gives the agent a richer fallback at runtime).
            var changed = files
                .Where(f => f.Path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                            && f.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Path)
                .ToArray();
            return changed;
        }

        private static ReviewResult FilterInvalidComments(
            ReviewResult review,
            IReadOnlyList<PrFile> files,
            ILogger logger)
        {
            var byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var kept = new List<LineComment>(review.Comments.Count);
            var dropped = new List<string>();

            foreach (var c in review.Comments)
            {
                if (!byPath.TryGetValue(c.Path, out var file))
                {
                    dropped.Add($"{c.Path}:{c.Line} (file not in diff)");
                    continue;
                }
                if (!DiffParser.IsLineInDiff(file.Patch, c.Line, c.Side))
                {
                    dropped.Add($"{c.Path}:{c.Line} {(c.Side == DiffSide.Left ? "LEFT" : "RIGHT")} (line not in diff)");
                    continue;
                }
                kept.Add(c);
            }

            if (dropped.Count == 0)
            {
                return review;
            }

            logger.LogWarning("Dropped {Count} inline comment(s) that fell outside the diff.", dropped.Count);
            var warning = "\n\n_Dropped " + dropped.Count + " inline comment(s) outside the diff: "
                + string.Join("; ", dropped) + "._";
            return review with
            {
                Summary = review.Summary + warning,
                Comments = kept,
            };
        }
    }
}
