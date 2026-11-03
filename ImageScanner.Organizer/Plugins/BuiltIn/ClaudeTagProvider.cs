using System.Text.Json;

using Anthropic;
using Anthropic.Models.Beta.Messages;

using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Plugins.BuiltIn;

/// <summary>
/// Suggests tags, a category and a one-line description for an image using Claude's vision input
/// and structured JSON output.
/// </summary>
internal sealed class ClaudeTagProvider(BuiltInPlugin plugin, IPluginHost host) : ITagSuggestionProvider {
    public const string ProviderId = "builtin.claude";

    private const string SystemPrompt =
        """
        You tag images for a personal image library so they can be found later by search.
        Return short, lower-case tags (1-3 words each) describing the subject, setting, style, colors, mood
        and notable objects or text. Prefer reusing tags from the library's existing vocabulary when they fit,
        and only invent new tags when nothing existing describes the image. Choose a category only if one of
        the existing categories clearly fits, or propose a short new category name when none exist.
        If the image is a comic, manga or illustration page, include that as a tag.
        """;

    private static readonly Dictionary<string, JsonElement> ResponseSchema = new() {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new {
            tags = new { type = "array", items = new { type = "string" } },
            category = new { type = "string" },
            description = new { type = "string" }
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "tags", "category", "description" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
    };

    private AnthropicClient? client;
    private string? clientKey;

    public string Id => ProviderId;
    public string DisplayName => "Claude (Anthropic)";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    // Falls back to the standard environment variable so developers don't have to paste a key
    private string? ApiKey => host.GetSetting(plugin, BuiltInPlugin.ApiKeySetting) is { Length: > 0 } key
                                  ? key
                                  : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public async Task<TagSuggestion> SuggestAsync(TagSuggestionRequest request, CancellationToken cancellationToken) {
        string apiKey = ApiKey ?? throw new InvalidOperationException("Add your Anthropic API key in Settings → Plugins → Built-in features.");
        if (client == null || clientKey != apiKey) {
            client = new AnthropicClient { ApiKey = apiKey };
            clientKey = apiKey;
        }

        string model = host.GetSetting(plugin, BuiltInPlugin.ModelSetting) is { Length: > 0 } configuredModel ? configuredModel : BuiltInPlugin.DefaultModel;
        string effort = host.GetSetting(plugin, BuiltInPlugin.EffortSetting) ?? "low";

        string vocabulary = request.ExistingTags.Count > 0 ? string.Join(", ", request.ExistingTags.Take(300)) : "(none yet)";
        string categories = request.Categories.Count > 0 ? string.Join(", ", request.Categories) : "(none yet)";
        string prompt =
            $"""
             Suggest up to {request.MaxTags} tags for this image.
             File name: {request.Image.FileName}
             Folder: {request.Image.Folder}
             Existing tag vocabulary: {vocabulary}
             Existing categories: {categories}
             """;

        BetaMessage response = await client.Beta.Messages.Create(new MessageCreateParams {
            Model = model,
            MaxTokens = 4096,
            System = SystemPrompt,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig {
                Effort = effort switch {
                    "medium" => Effort.Medium,
                    "high" => Effort.High,
                    _ => Effort.Low
                },
                Format = new BetaJsonOutputFormat { Schema = ResponseSchema }
            },
            Messages = [
                new BetaUserMessageParam {
                    Content = new List<BetaContentBlockParam> {
                        new BetaImageBlockParam {
                            Source = new BetaBase64ImageSource {
                                Data = Convert.ToBase64String(request.ImageJpeg),
                                MediaType = MediaType.ImageJpeg
                            }
                        },
                        new BetaTextBlockParam { Text = prompt }
                    }
                }
            ]
        }, cancellationToken);

        if (response.StopReason == "refusal") {
            throw new InvalidOperationException("Claude declined to tag this image.");
        }

        string json = string.Concat(response.Content.Select(block => block.TryPickText(out BetaTextBlock? text) ? text.Text : string.Empty));
        ClaudeTagResult? result = JsonSerializer.Deserialize<ClaudeTagResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result == null) {
            return new TagSuggestion([]);
        }

        return new TagSuggestion(
            [.. result.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(request.MaxTags)],
            string.IsNullOrWhiteSpace(result.Category) ? null : result.Category,
            string.IsNullOrWhiteSpace(result.Description) ? null : result.Description);
    }

    private sealed record ClaudeTagResult(List<string> Tags, string? Category, string? Description);
}
