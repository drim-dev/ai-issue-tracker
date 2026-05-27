using System.Text.Json;
using Api.Features.CodeReview.Agent.Anthropic;

namespace Api.Tests.Features.CodeReview;

/// <summary>
/// Plays back pre-recorded tool-use turns. The playbook is a JSON array of objects:
/// <code>
/// { "stop_reason": "tool_use" | "end_turn",
///   "content": [
///     { "type": "text",     "text": "..." },
///     { "type": "tool_use", "id": "...", "name": "...", "input": {...} }
///   ] }
/// </code>
/// </summary>
public sealed class FakeAnthropicClient : IAnthropicClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Queue<AnthropicResponse> _turns;
    public int CallCount { get; private set; }
    public List<AnthropicRequest> Requests { get; } = [];

    public FakeAnthropicClient(IEnumerable<AnthropicResponse> turns)
    {
        _turns = new Queue<AnthropicResponse>(turns);
    }

    public static FakeAnthropicClient FromPlaybook(string playbookPath)
    {
        var json = File.ReadAllText(playbookPath);
        using var doc = JsonDocument.Parse(json);

        var turns = new List<AnthropicResponse>();
        foreach (var turnEl in doc.RootElement.EnumerateArray())
        {
            var stopReason = turnEl.GetProperty("stop_reason").GetString()!;
            var blocks = new List<ContentBlock>();

            foreach (var blockEl in turnEl.GetProperty("content").EnumerateArray())
            {
                var type = blockEl.GetProperty("type").GetString();
                blocks.Add(type switch
                {
                    "text" => new TextBlock(blockEl.GetProperty("text").GetString() ?? ""),
                    "tool_use" => new ToolUseBlock(
                        blockEl.GetProperty("id").GetString() ?? "",
                        blockEl.GetProperty("name").GetString() ?? "",
                        blockEl.GetProperty("input").Clone()),
                    _ => throw new InvalidOperationException($"Unknown block type '{type}' in playbook."),
                });
            }
            turns.Add(new AnthropicResponse(stopReason, blocks));
        }

        return new FakeAnthropicClient(turns);
    }

    public Task<AnthropicResponse> CreateMessageAsync(AnthropicRequest request, CancellationToken ct)
    {
        CallCount++;
        Requests.Add(request);
        if (_turns.Count == 0)
        {
            // Mimic a stuck loop: keep returning tool_use so the agent eventually trips MaxTurns
            // (handy for the MaxTurns exceeded test case).
            return Task.FromResult(new AnthropicResponse("tool_use", [
                new ToolUseBlock($"stuck-{CallCount}", "fetch_pr_diff", JsonDocument.Parse("{}").RootElement),
            ]));
        }
        return Task.FromResult(_turns.Dequeue());
    }
}
