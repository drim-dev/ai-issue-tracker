using System.Text.Json;
using Api.Features.CodeReview.Options;
using Microsoft.Extensions.Options;

namespace Api.Features.CodeReview.Agent.Tools;

/// <summary>
/// Lists doc files under <c>docs/{designs,plans,specs}</c> so the agent can discover
/// design context when the PR body does not link to it directly.
/// </summary>
public sealed class ListDocsTool(IOptions<WorkspaceOptions> options) : ITool
{
    private static readonly string[] SubDirs = ["designs", "plans", "specs"];

    public ToolDefinition Definition { get; } = new(
        Name: "list_docs",
        Description: "Lists markdown files under docs/{designs,plans,specs}. Returns relative paths. Use this when the PR body does not link to a design/plan/spec.",
        InputSchema: ToolSchemas.EmptyObject);

    public Task<string> ExecuteAsync(JsonElement input, ReviewContext context, CancellationToken ct)
    {
        var root = Path.GetFullPath(options.Value.DocsRoot);
        if (!Directory.Exists(root))
        {
            return Task.FromResult(JsonSerializer.Serialize(new { paths = Array.Empty<string>() }));
        }

        var paths = new List<string>();
        foreach (var sub in SubDirs)
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                paths.Add(Path.GetRelativePath(Path.GetDirectoryName(root)!, file).Replace('\\', '/'));
            }
        }

        paths.Sort(StringComparer.Ordinal);
        return Task.FromResult(JsonSerializer.Serialize(new { paths }));
    }
}
