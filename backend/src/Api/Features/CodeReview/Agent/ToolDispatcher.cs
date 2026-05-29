using System.Text.Json;

namespace Api.Features.CodeReview.Agent;

/// <summary>
/// Collects every <see cref="ITool"/> from DI, exposes their <see cref="ToolDefinition"/>s,
/// and routes tool calls by name. Unknown names and execution failures come back as
/// <c>{ "error": "..." }</c> strings so the agent loop never throws on a bad tool call.
/// </summary>
public sealed class ToolDispatcher : IToolDispatcher
{
    private readonly Dictionary<string, ITool> _byName;

    public ToolDispatcher(IEnumerable<ITool> tools)
    {
        _byName = tools.ToDictionary(t => t.Definition.Name, StringComparer.Ordinal);
        Definitions = _byName.Values.Select(t => t.Definition).ToArray();
    }

    public IReadOnlyList<ToolDefinition> Definitions { get; }

    public async Task<string> ExecuteAsync(string name, JsonElement input, ReviewContext context, CancellationToken ct)
    {
        if (!_byName.TryGetValue(name, out var tool))
        {
            return JsonSerializer.Serialize(new { error = $"Unknown tool '{name}'." });
        }

        try
        {
            return await tool.ExecuteAsync(input, context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Tool '{name}' threw: {ex.Message}" });
        }
    }
}
