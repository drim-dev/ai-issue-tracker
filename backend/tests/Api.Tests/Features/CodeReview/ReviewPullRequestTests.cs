using Api.Features.CodeReview;
using Api.Features.CodeReview.Agent;
using Api.Features.CodeReview.Agent.Anthropic;
using Api.Features.CodeReview.Agent.Tools;
using Api.Features.CodeReview.GitHub;
using Api.Features.CodeReview.Options;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using static Api.Tests.Fixtures.TestCancellation;

namespace Api.Tests.Features.CodeReview;

/// <summary>
/// Component tests for the <c>ReviewPullRequest</c> slice. The two outbound dependencies —
/// GitHub and Anthropic — are faked; everything else runs as in production (handler,
/// MediatR, dispatcher, tools, sinks).
/// </summary>
public class ReviewPullRequestTests
{
    private const string PrUrl = "https://github.com/anthropics/ai-issue-tracker/pull/1";

    private static string FixtureDir(string scenario) =>
        Path.Combine(Path.GetDirectoryName(typeof(ReviewPullRequestTests).Assembly.Location)!,
            "Features", "CodeReview", "Fixtures", scenario);

    private static (ISender Sender, FakeAnthropicClient Anthropic, FakeGitHubClient Github)
        BuildSut(string scenario, string playbookFile = "agent-playbook.json", int? maxTurns = null)
    {
        var dir = FixtureDir(scenario);
        var github = new FakeGitHubClient(dir);
        var anthropic = FakeAnthropicClient.FromPlaybook(Path.Combine(dir, playbookFile));

        var services = new ServiceCollection();
        services.AddSingleton<IGitHubClient>(github);
        services.AddSingleton<IAnthropicClient>(anthropic);

        services.AddSingleton<IOptions<ClaudeOptions>>(
            Microsoft.Extensions.Options.Options.Create(new ClaudeOptions
            {
                ApiKey = "test", Model = "claude-opus-4-7", MaxTurns = maxTurns ?? 20,
            }));
        services.AddSingleton<IOptions<WorkspaceOptions>>(
            Microsoft.Extensions.Options.Options.Create(new WorkspaceOptions { DocsRoot = dir }));

        services.AddSingleton<ITool, FetchPrDiffTool>();
        services.AddSingleton<ITool, FetchChangedFileTool>();
        services.AddSingleton<ITool, ListDocsTool>();
        services.AddSingleton<ITool, ReadDocTool>();
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();
        services.AddSingleton<ICodeReviewAgent, ClaudeCodeReviewAgent>();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ReviewPullRequest).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ISender>(), anthropic, github);
    }

    [Fact]
    public async Task Happy_path_returns_comment_verdict()
    {
        var (sender, _, _) = BuildSut("pr-happy");

        var result = await sender.Send(new ReviewPullRequest.Request(PrUrl, PostToGitHub: false), CreateCancellationToken());

        result.Verdict.Should().Be(ReviewVerdict.Comment);
        result.Comments.Should().HaveCount(1);
        result.Comments[0].Path.Should().Be("backend/src/Api/Features/Auth/Login.cs");
        result.Comments[0].Line.Should().Be(4);
        result.Comments[0].Category.Should().Be(ReviewCategory.Convention);
        result.Summary.Should().NotContain("Dropped");
    }

    [Fact]
    public async Task Spec_mismatch_returns_request_changes()
    {
        var (sender, _, _) = BuildSut("pr-spec-mismatch");

        var result = await sender.Send(new ReviewPullRequest.Request(PrUrl, PostToGitHub: false), CreateCancellationToken());

        result.Verdict.Should().Be(ReviewVerdict.RequestChanges);
        result.Comments.Should().ContainSingle()
            .Which.Category.Should().Be(ReviewCategory.SpecMismatch);
    }

    [Fact]
    public async Task Invalid_line_comments_are_dropped_with_warning()
    {
        var (sender, _, _) = BuildSut("pr-happy", "agent-playbook-invalid-line.json");

        var result = await sender.Send(new ReviewPullRequest.Request(PrUrl, PostToGitHub: false), CreateCancellationToken());

        result.Comments.Should().ContainSingle("only the valid line-4 comment should remain")
            .Which.Body.Should().StartWith("Valid:");
        result.Summary.Should().Contain("Dropped 2 inline comment(s)");
    }

    [Fact]
    public async Task Max_turns_exceeded_throws()
    {
        // Empty playbook → FakeAnthropicClient keeps returning tool_use, agent never sees end_turn.
        var (sender, _, _) = BuildSut("pr-happy", "empty-playbook.json", maxTurns: 3);

        var act = async () => await sender.Send(
            new ReviewPullRequest.Request(PrUrl, PostToGitHub: false),
            CreateCancellationToken());

        (await act.Should().ThrowAsync<ReviewAgentException>())
            .Which.Message.Should().Contain("MaxTurns=3");
    }
}
