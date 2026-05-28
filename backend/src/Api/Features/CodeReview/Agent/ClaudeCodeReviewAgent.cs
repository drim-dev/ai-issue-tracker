using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Features.CodeReview.Agent.Anthropic;
using Api.Features.CodeReview.Agent.Prompts;
using Api.Features.CodeReview.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Features.CodeReview.Agent;

/// <summary>
/// Drives the Claude tool-use loop. The final verdict comes back as a forced call to the
/// <c>submit_review</c> tool whose <c>input_schema</c> is the review shape — Claude is
/// guaranteed to emit a structured object, so no text/JSON parsing is needed.
/// </summary>
public sealed class ClaudeCodeReviewAgent(
    IAnthropicClient anthropic,
    IToolDispatcher dispatcher,
    IOptions<ClaudeOptions> options,
    ILogger<ClaudeCodeReviewAgent> logger) : ICodeReviewAgent
{
    private const string SubmitReviewToolName = "submit_review";

    private static readonly JsonSerializerOptions ParseOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static readonly JsonElement SubmitReviewSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "summary": {
              "type": "string",
              "description": "1–3 short paragraphs describing the overall findings."
            },
            "verdict": {
              "type": "string",
              "enum": ["approve", "comment", "request_changes"]
            },
            "comments": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "path": { "type": "string" },
                  "line": { "type": "integer" },
                  "side": { "type": "string", "enum": ["LEFT", "RIGHT"] },
                  "category": {
                    "type": "string",
                    "enum": ["spec_mismatch", "bug", "convention", "tests"]
                  },
                  "body": { "type": "string" }
                },
                "required": ["path", "line", "side", "category", "body"]
              }
            }
          },
          "required": ["summary", "verdict", "comments"]
        }
        """).RootElement;

    public async Task<ReviewResult> ReviewAsync(ReviewContext context, CancellationToken ct)
    {
        var opts = options.Value;
        var tools = BuildTools(dispatcher.Definitions);

        var messages = new List<AnthropicMessage>
        {
            new("user", [new TextBlock(BuildInitialUserMessage(context))]),
        };

        for (int turn = 0; turn < opts.MaxTurns; turn++)
        {
            var request = new AnthropicRequest(
                Model: opts.Model,
                MaxTokens: opts.MaxTokens,
                System: SystemPrompt.Blocks,
                Tools: tools,
                Messages: messages);

            logger.LogDebug("Agent turn {Turn}: sending request with {Messages} messages", turn, messages.Count);
            var response = await anthropic.CreateMessageAsync(request, ct);
            logger.LogDebug("Agent turn {Turn}: stop_reason={StopReason}, blocks={Blocks}", turn, response.StopReason, response.Content.Count);

            messages.Add(new AnthropicMessage("assistant", response.Content));

            var submit = response.Content
                .OfType<ToolUseBlock>()
                .FirstOrDefault(b => b.Name == SubmitReviewToolName);
            if (submit is not null)
            {
                return ParseSubmittedReview(submit);
            }

            if (response.StopReason == "end_turn")
            {
                throw new ReviewAgentException(
                    "Agent stopped without calling submit_review.");
            }

            if (response.StopReason != "tool_use")
            {
                throw new ReviewAgentException(
                    $"Unexpected stop_reason '{response.StopReason}' from Claude on turn {turn}.");
            }

            var toolResults = new List<ContentBlock>();
            foreach (var use in response.Content.OfType<ToolUseBlock>())
            {
                logger.LogDebug("Agent turn {Turn}: tool_use {Tool} (id={Id})", turn, use.Name, use.Id);
                var output = await dispatcher.ExecuteAsync(use.Name, use.Input, context, ct);
                toolResults.Add(new ToolResultBlock(use.Id, output));
            }
            messages.Add(new AnthropicMessage("user", toolResults));
        }

        throw new ReviewAgentException(
            $"Agent loop exceeded MaxTurns={opts.MaxTurns} without calling submit_review.");
    }

    private static IReadOnlyList<AnthropicTool> BuildTools(IReadOnlyList<ToolDefinition> defs)
    {
        var tools = new List<AnthropicTool>(defs.Count + 1);
        for (int i = 0; i < defs.Count; i++)
        {
            tools.Add(new AnthropicTool(defs[i].Name, defs[i].Description, defs[i].InputSchema, CacheControl: null));
        }
        // submit_review terminates the loop; cache_control on the last tool caches the whole prefix.
        tools.Add(new AnthropicTool(
            SubmitReviewToolName,
            "Submit the final pull request review. Call this exactly once, as your last action.",
            SubmitReviewSchema,
            new CacheControl("ephemeral")));
        return tools;
    }

    private static string BuildInitialUserMessage(ReviewContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Pull request: https://github.com/{ctx.Pr.Coordinates.Owner}/{ctx.Pr.Coordinates.Repo}/pull/{ctx.Pr.Coordinates.Number}");
        sb.AppendLine($"Title: {ctx.Pr.Title}");
        sb.AppendLine($"Author: {ctx.Pr.Author}");
        sb.AppendLine($"Base: {ctx.Pr.BaseRef} ({ctx.Pr.BaseSha})  Head: {ctx.Pr.HeadRef} ({ctx.Pr.HeadSha})");
        sb.AppendLine();
        sb.AppendLine("PR body:");
        sb.AppendLine(string.IsNullOrWhiteSpace(ctx.Pr.Body) ? "(empty)" : ctx.Pr.Body);
        sb.AppendLine();

        if (ctx.DocPathsFromPrBody.Count > 0)
        {
            sb.AppendLine("Linked docs detected in the PR body:");
            foreach (var p in ctx.DocPathsFromPrBody) sb.AppendLine($"  - {p}");
        }
        else
        {
            sb.AppendLine("No design/plan/spec paths were detected in the PR body — call `list_docs` if you need context.");
        }
        sb.AppendLine();

        sb.AppendLine("Changed files:");
        foreach (var f in ctx.ChangedFiles)
        {
            sb.AppendLine($"  - {f.Path} ({f.Status}, +{f.Additions}/-{f.Deletions})");
        }
        sb.AppendLine();
        sb.AppendLine("Unified diff:");
        sb.AppendLine("```diff");
        sb.AppendLine(ctx.UnifiedDiff);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Start by collecting any docs you need, then call `submit_review` with your final verdict.");

        return sb.ToString();
    }

    private static ReviewResult ParseSubmittedReview(ToolUseBlock submit)
    {
        FinalReviewDto dto;
        try
        {
            dto = submit.Input.Deserialize<FinalReviewDto>(ParseOptions)
                ?? throw new ReviewAgentException("submit_review input deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ReviewAgentException($"Could not bind submit_review input: {ex.Message}");
        }

        var verdict = dto.Verdict switch
        {
            "approve" => ReviewVerdict.Approve,
            "comment" => ReviewVerdict.Comment,
            "request_changes" => ReviewVerdict.RequestChanges,
            _ => throw new ReviewAgentException($"Unknown verdict '{dto.Verdict}'."),
        };

        var comments = (dto.Comments ?? []).Select(c => new LineComment(
            Path: c.Path ?? throw new ReviewAgentException("comment.path is required."),
            Line: c.Line,
            Side: c.Side?.Equals("LEFT", StringComparison.OrdinalIgnoreCase) == true ? DiffSide.Left : DiffSide.Right,
            Body: c.Body ?? "",
            Category: c.Category switch
            {
                "spec_mismatch" => ReviewCategory.SpecMismatch,
                "bug" => ReviewCategory.Bug,
                "convention" => ReviewCategory.Convention,
                "tests" => ReviewCategory.Tests,
                _ => throw new ReviewAgentException($"Unknown category '{c.Category}'."),
            })).ToArray();

        return new ReviewResult(dto.Summary ?? "", comments, verdict);
    }

    private sealed record FinalReviewDto(
        string? Summary,
        string? Verdict,
        FinalCommentDto[]? Comments);

    private sealed record FinalCommentDto(
        string? Path,
        int Line,
        string? Side,
        string? Category,
        string? Body);
}
