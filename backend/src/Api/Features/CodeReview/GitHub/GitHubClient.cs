using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Features.CodeReview.Options;
using Microsoft.Extensions.Options;

namespace Api.Features.CodeReview.GitHub;

/// <summary>
/// Thin REST wrapper over the GitHub API. Configured via <see cref="GitHubOptions"/>
/// and <c>IHttpClientFactory</c> with the standard resilience handler.
/// </summary>
public sealed class GitHubClient(HttpClient http) : IGitHubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PrMetadata> GetPullRequestAsync(PrCoordinates coords, CancellationToken ct)
    {
        var path = $"repos/{coords.Owner}/{coords.Repo}/pulls/{coords.Number}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, ct);
        var dto = await response.Content.ReadFromJsonAsync<PullDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("GitHub returned an empty PR payload.");

        return new PrMetadata(
            Coordinates: coords,
            Title: dto.Title ?? "",
            Body: dto.Body ?? "",
            HeadSha: dto.Head?.Sha ?? "",
            BaseSha: dto.Base?.Sha ?? "",
            HeadRef: dto.Head?.Ref ?? "",
            BaseRef: dto.Base?.Ref ?? "",
            Author: dto.User?.Login ?? "");
    }

    public async Task<IReadOnlyList<PrFile>> GetFilesAsync(PrCoordinates coords, CancellationToken ct)
    {
        var files = new List<PrFile>();
        int page = 1;
        while (true)
        {
            var path = $"repos/{coords.Owner}/{coords.Repo}/pulls/{coords.Number}/files?per_page=100&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            using var response = await SendAsync(request, ct);
            var dtos = await response.Content.ReadFromJsonAsync<FileDto[]>(JsonOptions, ct) ?? [];

            foreach (var f in dtos)
            {
                files.Add(new PrFile(
                    Path: f.Filename ?? "",
                    Status: f.Status ?? "",
                    Additions: f.Additions,
                    Deletions: f.Deletions,
                    Patch: f.Patch));
            }

            if (dtos.Length < 100) break;
            page++;
        }
        return files;
    }

    public async Task<string> GetUnifiedDiffAsync(PrCoordinates coords, CancellationToken ct)
    {
        var path = $"repos/{coords.Owner}/{coords.Repo}/pulls/{coords.Number}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
        using var response = await SendAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<string> GetFileContentAsync(PrCoordinates coords, string path, string @ref, CancellationToken ct)
    {
        var url = $"repos/{coords.Owner}/{coords.Repo}/contents/{Uri.EscapeDataString(path).Replace("%2F", "/")}?ref={Uri.EscapeDataString(@ref)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
        using var response = await SendAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task PostReviewAsync(
        PrCoordinates coords,
        string commitSha,
        string body,
        string @event,
        IReadOnlyList<GitHubReviewComment> comments,
        CancellationToken ct)
    {
        var path = $"repos/{coords.Owner}/{coords.Repo}/pulls/{coords.Number}/reviews";
        var payload = new
        {
            commit_id = commitSha,
            body,
            @event,
            comments = comments.Select(c => new { path = c.Path, line = c.Line, side = c.Side, body = c.Body }),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        using var response = await SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Rate-limit: 403 with X-RateLimit-Remaining: 0 (per GitHub docs).
            if (response.StatusCode == HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                && values.FirstOrDefault() == "0")
            {
                var resetAt = response.Headers.TryGetValues("X-RateLimit-Reset", out var rv) ? rv.FirstOrDefault() : null;
                response.Dispose();
                throw new GitHubRateLimitException(
                    $"GitHub rate limit exhausted. Resets at unix:{resetAt ?? "?"}.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new HttpRequestException(
                $"GitHub {request.Method} {request.RequestUri} failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }
        return response;
    }

    private sealed record PullDto(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("head")] RefDto? Head,
        [property: JsonPropertyName("base")] RefDto? Base,
        [property: JsonPropertyName("user")] UserDto? User);

    private sealed record RefDto(
        [property: JsonPropertyName("sha")] string? Sha,
        [property: JsonPropertyName("ref")] string? Ref);

    private sealed record UserDto([property: JsonPropertyName("login")] string? Login);

    private sealed record FileDto(
        [property: JsonPropertyName("filename")] string? Filename,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("additions")] int Additions,
        [property: JsonPropertyName("deletions")] int Deletions,
        [property: JsonPropertyName("patch")] string? Patch);
}
