using System.Text.Json;

namespace Api.Features.CodeReview.Agent.Tools;

/// <summary>
/// Returns the PR's unified diff. The handler already prefetches it into <see cref="ReviewContext"/>,
/// so this tool just hands it back — no GitHub round-trip per call.
/// </summary>
public sealed class FetchPrDiffTool : ITool
{
    public ToolDefinition Definition { get; } = new(
        Name: "fetch_pr_diff",
        Description: "Returns the unified diff of the pull request being reviewed.",
        InputSchema: ToolSchemas.EmptyObject);

    public Task<string> ExecuteAsync(JsonElement input, ReviewContext context, CancellationToken ct)
    {
        return Task.FromResult(JsonSerializer.Serialize(new { diff = context.UnifiedDiff }));
    }
}
