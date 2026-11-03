using ImageScanner.Core.Library;

namespace ImageScanner.Plugins;

/// <summary>
/// Entry point of a plugin. The organizer creates one instance of every public, non-abstract
/// <see cref="IPlugin"/> type found in a plugin assembly (parameterless constructor required),
/// then calls <see cref="Initialize"/> where the plugin registers its features with the host.
/// </summary>
public interface IPlugin {
    /// <summary>Stable, unique id, e.g. "com.example.duplicates". Used to store settings.</summary>
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Author { get; }
    Version Version { get; }

    void Initialize(IPluginHost host);

    /// <summary>Called when the app closes or the plugin is disabled.</summary>
    void Shutdown() { }
}

/// <summary>
/// Optional: plugins that expose settings. The host renders an editor for them on the Settings page
/// and persists the values (secrets are encrypted per Windows user).
/// </summary>
public interface IConfigurablePlugin {
    IReadOnlyList<PluginSetting> Settings { get; }
}

public enum PluginSettingKind {
    Text,
    Secret,
    Number,
    Toggle,
    Choice
}

public record PluginSetting(string Key, string Label, PluginSettingKind Kind, string? DefaultValue = null, string? Description = null, IReadOnlyList<string>? Choices = null);

public enum NotificationKind {
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Services the organizer exposes to plugins.
/// </summary>
public interface IPluginHost {
    /// <summary>The open library. All changes made through it update the UI automatically.</summary>
    ImageLibrary Library { get; }

    /// <summary>Images currently selected in the gallery.</summary>
    IReadOnlyList<LibraryImage> SelectedImages { get; }

    /// <summary>A writable folder dedicated to this plugin.</summary>
    string GetDataDirectory(IPlugin plugin);

    string? GetSetting(IPlugin plugin, string key);
    void SetSetting(IPlugin plugin, string key, string? value);

    void Notify(string message, NotificationKind kind = NotificationKind.Info);

    /// <summary>Runs a search in the gallery, e.g. "tag:duplicate".</summary>
    void ShowSearch(string query);

    void RegisterTagSuggestionProvider(ITagSuggestionProvider provider);
    void RegisterImageAction(IImageAction action);
    void RegisterImportProcessor(IImportProcessor processor);
    void RegisterCommand(IPluginCommand command);
}

/// <summary>
/// Suggests tags (and optionally a category) for an image, e.g. by calling a vision model.
/// Users pick the active provider in Settings.
/// </summary>
public interface ITagSuggestionProvider {
    string Id { get; }
    string DisplayName { get; }

    /// <summary>False when required configuration (API key, endpoint ...) is missing.</summary>
    bool IsConfigured { get; }

    Task<TagSuggestion> SuggestAsync(TagSuggestionRequest request, CancellationToken cancellationToken);
}

/// <param name="Image">The library record.</param>
/// <param name="ImageJpeg">The image downscaled (longest edge ≤ 1024px) and encoded as JPEG.</param>
/// <param name="ExistingTags">Tags already used in the library, so providers can prefer reusing them.</param>
/// <param name="Categories">Categories already defined in the library.</param>
/// <param name="MaxTags">Upper bound on how many tags to return.</param>
public record TagSuggestionRequest(LibraryImage Image, byte[] ImageJpeg, IReadOnlyCollection<string> ExistingTags, IReadOnlyCollection<string> Categories, int MaxTags);

public record TagSuggestion(IReadOnlyList<string> Tags, string? Category = null, string? Description = null);

/// <summary>
/// Optional: implemented by a plugin whose main purpose is AI tagging, so the first-run wizard and
/// Settings can offer it as a tagging engine before the plugin is initialized.
/// </summary>
public interface ITagProviderPlugin {
    /// <summary>The <see cref="ITagSuggestionProvider.Id"/> this plugin registers.</summary>
    string ProviderId { get; }

    /// <summary>One or two sentences shown when choosing an engine (where it runs, cost, requirements).</summary>
    string ProviderSummary { get; }
}

/// <summary>
/// Thrown by a tag provider when a request fails. Set <see cref="IsConfigurationError"/> when every
/// following image would fail the same way (bad API key, server not running) so the batch stops early.
/// </summary>
public class TagProviderException(string message, bool isConfigurationError = false, Exception? innerException = null)
    : Exception(message, innerException) {
    public bool IsConfigurationError { get; } = isConfigurationError;
}

/// <summary>
/// An action shown in the gallery's right-click menu.
/// </summary>
public interface IImageAction {
    string Name { get; }

    bool CanExecute(IReadOnlyList<LibraryImage> images) => images.Count > 0;

    Task ExecuteAsync(IReadOnlyList<LibraryImage> images, CancellationToken cancellationToken);
}

/// <summary>
/// Runs on every newly imported image after it has been saved to the library.
/// </summary>
public interface IImportProcessor {
    string Name { get; }

    Task ProcessAsync(LibraryImage image, CancellationToken cancellationToken);
}

/// <summary>
/// A command shown in the Plugins menu.
/// </summary>
public interface IPluginCommand {
    string Name { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}
