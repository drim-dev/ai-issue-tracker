using System.Text.Json;

namespace Api.Features.CodeReview.Agent.Tools;

/// <summary>Small helpers for the JSON-Schema fragments embedded in tool definitions.</summary>
internal static class ToolSchemas
{
    public static JsonElement EmptyObject { get; } =
        Parse("""{ "type": "object", "properties": {}, "additionalProperties": false }""");

    public static JsonElement SingleStringProperty(string name, string description) =>
        Parse($$"""
            {
              "type": "object",
              "properties": { "{{name}}": { "type": "string", "description": {{JsonSerializer.Serialize(description)}} } },
              "required": ["{{name}}"],
              "additionalProperties": false
            }
            """);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
