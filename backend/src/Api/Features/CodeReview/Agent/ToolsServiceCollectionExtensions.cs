using Api.Features.CodeReview.Agent.Tools;
using Api.Features.CodeReview.Options;

namespace Api.Features.CodeReview.Agent;

public static class ToolsServiceCollectionExtensions
{
    public static IServiceCollection AddCodeReviewTools(this IServiceCollection services)
    {
        services.AddOptions<WorkspaceOptions>()
            .BindConfiguration(WorkspaceOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddSingleton<ITool, ListDocsTool>();
        services.AddSingleton<ITool, ReadDocTool>();
        services.AddSingleton<ITool, FetchPrDiffTool>();
        services.AddSingleton<ITool, FetchChangedFileTool>();
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();

        return services;
    }
}
