using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.LocalTagger;

/// <summary>
/// Suggests tags with a vision model running on this PC. Requests are sent one at a time because
/// a local server processes a single generation at once anyway.
/// </summary>
internal sealed class LocalTagProvider(LocalTaggerPlugin plugin, IPluginHost host, LocalServer server) : ITagSuggestionProvider {
    public const string ProviderId = "local.koboldcpp";

    private const string SystemPrompt = "You are an image tagging assistant. You describe images accurately and answer only with JSON.";

    private readonly SemaphoreSlim oneAtATime = new(1, 1);
    private string? cachedModel;

    public string Id => ProviderId;
    public string DisplayName => "Local AI (KoboldCpp)";

    public bool IsConfigured => Uri.TryCreate(Setting(LocalTaggerPlugin.ServerUrlSetting), UriKind.Absolute, out _);

    public async Task<TagSuggestion> SuggestAsync(TagSuggestionRequest request, CancellationToken cancellationToken) {
        LocalServerOptions options = ReadServerOptions();
        bool includeDescription = Setting(LocalTaggerPlugin.ModeSetting) != LocalTaggerPlugin.KeywordsOnlyMode;
        int imageSize = Math.Clamp(ReadInt(LocalTaggerPlugin.ImageSizeSetting, 768), 256, 2048);

        await oneAtATime.WaitAsync(cancellationToken);
        try {
            await server.EnsureRunningAsync(options, cancellationToken);
            cachedModel ??= Setting(LocalTaggerPlugin.ModelNameSetting) is { Length: > 0 } name
                                ? name
                                : await server.GetDefaultModelAsync(options, cancellationToken);

            byte[] jpeg = await Task.Run(() => PrepareImage(request, imageSize), cancellationToken);
            JsonObject payload = BuildPayload(request, jpeg, includeDescription);

            // Small models occasionally return no usable keywords; one retry usually fixes it
            ParsedResponse parsed = new([], null, null);
            List<string> keywords = [];
            for (int attempt = 0; attempt < 2 && keywords.Count == 0; attempt++) {
                parsed = ResponseParser.Parse(await server.ChatAsync(options, payload, cancellationToken));
                keywords = KeywordNormalizer.Normalize(parsed.Keywords, ReadKeywordOptions());
            }

            if (keywords.Count == 0) {
                throw new TagProviderException($"The local model didn't return usable keywords for {request.Image.FileName}.");
            }

            // Only accept a category the library already uses; small models invent inconsistent ones
            string? category = request.Categories.FirstOrDefault(existing => string.Equals(existing, parsed.Category, StringComparison.OrdinalIgnoreCase));
            return new TagSuggestion([.. keywords.Take(request.MaxTags)], category, includeDescription ? parsed.Description : null);
        }
        finally {
            oneAtATime.Release();
        }
    }

    private JsonObject BuildPayload(TagSuggestionRequest request, byte[] jpeg, bool includeDescription) {
        JsonObject payload = new() {
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}" }
                        },
                        new JsonObject { ["type"] = "text", ["text"] = BuildInstruction(request, includeDescription) })
                }),
            ["max_tokens"] = ReadInt(LocalTaggerPlugin.MaxTokensSetting, 300),
            ["temperature"] = ReadDouble(LocalTaggerPlugin.TemperatureSetting, 0.1),
            ["top_p"] = 0.8,
            // KoboldCpp / llama.cpp sampler extensions; other servers ignore them
            ["top_k"] = 100,
            ["min_p"] = 0.0,
            // Qwen-style models otherwise spend the token budget "thinking"
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false }
        };

        if (cachedModel != null) {
            payload["model"] = cachedModel;
        }

        if (bool.TryParse(Setting(LocalTaggerPlugin.JsonGrammarSetting), out bool constrain) && constrain) {
            JsonObject properties = new() { ["Keywords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } } };
            JsonArray required = ["Keywords"];
            if (includeDescription) {
                properties["Description"] = new JsonObject { ["type"] = "string" };
                required.Add("Description");
            }

            payload["response_format"] = new JsonObject {
                ["type"] = "json_object",
                ["schema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required }
            };
        }

        return payload;
    }

    private static string BuildInstruction(TagSuggestionRequest request, bool includeDescription) {
        int minimum = Math.Min(5, request.MaxTags);
        StringBuilder instruction = new("Look at this image and return a JSON object with these fields:\n");
        if (includeDescription) {
            instruction.AppendLine("- \"Description\": one or two sentences in English describing the image, written in the active voice.");
        }

        instruction.AppendLine($"- \"Keywords\": {minimum} to {request.MaxTags} English keywords of one or two words each, covering the main subjects, " +
                               "objects, setting, activities, artistic style, notable colors and mood. If people are visible, include clothing and approximate age group.");
        if (request.Categories.Count > 0) {
            instruction.AppendLine($"- \"Category\": the best match from this list, or an empty string if none fits: {string.Join(", ", request.Categories.Take(40))}.");
        }

        if (request.ExistingTags.Count > 0) {
            // Kept short: small models have small context windows and copy long lists verbatim
            instruction.AppendLine($"Reuse these existing tags when they describe the image: {string.Join(", ", request.ExistingTags.Take(60))}.");
        }

        instruction.Append("Answer with the JSON object only.");
        return instruction.ToString();
    }

    private static byte[] PrepareImage(TagSuggestionRequest request, int imageSize) {
        try {
            return ImageProcessing.EncodeJpeg(request.Image.FilePath, imageSize, 90);
        }
        catch (Exception) {
            // File moved or unreadable since import: fall back to the copy the host prepared
            return request.ImageJpeg;
        }
    }

    private LocalServerOptions ReadServerOptions() {
        if (!Uri.TryCreate(Setting(LocalTaggerPlugin.ServerUrlSetting), UriKind.Absolute, out Uri? url)) {
            throw new TagProviderException("The local AI server URL isn't valid. Check it in Settings → Plugins → Local AI tagging.", isConfigurationError: true);
        }

        return new LocalServerOptions {
            BaseUrl = url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/"),
            ApiPassword = Setting(LocalTaggerPlugin.PasswordSetting),
            KoboldCppPath = Setting(LocalTaggerPlugin.KoboldCppPathSetting),
            ModelPath = Setting(LocalTaggerPlugin.ModelPathSetting),
            MmprojPath = Setting(LocalTaggerPlugin.MmprojPathSetting)
        };
    }

    private KeywordOptions ReadKeywordOptions() => new() {
        MaxWordsPerKeyword = Math.Clamp(ReadInt(LocalTaggerPlugin.MaxWordsSetting, 2), 1, 5),
        LatinOnly = !bool.TryParse(Setting(LocalTaggerPlugin.LatinOnlySetting), out bool latin) || latin,
        Depluralize = bool.TryParse(Setting(LocalTaggerPlugin.DepluralizeSetting), out bool depluralize) && depluralize
    };

    private string? Setting(string key) => host.GetSetting(plugin, key) is { Length: > 0 } value ? value.Trim() : null;

    private int ReadInt(string key, int fallback) => int.TryParse(Setting(key), out int value) ? value : fallback;

    private double ReadDouble(string key, double fallback) =>
        double.TryParse(Setting(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
}
