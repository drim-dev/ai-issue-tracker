using System.Net.Http.Headers;
using Api.Features.CodeReview.Options;
using Microsoft.Extensions.Options;

namespace Api.Features.CodeReview.GitHub;

public static class GitHubServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubClient(this IServiceCollection services)
    {
        services.AddHttpClient<IGitHubClient, GitHubClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<GitHubOptions>>().Value;
            http.BaseAddress = new Uri("https://api.github.com/");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.Token);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        })
        .AddStandardResilienceHandler();

        return services;
    }
}
