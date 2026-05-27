using System.Text.Json;

namespace Api.Features.CodeReview.Agent;

/// <summary>
/// Schema of a tool exposed to Claude. <see cref="InputSchema"/> is the raw JSON Schema
/// object passed in the <c>tools</c> array of the Messages API request.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);

/// <summary>
/// Routes a tool call from the agent loop to the corresponding <see cref="ITool"/>.
/// Per design, unknown tool names and execution failures are returned as <c>is_error: true</c>
/// strings — the dispatcher does not throw.
/// </summary>
public interface IToolDispatcher
{
    IReadOnlyList<ToolDefinition> Definitions { get; }

    Task<string> ExecuteAsync(string name, JsonElement input, ReviewContext context, CancellationToken ct);
}

/// <summary>One tool exposed to the agent.</summary>
public interface ITool
{
    ToolDefinition Definition { get; }

    Task<string> ExecuteAsync(JsonElement input, ReviewContext context, CancellationToken ct);
}
