namespace ImageScanner.Plugins.LocalTagger;

/// <summary>
/// Free, private AI tagging with a vision model running on this PC through KoboldCpp
/// (or any OpenAI-compatible server such as llama.cpp, LM Studio or Ollama).
/// The approach follows ImageIndexer (github.com/jabberjabberjabber/ImageIndexer).
/// </summary>
public sealed class LocalTaggerPlugin : IPlugin, IConfigurablePlugin, ITagProviderPlugin {
    internal const string ServerUrlSetting = "ServerUrl";
    internal const string PasswordSetting = "ApiPassword";
    internal const string KoboldCppPathSetting = "KoboldCppPath";
    internal const string ModelPathSetting = "ModelPath";
    internal const string MmprojPathSetting = "MmprojPath";
    internal const string ModelNameSetting = "ModelName";
    internal const string ModeSetting = "Mode";
    internal const string ImageSizeSetting = "ImageSize";
    internal const string MaxTokensSetting = "MaxTokens";
    internal const string TemperatureSetting = "Temperature";
    internal const string MaxWordsSetting = "MaxWordsPerKeyword";
    internal const string LatinOnlySetting = "LatinOnly";
    internal const string DepluralizeSetting = "Depluralize";
    internal const string JsonGrammarSetting = "ConstrainJson";

    internal const string KeywordsAndDescriptionMode = "Keywords and description";
    internal const string KeywordsOnlyMode = "Keywords only";

    private LocalServer? server;

    public string Id => "local.tagger";
    public string Name => "Local AI tagging";
    public string Description => "Tags images with a vision model running on this PC through KoboldCpp. Free and private: images never leave your computer.";
    public string Author => "ImageScanner";
    public Version Version => new(1, 0);

    public string ProviderId => LocalTagProvider.ProviderId;
    public string ProviderSummary => "On this PC. Free and private; needs KoboldCpp and a vision model (a GPU is recommended).";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(ServerUrlSetting, "Server URL", PluginSettingKind.Text, "http://localhost:5001",
            "KoboldCpp's default address. Any OpenAI-compatible vision server works (llama.cpp, LM Studio, Ollama at http://localhost:11434)."),
        new(KoboldCppPathSetting, "KoboldCpp executable (optional)", PluginSettingKind.Text,
            Description: "Full path to koboldcpp.exe. When set, KoboldCpp starts automatically if it isn't running and stops when you close the app."),
        new(ModelPathSetting, "Model or KoboldCpp config (optional)", PluginSettingKind.Text,
            Description: "A .kcpps launch config or a .gguf vision model, passed to KoboldCpp when it starts it. Leave empty to choose in KoboldCpp's own window."),
        new(MmprojPathSetting, "Vision projector .gguf (optional)", PluginSettingKind.Text,
            Description: "The mmproj file that pairs with a .gguf model. Not needed with a .kcpps config."),
        new(ModelNameSetting, "Model name (optional)", PluginSettingKind.Text,
            Description: "Only needed for servers that host several models (LM Studio, Ollama). Defaults to the first model the server lists."),
        new(PasswordSetting, "Server password (optional)", PluginSettingKind.Secret),
        new(ModeSetting, "Generate", PluginSettingKind.Choice, KeywordsAndDescriptionMode, Choices: [KeywordsAndDescriptionMode, KeywordsOnlyMode]),
        new(ImageSizeSetting, "Image size sent to the model (px)", PluginSettingKind.Number, "768",
            "Longest edge. 392 to 896 works well; larger is slower and not always better."),
        new(MaxTokensSetting, "Max tokens per reply", PluginSettingKind.Number, "300"),
        new(TemperatureSetting, "Temperature", PluginSettingKind.Number, "0.1", "Lower gives more consistent tags. 0.1 to 0.5 recommended."),
        new(MaxWordsSetting, "Max words per keyword", PluginSettingKind.Number, "2"),
        new(LatinOnlySetting, "Latin characters only", PluginSettingKind.Toggle, "true", "Turn off to keep keywords in non-Latin scripts."),
        new(DepluralizeSetting, "Singularize keywords", PluginSettingKind.Toggle, "false", "\"cats\" becomes \"cat\" so the same tag isn't created twice."),
        new(JsonGrammarSetting, "Force JSON output", PluginSettingKind.Toggle, "false",
            "Ask the server to constrain replies to JSON. Helps weak models; not every server supports it.")
    ];

    public void Initialize(IPluginHost host) {
        server = new LocalServer();
        host.RegisterTagSuggestionProvider(new LocalTagProvider(this, host, server));
    }

    public void Shutdown() => server?.Dispose();
}
