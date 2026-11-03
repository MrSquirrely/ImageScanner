using System.Globalization;
using System.Windows;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Duplicates;

/// <summary>
/// Finds duplicate and near-duplicate images with perceptual hashing (DuplaImage.Lib) and lets you send the
/// copies you don't want to the Recycle Bin.
/// </summary>
public sealed class DuplicatesPlugin : IPlugin, IConfigurablePlugin {
    internal const string AlgorithmSetting = "Algorithm";
    internal const string SimilaritySetting = "MinimumSimilarity";

    private IPluginHost? host;
    private DuplicatesWindow? window;

    public string Id => "pixshelf.duplicates";
    public string Name => "Duplicate finder";
    public string Description => "Finds duplicate and look-alike images (resized, re-saved or lightly edited copies) and lets you send the extras to the Recycle Bin.";
    public string Author => "PixShelf";
    public Version Version => new(1, 0);
    public string Icon => "";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(AlgorithmSetting, "Comparison method", PluginSettingKind.Choice, DuplicateScanner.DisplayName(HashAlgorithm.Difference),
            "How images are fingerprinted. Difference is fast and accurate; DCT also catches edited copies but is slower.",
            Choices: [.. Enum.GetValues<HashAlgorithm>().Select(DuplicateScanner.DisplayName)]) { Category = "Matching" },
        new(SimilaritySetting, "Minimum similarity (%)", PluginSettingKind.Number, "92",
            "Images at least this similar are grouped. 100 finds only exact look-alikes; 85 also finds crops and heavier edits.") { Category = "Matching" }
    ];

    public void Initialize(IPluginHost pluginHost) {
        host = pluginHost;
        host.RegisterCommand(new FindDuplicatesCommand(this));
        host.RegisterImageAction(new FindSimilarAction(this));
    }

    public void Shutdown() => window?.Close();

    internal HashAlgorithm Algorithm =>
        Enum.GetValues<HashAlgorithm>().FirstOrDefault(algorithm => DuplicateScanner.DisplayName(algorithm) == host?.GetSetting(this, AlgorithmSetting));

    internal double MinimumSimilarity =>
        double.TryParse(host?.GetSetting(this, SimilaritySetting), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
            ? Math.Clamp(percent, 50, 100) / 100
            : 0.92;

    /// <summary>Opens (or brings back) the review window and scans; <paramref name="focus"/> limits results to groups containing it.</summary>
    internal void ShowWindow(LibraryImage? focus) {
        if (host == null) return;
        if (window is { IsLoaded: true }) {
            window.Activate();
        }
        else {
            window = new DuplicatesWindow(this, host, new DuplicateScanner(host.GetDataDirectory(this))) {
                Owner = Application.Current.MainWindow
            };
            window.Closed += (_, _) => window = null;
            window.Show();
        }

        _ = window.ScanAsync(focus);
    }

    private sealed class FindDuplicatesCommand(DuplicatesPlugin plugin) : IPluginCommand {
        public string Name => "Find duplicate images…";

        public Task ExecuteAsync(CancellationToken cancellationToken) {
            plugin.ShowWindow(null);
            return Task.CompletedTask;
        }
    }

    private sealed class FindSimilarAction(DuplicatesPlugin plugin) : IImageAction {
        public string Name => "Find duplicates of this image";

        public bool CanExecute(IReadOnlyList<LibraryImage> images) => images.Count == 1;

        public Task ExecuteAsync(IReadOnlyList<LibraryImage> images, CancellationToken cancellationToken) {
            plugin.ShowWindow(images[0]);
            return Task.CompletedTask;
        }
    }
}
