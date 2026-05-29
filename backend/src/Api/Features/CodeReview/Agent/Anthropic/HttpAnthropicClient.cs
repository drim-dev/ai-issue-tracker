using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Features.CodeReview.Agent.Anthropic;

/// <summary>HTTP-backed <see cref="IAnthropicClient"/> against <c>POST /v1/messages</c>.</summary>
public sealed class HttpAnthropicClient(HttpClient http) : IAnthropicClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<AnthropicResponse> CreateMessageAsync(AnthropicRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("v1/messages", request, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic API request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }
        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response from Anthropic API.");
    }
}
