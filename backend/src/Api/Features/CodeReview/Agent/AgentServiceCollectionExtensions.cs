using Api.Features.CodeReview.Agent.Anthropic;
using Api.Features.CodeReview.Options;
using Microsoft.Extensions.Options;

namespace Api.Features.CodeReview.Agent;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeReviewAgent(this IServiceCollection services)
    {
        services.AddHttpClient<IAnthropicClient, HttpAnthropicClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value;
            http.BaseAddress = new Uri("https://api.anthropic.com/");
            http.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            http.Timeout = TimeSpan.FromMinutes(5);
        })
        .AddStandardResilienceHandler();

        services.AddSingleton<ICodeReviewAgent, ClaudeCodeReviewAgent>();
        return services;
    }
}
