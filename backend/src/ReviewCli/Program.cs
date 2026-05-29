using System.CommandLine;
using System.Reflection;
using Api.Common.Exceptions;
using Api.Common.Validation;
using Api.Features.CodeReview;
using Api.Features.CodeReview.Options;
using Api.Features.CodeReview.Sinks;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var prUrlArgument = new Argument<string>("pr-url")
{
    Description = "URL of the GitHub pull request to review.",
};

var postOption = new Option<bool>("--post")
{
    Description = "Publish the review back to GitHub. By default, only stdout is updated.",
};
var verboseOption = new Option<bool>("--verbose")
{
    Description = "Enable Debug logging for the agent (turn-by-turn trace).",
};
var maxTurnsOption = new Option<int?>("--max-turns")
{
    Description = "Override ClaudeOptions.MaxTurns for this run.",
};

var reviewCommand = new Command("review", "Review a pull request");
reviewCommand.Arguments.Add(prUrlArgument);
reviewCommand.Options.Add(postOption);
reviewCommand.Options.Add(verboseOption);
reviewCommand.Options.Add(maxTurnsOption);

reviewCommand.SetAction(async (parseResult, ct) =>
{
    var prUrl = parseResult.GetValue(prUrlArgument)!;
    var post = parseResult.GetValue(postOption);
    var verbose = parseResult.GetValue(verboseOption);
    var maxTurnsOverride = parseResult.GetValue(maxTurnsOption);

    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
    builder.Configuration.AddEnvironmentVariables();

    if (maxTurnsOverride.HasValue)
    {
        builder.Configuration["Claude:MaxTurns"] = maxTurnsOverride.Value.ToString();
    }

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    builder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
    if (!verbose)
    {
        // Quiet HttpClient noise during normal runs.
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
    }

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssembly(typeof(ReviewPullRequest).Assembly));
    builder.Services.AddValidatorsFromAssembly(typeof(ReviewPullRequest).Assembly);
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    builder.Services.AddCodeReviewFeature();

    using var host = builder.Build();

    var sender = host.Services.GetRequiredService<ISender>();
    var consoleSink = host.Services.GetRequiredService<ConsoleReviewSink>();
    var githubSink = host.Services.GetRequiredService<GitHubReviewSink>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    try
    {
        var result = await sender.Send(new ReviewPullRequest.Request(prUrl, post), ct);

        // We need the ReviewContext for sink rendering; rebuild a minimal one from the URL.
        // The handler does not expose context; render via a simplified path using metadata
        // fetched here.
        var coords = Api.Features.CodeReview.GitHub.PrUrlParser.Parse(prUrl);
        var github = host.Services.GetRequiredService<Api.Features.CodeReview.GitHub.IGitHubClient>();
        var metadata = await github.GetPullRequestAsync(coords, ct);
        var context = new ReviewContext(metadata, [], "", []);

        await consoleSink.PublishAsync(result, context, ct);

        if (post)
        {
            await githubSink.PublishAsync(result, context, ct);
            logger.LogInformation("Review posted to {Url}", prUrl);
        }

        return result.Verdict switch
        {
            ReviewVerdict.RequestChanges => 1,
            _ => 0,
        };
    }
    catch (DomainException ex)
    {
        logger.LogError(ex, "Review failed: {Code}", ex.ErrorCode);
        return 2;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error");
        return 2;
    }
});

var root = new RootCommand("ai-issue-tracker AI pull request reviewer");
root.Subcommands.Add(reviewCommand);

return await root.Parse(args).InvokeAsync();
