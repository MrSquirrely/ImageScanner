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
    internal const string TagPromptSetting = "TagPrompt";
    internal const string CaptionPromptSetting = "CaptionPrompt";
    internal const string ResponseTimeoutSetting = "ResponseTimeoutSeconds";
    internal const string ShowConsoleSetting = "ShowKoboldCppConsole";

    internal const string TagsAndCaptionMode = "Tags and caption";
    internal const string TagsOnlyMode = "Tags only";

    internal const string DefaultTagPrompt = "Write a list of booru-style tags for this image.";
    internal const string E621TagPrompt = "Write a list of e621 tags for this image.";
    internal const string DanbooruTagPrompt = "Write a list of Danbooru tags for this image.";

    internal const string DefaultCaptionPrompt =
        "Write a straightforward caption for this image. Begin with the main subject and medium. Mention pivotal elements—people, objects, " +
        "scenery—using confident, definite language. Focus on concrete details like color, shape, texture, and spatial relationships. " +
        "Show how elements interact. Omit mood and speculative wording. If text is present, quote it exactly. Note any watermarks, signatures, " +
        "or compression artifacts. Never mention what's absent, resolution, or unobservable details. Vary your sentence structure and keep " +
        "the description concise, without starting with \"This image is…\" or similar phrasing.";

    private LocalServer? server;

    public string Id => "local.tagger";
    public string Name => "Local AI tagging";
    public string Description => "Tags images with a vision model running on this PC through KoboldCpp. Free and private: images never leave your computer.";
    public string Author => "ImageScanner";
    public Version Version => new(1, 0);
    public string Icon => "\uE977";

    public string ProviderId => LocalTagProvider.ProviderId;
    public string ProviderSummary => "On this PC. Free and private; needs KoboldCpp and a vision model (a GPU is recommended).";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(ServerUrlSetting, "Server URL", PluginSettingKind.Text, "http://localhost:5001",
            "KoboldCpp's default address. Any OpenAI-compatible vision server works (llama.cpp, LM Studio, Ollama at http://localhost:11434)."),
        new(KoboldCppPathSetting, "KoboldCpp executable (optional)", PluginSettingKind.FilePath,
            Description: "koboldcpp.exe. When set, KoboldCpp starts automatically if it isn't running and stops when you close the app.",
            FileFilter: "KoboldCpp (koboldcpp*.exe)|koboldcpp*.exe|Programs (*.exe)|*.exe"),
        new(ModelPathSetting, "Model or KoboldCpp config (optional)", PluginSettingKind.FilePath,
            Description: "A .kcpps launch config or a .gguf vision model, passed to KoboldCpp when it starts it. Leave empty to choose in KoboldCpp's own window.",
            FileFilter: "KoboldCpp config or model (*.kcpps;*.gguf)|*.kcpps;*.gguf|KoboldCpp config (*.kcpps)|*.kcpps|Model (*.gguf)|*.gguf"),
        new(MmprojPathSetting, "Vision projector (mmproj) .gguf", PluginSettingKind.FilePath,
            Description: "Required with a .gguf model: without it the model can't see images. Download the mmproj file that matches your model. " +
                         "Leave empty to use one in the model's folder; not needed with a .kcpps config.",
            FileFilter: "Vision projector (*mmproj*.gguf)|*mmproj*.gguf|Model files (*.gguf)|*.gguf"),
        new(ShowConsoleSetting, "Show KoboldCpp's console window", PluginSettingKind.Toggle, "false",
            "Only applies when the app starts KoboldCpp. Hidden by default because clicking inside a console window pauses the program; output goes to koboldcpp.log in the plugin's data folder."),
        new(ModelNameSetting, "Model name (optional)", PluginSettingKind.Text,
            Description: "Only needed for servers that host several models (LM Studio, Ollama). Defaults to the first model the server lists."),
        new(PasswordSetting, "Server password (optional)", PluginSettingKind.Secret),
        new(ModeSetting, "Generate", PluginSettingKind.Choice, TagsAndCaptionMode, Choices: [TagsAndCaptionMode, TagsOnlyMode],
            Description: "A caption is a second request per image, so tags only is about twice as fast."),
        new(TagPromptSetting, "Tag prompt", PluginSettingKind.MultilineText, DefaultTagPrompt,
            "Sent with each image. The model should answer with a comma-separated list of tags.",
            Presets: [new("Default", DefaultTagPrompt), new("Furry (e621)", E621TagPrompt), new("Anime (Danbooru)", DanbooruTagPrompt)]),
        new(CaptionPromptSetting, "Caption prompt", PluginSettingKind.MultilineText, DefaultCaptionPrompt,
            "Used for the image description when generating tags and caption.",
            Presets: [new("Default", DefaultCaptionPrompt)]),
        new(ImageSizeSetting, "Image size sent to the model (px)", PluginSettingKind.Number, "768",
            "Longest edge. 392 to 896 works well; larger is slower and not always better."),
        new(MaxTokensSetting, "Max tokens per reply", PluginSettingKind.Number, "300"),
        new(ResponseTimeoutSetting, "Response timeout (seconds)", PluginSettingKind.Number, "300",
            "How long to wait for one reply. Raise it if tagging runs on a CPU and replies take minutes."),
        new(TemperatureSetting, "Temperature", PluginSettingKind.Number, "0.1", "Lower gives more consistent tags. 0.1 to 0.5 recommended."),
        new(MaxWordsSetting, "Max words per tag", PluginSettingKind.Number, "4",
            "Longer tags are dropped. Booru tags such as looking_at_viewer have three words."),
        new(LatinOnlySetting, "Latin characters only", PluginSettingKind.Toggle, "true", "Turn off to keep keywords in non-Latin scripts."),
        new(DepluralizeSetting, "Singularize keywords", PluginSettingKind.Toggle, "false", "\"cats\" becomes \"cat\" so the same tag isn't created twice.")
    ];

    public void Initialize(IPluginHost host) {
        server = new LocalServer();
        host.RegisterTagSuggestionProvider(new LocalTagProvider(this, host, server));
    }

    public void Shutdown() => server?.Dispose();
}
