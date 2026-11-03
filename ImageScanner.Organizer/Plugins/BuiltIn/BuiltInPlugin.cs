using System.Diagnostics;
using System.Windows;

using ImageScanner.Core.Library;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Plugins.BuiltIn;

/// <summary>
/// Features that ship with the app, registered through the public plugin API.
/// </summary>
public sealed class BuiltInPlugin : IPlugin, IConfigurablePlugin {
    public const string ApiKeySetting = "AnthropicApiKey";
    public const string ModelSetting = "ClaudeModel";
    public const string EffortSetting = "ClaudeEffort";
    public const string DefaultModel = "claude-opus-5-5";

    public string Id => "builtin";
    public string Name => "Built-in features";
    public string Description => "Claude AI tagging and Windows Explorer actions.";
    public string Author => "ImageScanner";
    public Version Version => new(1, 0);

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(ApiKeySetting, "Anthropic API key", PluginSettingKind.Secret,
            Description: "Used for AI tag suggestions. Leave empty to use the ANTHROPIC_API_KEY environment variable."),
        new(ModelSetting, "Claude model", PluginSettingKind.Text, DefaultModel),
        new(EffortSetting, "Effort", PluginSettingKind.Choice, "low",
            Description: "Higher effort can give more thorough tags but costs more per image.", Choices: ["low", "medium", "high"])
    ];

    public void Initialize(IPluginHost host) {
        host.RegisterTagSuggestionProvider(new ClaudeTagProvider(this, host));
        host.RegisterImageAction(new DelegateImageAction("Open in default app", images => {
            foreach (LibraryImage image in images.Take(10)) {
                Process.Start(new ProcessStartInfo(image.FilePath) { UseShellExecute = true });
            }
        }));
        host.RegisterImageAction(new DelegateImageAction("Show in Explorer", images =>
            Process.Start("explorer.exe", $"/select,\"{images[0].FilePath}\"")));
        host.RegisterImageAction(new DelegateImageAction("Copy file path", images =>
            Clipboard.SetText(string.Join(Environment.NewLine, images.Select(image => image.FilePath)))));
    }

    private sealed class DelegateImageAction(string name, Action<IReadOnlyList<LibraryImage>> execute) : IImageAction {
        public string Name => name;

        public Task ExecuteAsync(IReadOnlyList<LibraryImage> images, CancellationToken cancellationToken) {
            execute(images);
            return Task.CompletedTask;
        }
    }
}
