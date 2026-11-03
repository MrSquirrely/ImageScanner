using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageScanner.Plugins.Samples;

/// <summary>
/// Free, offline alternative to the built-in Claude tagger using a local Ollama vision model.
/// </summary>
internal sealed class OllamaTagProvider(IPlugin plugin, IPluginHost host) : ITagSuggestionProvider {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public string Id => "samples.ollama";
    public string DisplayName => "Ollama (local)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(host.GetSetting(plugin, SamplesPlugin.OllamaUrlSetting));

    public async Task<TagSuggestion> SuggestAsync(TagSuggestionRequest request, CancellationToken cancellationToken) {
        string baseUrl = host.GetSetting(plugin, SamplesPlugin.OllamaUrlSetting)!.TrimEnd('/');
        string model = host.GetSetting(plugin, SamplesPlugin.OllamaModelSetting) ?? "llava";
        string vocabulary = request.ExistingTags.Count > 0 ? string.Join(", ", request.ExistingTags.Take(150)) : "none";

        var body = new {
            model,
            stream = false,
            format = new {
                type = "object",
                properties = new {
                    tags = new { type = "array", items = new { type = "string" } },
                    category = new { type = "string" },
                    description = new { type = "string" }
                },
                required = new[] { "tags", "category", "description" }
            },
            messages = new[] {
                new {
                    role = "user",
                    content = $"List up to {request.MaxTags} short lower-case tags describing this image, a one-word category and a one-sentence description. " +
                              $"Prefer these existing tags when they fit: {vocabulary}. Answer as JSON.",
                    images = new[] { Convert.ToBase64String(request.ImageJpeg) }
                }
            }
        };

        using HttpResponseMessage response = await Http.PostAsJsonAsync($"{baseUrl}/api/chat", body, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        OllamaChatResponse? chat = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
        OllamaResult? result = chat?.Message?.Content is { Length: > 0 } json
                                   ? JsonSerializer.Deserialize<OllamaResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                   : null;

        return result == null
                   ? new TagSuggestion([])
                   : new TagSuggestion([.. result.Tags.Take(request.MaxTags)], result.Category, result.Description);
    }

    private sealed record OllamaChatResponse([property: JsonPropertyName("message")] OllamaMessage? Message);

    private sealed record OllamaMessage([property: JsonPropertyName("content")] string? Content);

    private sealed record OllamaResult(List<string> Tags, string? Category, string? Description);
}
