using System.Globalization;
using System.Text.Json.Nodes;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.LocalTagger;

/// <summary>
/// Suggests tags with a vision model running on this PC. Requests are sent one at a time because
/// a local server processes a single generation at once anyway.
/// </summary>
internal sealed class LocalTagProvider(LocalTaggerPlugin plugin, IPluginHost host, LocalServer server) : ITagSuggestionProvider {
    public const string ProviderId = "local.koboldcpp";

    private const string SystemPrompt = "You are a helpful image captioner.";

    private readonly SemaphoreSlim oneAtATime = new(1, 1);
    private string? cachedModel;

    public string Id => ProviderId;
    public string DisplayName => "Local AI (KoboldCpp)";

    public bool IsConfigured => Uri.TryCreate(Setting(LocalTaggerPlugin.ServerUrlSetting), UriKind.Absolute, out _);

    public async Task<TagSuggestion> SuggestAsync(TagSuggestionRequest request, CancellationToken cancellationToken) {
        LocalServerOptions options = ReadServerOptions();
        // "Keywords only" is the setting's value from before tags and captions used separate prompts
        bool includeCaption = Setting(LocalTaggerPlugin.ModeSetting) is not (LocalTaggerPlugin.TagsOnlyMode or "Keywords only");
        string tagPrompt = Setting(LocalTaggerPlugin.TagPromptSetting) ?? LocalTaggerPlugin.DefaultTagPrompt;
        string captionPrompt = Setting(LocalTaggerPlugin.CaptionPromptSetting) ?? LocalTaggerPlugin.DefaultCaptionPrompt;
        int imageSize = Math.Clamp(ReadInt(LocalTaggerPlugin.ImageSizeSetting, 768), 256, 2048);

        await oneAtATime.WaitAsync(cancellationToken);
        try {
            await server.EnsureRunningAsync(options, cancellationToken);
            cachedModel ??= Setting(LocalTaggerPlugin.ModelNameSetting) is { Length: > 0 } name
                                ? name
                                : await server.GetDefaultModelAsync(options, cancellationToken);

            byte[] jpeg = await Task.Run(() => PrepareImage(request, imageSize), cancellationToken);

            // Small models occasionally return nothing usable; one retry usually fixes it
            List<string> tags = [];
            for (int attempt = 0; attempt < 2 && tags.Count == 0; attempt++) {
                string? reply = await server.ChatAsync(options, BuildPayload(jpeg, tagPrompt), cancellationToken);
                tags = KeywordNormalizer.Normalize(ResponseParser.ParseTagList(reply), ReadKeywordOptions());
            }

            if (tags.Count == 0) {
                throw new TagProviderException($"The local model didn't return usable tags for {request.Image.FileName}.");
            }

            string? caption = null;
            if (includeCaption) {
                caption = ResponseParser.ParseCaption(await server.ChatAsync(options, BuildPayload(jpeg, captionPrompt), cancellationToken));
            }

            return new TagSuggestion([.. tags.Take(request.MaxTags)], Description: caption);
        }
        finally {
            oneAtATime.Release();
        }
    }

    /// <summary>
    /// One chat request: the image followed by the user's prompt, exactly as written in settings.
    /// </summary>
    private JsonObject BuildPayload(byte[] jpeg, string prompt) {
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
                        new JsonObject { ["type"] = "text", ["text"] = prompt })
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

        return payload;
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

    // Trims quotes too: Explorer's "Copy as path" wraps paths in them
    private string? Setting(string key) => host.GetSetting(plugin, key)?.Trim().Trim('"') is { Length: > 0 } value ? value : null;

    private int ReadInt(string key, int fallback) => int.TryParse(Setting(key), out int value) ? value : fallback;

    private double ReadDouble(string key, double fallback) =>
        double.TryParse(Setting(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
}
