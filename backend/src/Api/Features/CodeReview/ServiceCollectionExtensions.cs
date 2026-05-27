using Api.Features.CodeReview.Agent;
using Api.Features.CodeReview.GitHub;
using Api.Features.CodeReview.Options;
using Api.Features.CodeReview.Sinks;

namespace Api.Features.CodeReview;

/// <summary>
/// Wires up the CodeReview slice: Options, MediatR for this assembly, and the
/// component sub-graphs (GitHub client, agent, tools, sinks).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodeReviewFeature(this IServiceCollection services)
    {
        services.AddOptions<ClaudeOptions>()
            .BindConfiguration(ClaudeOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddOptions<GitHubOptions>()
            .BindConfiguration(GitHubOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddGitHubClient();
        services.AddCodeReviewTools();
        services.AddClaudeReviewAgent();
        services.AddReviewSinks();

        return services;
    }
}
