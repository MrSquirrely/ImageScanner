using System.Globalization;
using System.Windows;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Converter;

/// <summary>
/// Batch-converts images to AVIF, WebP, JPEG XL, JPEG or PNG and reports how much smaller (or larger) they got.
/// </summary>
public sealed class ConverterPlugin : IPlugin, IConfigurablePlugin {
    internal const string FormatSetting = "Format";
    internal const string QualitySetting = "Quality";
    internal const string LocationSetting = "OutputLocation";
    internal const string FolderSetting = "OutputFolder";
    internal const string SmallerOnlySetting = "KeepOnlyIfSmaller";
    internal const string MetadataSetting = "KeepMetadata";

    private IPluginHost? host;

    public string Id => "pixshelf.converter";
    public string Name => "Batch converter";
    public string Description => "Converts selected images to AVIF, WebP or another format and shows how much space it saved. Originals are left untouched.";
    public string Author => "PixShelf";
    public Version Version => new(1, 0);
    public string Icon => "";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(FormatSetting, "Convert to", PluginSettingKind.Choice, ImageConverter.DisplayName(TargetFormat.Avif),
            "The format the convert window starts with.", Choices: [.. Enum.GetValues<TargetFormat>().Select(ImageConverter.DisplayName)]) { Category = "Output" },
        new(QualitySetting, "Quality (1-100)", PluginSettingKind.Number, "70",
            "Lower makes smaller files. 60-75 suits AVIF, 75-85 suits WebP and JPEG. PNG is always lossless.") { Category = "Output" },
        new(LocationSetting, "Save converted images", PluginSettingKind.Choice, ImageConverter.DisplayName(OutputLocation.ConvertedSubfolder),
            Choices: [.. Enum.GetValues<OutputLocation>().Select(ImageConverter.DisplayName)]) { Category = "Output" },
        new(FolderSetting, "Folder for converted images", PluginSettingKind.FolderPath,
            Description: "Used when saving \"In a folder I choose\".") { Category = "Output" },
        new(SmallerOnlySetting, "Only keep files that got smaller", PluginSettingKind.Toggle, "false",
            "Discards a converted file if it's not smaller than the original (common for small PNG icons).") { Category = "Options" },
        new(MetadataSetting, "Keep metadata (EXIF, color profile)", PluginSettingKind.Toggle, "true",
            "Turn off for smaller files without camera details or GPS location.") { Category = "Options" }
    ];

    public void Initialize(IPluginHost pluginHost) {
        host = pluginHost;
        host.RegisterImageAction(new ConvertAction(this));
        host.RegisterCommand(new ConvertCommand(this));
    }

    public void Shutdown() {
    }

    internal ConvertOptions SavedOptions() => new(
        Enum.GetValues<TargetFormat>().FirstOrDefault(format => ImageConverter.DisplayName(format) == Setting(FormatSetting)),
        int.TryParse(Setting(QualitySetting), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality) ? Math.Clamp(quality, 1, 100) : 70,
        Enum.GetValues<OutputLocation>().FirstOrDefault(location => ImageConverter.DisplayName(location) == Setting(LocationSetting)),
        Setting(FolderSetting),
        bool.TryParse(Setting(SmallerOnlySetting), out bool smallerOnly) && smallerOnly,
        !bool.TryParse(Setting(MetadataSetting), out bool metadata) || metadata);

    internal void Save(ConvertOptions options) {
        host?.SetSetting(this, FormatSetting, ImageConverter.DisplayName(options.Format));
        host?.SetSetting(this, QualitySetting, options.Quality.ToString(CultureInfo.InvariantCulture));
        host?.SetSetting(this, LocationSetting, ImageConverter.DisplayName(options.Location));
        host?.SetSetting(this, FolderSetting, options.Folder);
        host?.SetSetting(this, SmallerOnlySetting, options.KeepOnlyIfSmaller.ToString());
        host?.SetSetting(this, MetadataSetting, options.KeepMetadata.ToString());
    }

    private string? Setting(string key) => host?.GetSetting(this, key) is { Length: > 0 } value ? value.Trim().Trim('"') : null;

    private void Open(IReadOnlyList<LibraryImage> images) {
        if (host == null) return;
        if (images.Count == 0) {
            host.Notify("Select the images to convert first.", NotificationKind.Info);
            return;
        }

        new ConvertWindow(this, host, images) { Owner = Application.Current.MainWindow }.Show();
    }

    private sealed class ConvertAction(ConverterPlugin plugin) : IImageAction {
        public string Name => "Convert images…";

        public Task ExecuteAsync(IReadOnlyList<LibraryImage> images, CancellationToken cancellationToken) {
            plugin.Open(images);
            return Task.CompletedTask;
        }
    }

    private sealed class ConvertCommand(ConverterPlugin plugin) : IPluginCommand {
        public string Name => "Batch convert images…";

        public Task ExecuteAsync(CancellationToken cancellationToken) {
            plugin.Open(plugin.host?.SelectedImages ?? []);
            return Task.CompletedTask;
        }
    }
}
