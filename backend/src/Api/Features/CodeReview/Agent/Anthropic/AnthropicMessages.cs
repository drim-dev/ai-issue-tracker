using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Features.CodeReview.Agent.Anthropic;

/// <summary>One block of a system prompt. <see cref="CacheControl"/> opts the block in for the 5-minute ephemeral cache.</summary>
public sealed record SystemBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl);

public sealed record CacheControl([property: JsonPropertyName("type")] string Type);

/// <summary>One tool schema in the Messages API request.</summary>
public sealed record AnthropicTool(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] JsonElement InputSchema,
    [property: JsonPropertyName("cache_control")] CacheControl? CacheControl);

public sealed record AnthropicMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
public abstract record ContentBlock;

public sealed record TextBlock([property: JsonPropertyName("text")] string Text) : ContentBlock;

public sealed record ToolUseBlock(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("input")] JsonElement Input) : ContentBlock;

public sealed record ToolResultBlock(
    [property: JsonPropertyName("tool_use_id")] string ToolUseId,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("is_error")] bool? IsError = null) : ContentBlock;

public sealed record AnthropicRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] IReadOnlyList<SystemBlock> System,
    [property: JsonPropertyName("tools")] IReadOnlyList<AnthropicTool> Tools,
    [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

public sealed record AnthropicResponse(
    [property: JsonPropertyName("stop_reason")] string StopReason,
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content);

public interface IAnthropicClient
{
    Task<AnthropicResponse> CreateMessageAsync(AnthropicRequest request, CancellationToken ct);
}
