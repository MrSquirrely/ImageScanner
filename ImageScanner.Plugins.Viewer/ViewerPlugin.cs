using System.Globalization;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Viewer;

/// <summary>
/// Replaces the built-in viewer with one that zooms and pans, plays animated GIF, WebP and PNG files and plays videos.
/// </summary>
public sealed class ViewerPlugin : IPlugin, IConfigurablePlugin, IMediaViewer {
    internal const string StillImagesSetting = "OpenStillImages";
    internal const string AutoplaySetting = "Autoplay";
    internal const string LoopSetting = "Loop";
    internal const string MutedSetting = "Muted";
    internal const string VolumeSetting = "Volume";
    internal const string SlideshowSetting = "SlideshowSeconds";

    private IPluginHost? host;
    private ViewerWindow? window;

    public string Id => "pixshelf.viewer";
    public string Name => "Media viewer";
    public string Description => "A viewer with zoom, pan, rotate, full screen and slideshow that plays animated GIF, WebP and PNG files and videos. Replaces the built-in viewer.";
    public string Author => "PixShelf";
    public Version Version => new(1, 0);
    public string Icon => "";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(StillImagesSetting, "Also open still images", PluginSettingKind.Toggle, "true",
            "Off: only videos and animations open here; other images open the way Settings → General says.") { Category = "General" },
        new(SlideshowSetting, "Slideshow seconds per image", PluginSettingKind.Number, "4",
            "Videos and animations play to the end before the slideshow moves on.") { Category = "General" },
        new(AutoplaySetting, "Play videos automatically", PluginSettingKind.Toggle, "true") { Category = "Videos" },
        new(LoopSetting, "Loop videos", PluginSettingKind.Toggle, "true") { Category = "Videos" },
        new(MutedSetting, "Start muted", PluginSettingKind.Toggle, "false", "The viewer also remembers the mute button and volume you last used.") { Category = "Videos" }
    ];

    public void Initialize(IPluginHost pluginHost) {
        host = pluginHost;
        host.RegisterMediaViewer(this);
    }

    public void Shutdown() {
        window?.Close();
    }

    public bool CanView(LibraryImage item) {
        return item.IsVideo || Flag(StillImagesSetting, true) || AnimationLoader.IsAnimated(item.FilePath);
    }

    public void Show(IReadOnlyList<LibraryImage> items, int startIndex) {
        if (items.Count == 0) {
            return;
        }

        // One viewer window, reused when another item is opened. It has no owner, so it can sit behind the main window.
        if (window is null) {
            window = new ViewerWindow(this);
            window.Closed += (_, _) => window = null;
        }

        window.Open(items, startIndex);
        window.Show();
        window.Activate();
    }

    internal bool Flag(string key, bool fallback) {
        return bool.TryParse(host?.GetSetting(this, key), out bool value) ? value : fallback;
    }

    internal double Number(string key, double fallback) {
        return double.TryParse(host?.GetSetting(this, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
    }

    internal void Save(string key, string value) {
        host?.SetSetting(this, key, value);
    }
}