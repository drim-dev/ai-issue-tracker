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
/// Drives the Claude tool-use loop and parses the final JSON verdict. Promotes
/// cache hits by reusing the static system prompt and the tools schema (both
/// carry <c>cache_control: ephemeral</c>).
/// </summary>
public sealed class ClaudeCodeReviewAgent(
    IAnthropicClient anthropic,
    IToolDispatcher dispatcher,
    IOptions<ClaudeOptions> options,
    ILogger<ClaudeCodeReviewAgent> logger) : ICodeReviewAgent
{
    private static readonly JsonSerializerOptions ParseOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

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

            if (response.StopReason == "end_turn")
            {
                return ParseFinalReview(response.Content);
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
            $"Agent loop exceeded MaxTurns={opts.MaxTurns} without reaching end_turn.");
    }

    private static IReadOnlyList<AnthropicTool> BuildTools(IReadOnlyList<ToolDefinition> defs)
    {
        var tools = new List<AnthropicTool>(defs.Count);
        for (int i = 0; i < defs.Count; i++)
        {
            // cache_control on the last tool caches the whole tools array prefix.
            var cache = i == defs.Count - 1 ? new CacheControl("ephemeral") : null;
            tools.Add(new AnthropicTool(defs[i].Name, defs[i].Description, defs[i].InputSchema, cache));
        }
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
        sb.AppendLine("Start by collecting any docs you need, then produce the JSON review per the protocol.");

        return sb.ToString();
    }

    private static ReviewResult ParseFinalReview(IReadOnlyList<ContentBlock> blocks)
    {
        var text = string.Join("\n", blocks.OfType<TextBlock>().Select(b => b.Text)).Trim();
        if (text.Length == 0)
        {
            throw new ReviewAgentException("Final assistant message contained no text.");
        }

        // Allow accidental ```json fences.
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                text = text[(firstNewline + 1)..lastFence].Trim();
            }
        }

        FinalReviewDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FinalReviewDto>(text, ParseOptions)
                ?? throw new ReviewAgentException("Final review JSON deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ReviewAgentException($"Could not parse final review JSON: {ex.Message}");
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
