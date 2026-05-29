using System.Text.Json;
using Api.Features.CodeReview.GitHub;

namespace Api.Tests.Features.CodeReview;

/// <summary>
/// Replays a recorded GitHub PR from <c>Fixtures/&lt;scenario&gt;/</c> so component tests
/// can run the handler end-to-end without hitting the real API.
/// </summary>
public sealed class FakeGitHubClient : IGitHubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PrMetadata _metadata;
    private readonly IReadOnlyList<PrFile> _files;
    private readonly string _diff;

    public List<(string Path, int Line, string Side, string Body)> PostedComments { get; } = [];
    public string? PostedEvent { get; private set; }

    public FakeGitHubClient(string fixtureDir)
    {
        var metaJson = File.ReadAllText(Path.Combine(fixtureDir, "metadata.json"));
        _metadata = JsonSerializer.Deserialize<PrMetadata>(metaJson, JsonOptions)
            ?? throw new InvalidOperationException("metadata.json was empty");
        var filesJson = File.ReadAllText(Path.Combine(fixtureDir, "files.json"));
        _files = JsonSerializer.Deserialize<PrFile[]>(filesJson, JsonOptions)
            ?? throw new InvalidOperationException("files.json was empty");
        _diff = File.ReadAllText(Path.Combine(fixtureDir, "diff.txt"));
    }

    public Task<PrMetadata> GetPullRequestAsync(PrCoordinates coords, CancellationToken ct) =>
        Task.FromResult(_metadata);

    public Task<IReadOnlyList<PrFile>> GetFilesAsync(PrCoordinates coords, CancellationToken ct) =>
        Task.FromResult(_files);

    public Task<string> GetUnifiedDiffAsync(PrCoordinates coords, CancellationToken ct) =>
        Task.FromResult(_diff);

    public Task<string> GetFileContentAsync(PrCoordinates coords, string path, string @ref, CancellationToken ct) =>
        Task.FromResult($"// fake content for {path}");

    public Task PostReviewAsync(
        PrCoordinates coords,
        string commitSha,
        string body,
        string @event,
        IReadOnlyList<GitHubReviewComment> comments,
        CancellationToken ct)
    {
        PostedEvent = @event;
        PostedComments.AddRange(comments.Select(c => (c.Path, c.Line, c.Side, c.Body)));
        return Task.CompletedTask;
    }
}
