namespace ImageScanner.Plugins.Claude;

/// <summary>
/// Cloud AI tagging with Claude's vision input and structured JSON output.
/// </summary>
public sealed class ClaudePlugin : IPlugin, IConfigurablePlugin, ITagProviderPlugin {
    internal const string ApiKeySetting = "AnthropicApiKey";
    internal const string ModelSetting = "ClaudeModel";
    internal const string EffortSetting = "ClaudeEffort";
    internal const string DefaultModel = "claude-opus-5-5";

    public string Id => "anthropic.claude";
    public string Name => "Claude AI tagging";
    public string Description => "Suggests tags, a category and a description for images using Anthropic's Claude.";
    public string Author => "ImageScanner";
    public Version Version => new(1, 0);
    public string Icon => "\uE753";

    public string ProviderId => ClaudeTagProvider.ProviderId;
    public string ProviderSummary => "Cloud. High-quality tags; needs an Anthropic API key and each image is one paid request.";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(ApiKeySetting, "Anthropic API key", PluginSettingKind.Secret,
            Description: "Stored encrypted for your Windows account. Leave empty to use the ANTHROPIC_API_KEY environment variable."),
        new(ModelSetting, "Claude model", PluginSettingKind.Text, DefaultModel),
        new(EffortSetting, "Effort", PluginSettingKind.Choice, "low",
            Description: "Higher effort can give more thorough tags but costs more per image.", Choices: ["low", "medium", "high"])
    ];

    public void Initialize(IPluginHost host) => host.RegisterTagSuggestionProvider(new ClaudeTagProvider(this, host));
}
