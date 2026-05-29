namespace Api.Features.CodeReview.Sinks;

public static class SinksServiceCollectionExtensions
{
    public static IServiceCollection AddReviewSinks(this IServiceCollection services)
    {
        services.AddSingleton<ConsoleReviewSink>();
        services.AddSingleton<GitHubReviewSink>();
        return services;
    }
}
