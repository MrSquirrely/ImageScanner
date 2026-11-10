using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Videos;

/// <summary>
/// Adds videos to the library: imports and rescans pick up video files next to the images, with a thumbnail,
/// the frame size and the running time.
/// </summary>
public sealed class VideosPlugin : IPlugin, IConfigurablePlugin {
    internal const string ExtensionsSetting = "Extensions";
    internal const string DefaultExtensions = ".mp4; .m4v; .mkv; .webm; .mov; .avi; .wmv; .mpg; .mpeg; .flv";

    public string Id => "pixshelf.videos";
    public string Name => "Video tracking";
    public string Description => "Adds videos to your library. Imports and rescans pick up video files with a thumbnail, size and running time, so you can tag, rate and search them like images.";
    public string Author => "PixShelf";
    public Version Version => new(1, 0);
    public string Icon => "";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(ExtensionsSetting, "Video file types", PluginSettingKind.Text, DefaultExtensions,
            "File extensions to import as videos, separated by ; or spaces. Restart PixShelf after changing this, then rescan. "
            + "Thumbnails come from Windows, so a type shows a frame only if Windows can play it; otherwise it gets the file's icon.")
    ];

    public void Initialize(IPluginHost host) {
        host.RegisterMediaReader(new VideoReader(ParseExtensions(host.GetSetting(this, ExtensionsSetting))));
    }

    /// <summary>
    /// "mp4; .MKV webm" becomes [".mp4", ".mkv", ".webm"]. Falls back to the defaults when nothing usable is left.
    /// </summary>
    internal static IReadOnlyCollection<string> ParseExtensions(string? text) {
        List<string> extensions = Parse(text);
        return (extensions.Count > 0) ? extensions : Parse(DefaultExtensions);
    }

    private static List<string> Parse(string? text) {
        List<string> extensions = [];
        foreach (string part in (text ?? string.Empty).Split([';', ',', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string extension = "." + part.TrimStart('*').TrimStart('.').ToLowerInvariant();
            bool valid = (extension.Length > 1) && extension.Skip(1).All(char.IsLetterOrDigit);
            if (valid && !extensions.Contains(extension)) {
                extensions.Add(extension);
            }
        }

        return extensions;
    }
}