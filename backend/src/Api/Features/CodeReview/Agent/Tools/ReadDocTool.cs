using System.Text.Json;
using Api.Features.CodeReview.Options;
using Microsoft.Extensions.Options;

namespace Api.Features.CodeReview.Agent.Tools;

/// <summary>
/// Reads a single doc file under <c>docs/**</c>. Path traversal outside the docs root
/// is refused — the agent should never read source code through this tool.
/// </summary>
public sealed class ReadDocTool(IOptions<WorkspaceOptions> options) : ITool
{
    public ToolDefinition Definition { get; } = new(
        Name: "read_doc",
        Description: "Reads the contents of a markdown file under docs/**. The path must start with 'docs/' and lie inside that directory.",
        InputSchema: ToolSchemas.SingleStringProperty("path", "Path to the doc file, e.g. 'docs/designs/2026-05-27-ai-pr-reviewer.md'."));

    public async Task<string> ExecuteAsync(JsonElement input, ReviewContext context, CancellationToken ct)
    {
        if (!input.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
        {
            return ToolError("input.path is required (string).");
        }

        var requested = pathProp.GetString()!;
        var root = Path.GetFullPath(options.Value.DocsRoot);
        var rootParent = Path.GetDirectoryName(root)!;

        // Reject relative escapes early.
        if (requested.Contains("..", StringComparison.Ordinal))
        {
            return ToolError("'..' is not allowed in the path.");
        }

        var full = Path.GetFullPath(Path.Combine(rootParent, requested));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !full.StartsWith(root, StringComparison.Ordinal))
        {
            return ToolError($"Path '{requested}' is outside the docs root.");
        }

        if (!File.Exists(full))
        {
            return ToolError($"File '{requested}' was not found.");
        }

        var content = await File.ReadAllTextAsync(full, ct);
        return JsonSerializer.Serialize(new { path = requested, content });
    }

    private static string ToolError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
