using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Samples;

/// <summary>
/// Example plugin showing every extension point: a local AI tagger (Ollama), an import step,
/// a gallery action and a Tools-menu command.
/// </summary>
public sealed class SamplesPlugin : IPlugin, IConfigurablePlugin {
    internal const string FolderTagsSetting = "FolderTags";
    internal const string OllamaUrlSetting = "OllamaUrl";
    internal const string OllamaModelSetting = "OllamaModel";

    public string Id => "samples";
    public string Name => "Sample tools";
    public string Description => "Local AI tagging with Ollama, folder-name tags on import, duplicate finder and tag sidecar export.";
    public string Author => "ImageScanner";
    public Version Version => new(1, 0);

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(FolderTagsSetting, "Tag new images with their folder name", PluginSettingKind.Toggle, "false",
            "On import, adds the parent folder's name as a tag (e.g. Pictures\\Vacation 2024 → \"vacation 2024\")."),
        new(OllamaUrlSetting, "Ollama URL", PluginSettingKind.Text, "http://localhost:11434"),
        new(OllamaModelSetting, "Ollama vision model", PluginSettingKind.Text, "llava",
            "Any vision-capable model you've pulled, e.g. llava, llama3.2-vision, qwen2.5vl.")
    ];

    public void Initialize(IPluginHost host) {
        host.RegisterTagSuggestionProvider(new OllamaTagProvider(this, host));
        host.RegisterImportProcessor(new FolderNameTagger(this, host));
        host.RegisterCommand(new DuplicateFinder(host));
        host.RegisterImageAction(new SidecarExporter(host));
    }
}

/// <summary>
/// Import step: adds the containing folder's name as a tag when enabled in settings.
/// </summary>
internal sealed class FolderNameTagger(IPlugin plugin, IPluginHost host) : IImportProcessor {
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase) {
        "pictures", "images", "photos", "downloads", "desktop", "documents", "camera roll", "dcim", "screenshots", "new folder"
    };

    public string Name => "Folder name tagger";

    public Task ProcessAsync(LibraryImage image, CancellationToken cancellationToken) {
        if (!bool.TryParse(host.GetSetting(plugin, SamplesPlugin.FolderTagsSetting), out bool enabled) || !enabled) {
            return Task.CompletedTask;
        }

        string folderName = Path.GetFileName(image.Folder.TrimEnd(Path.DirectorySeparatorChar));
        if (folderName.Length > 1 && !folderName.EndsWith(':') && !GenericNames.Contains(folderName)) {
            host.Library.AddTags([image.Id], [folderName]);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Tools-menu command: finds byte-identical files and tags them "duplicate".
/// </summary>
internal sealed class DuplicateFinder(IPluginHost host) : IPluginCommand {
    public string Name => "Find duplicate images";

    public async Task ExecuteAsync(CancellationToken cancellationToken) {
        // Only files sharing an exact size can be identical, so hash just those
        List<IGrouping<long, LibraryImage>> sameSize = [.. host.Library.GetAllImages().GroupBy(image => image.FileSize).Where(group => group.Count() > 1)];
        List<int> duplicates = [];

        await Task.Run(() => {
            foreach (IGrouping<long, LibraryImage> group in sameSize) {
                foreach (IGrouping<string, LibraryImage> identical in group.GroupBy(image => Hash(image.FilePath)).Where(byHash => byHash.Key.Length > 0 && byHash.Count() > 1)) {
                    duplicates.AddRange(identical.Select(image => image.Id));
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }, cancellationToken);

        if (duplicates.Count == 0) {
            host.Notify("No duplicate images found.", NotificationKind.Success);
            return;
        }

        host.Library.AddTags(duplicates, ["duplicate"]);
        host.Notify($"Tagged {duplicates.Count:N0} images as \"duplicate\".", NotificationKind.Success);
        host.ShowSearch("tag:duplicate");
    }

    private static string Hash(string path) {
        try {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (IOException) {
            return string.Empty;
        }
        catch (UnauthorizedAccessException) {
            return string.Empty;
        }
    }
}

/// <summary>
/// Gallery action: writes each image's tags to a "&lt;image&gt;.txt" sidecar (one tag per line), a format many tools read.
/// </summary>
internal sealed class SidecarExporter(IPluginHost host) : IImageAction {
    public string Name => "Export tags to .txt sidecar";

    public bool CanExecute(IReadOnlyList<LibraryImage> images) => images.Any(image => image.Tags.Count > 0);

    public async Task ExecuteAsync(IReadOnlyList<LibraryImage> images, CancellationToken cancellationToken) {
        int written = 0;
        foreach (LibraryImage image in images.Where(image => image.Tags.Count > 0)) {
            await File.WriteAllLinesAsync(Path.ChangeExtension(image.FilePath, ".txt"), image.Tags, cancellationToken);
            written++;
        }

        host.Notify($"Wrote {written:N0} sidecar files.", NotificationKind.Success);
    }
}
