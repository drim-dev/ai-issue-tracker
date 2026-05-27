using System.Text.Json;
using Api.Features.CodeReview.GitHub;

namespace Api.Features.CodeReview.Agent.Tools;

/// <summary>
/// Returns the head-sha contents of a file changed in the PR. Refuses paths that are not
/// part of the PR's changed-files set so the agent cannot read arbitrary repo files.
/// </summary>
public sealed class FetchChangedFileTool(IGitHubClient github) : ITool
{
    public ToolDefinition Definition { get; } = new(
        Name: "fetch_changed_file",
        Description: "Reads the full head-sha contents of a file changed in the PR. The path must appear in the PR's changed-files list.",
        InputSchema: ToolSchemas.SingleStringProperty("path", "Repository-relative path of the file to read."));

    public async Task<string> ExecuteAsync(JsonElement input, ReviewContext context, CancellationToken ct)
    {
        if (!input.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
        {
            return JsonSerializer.Serialize(new { error = "input.path is required (string)." });
        }

        var path = pathProp.GetString()!;
        var changed = context.ChangedFiles.FirstOrDefault(f =>
            string.Equals(f.Path, path, StringComparison.Ordinal));
        if (changed is null)
        {
            return JsonSerializer.Serialize(new { error = $"Path '{path}' is not in the PR's changed files." });
        }

        var content = await github.GetFileContentAsync(context.Pr.Coordinates, path, context.Pr.HeadSha, ct);
        return JsonSerializer.Serialize(new { path, content });
    }
}
