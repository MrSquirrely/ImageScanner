using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

using ImageScanner.Core.Library;

using MessageBox = HandyControl.Controls.MessageBox;

namespace ImageScanner.Plugins.Duplicates;

public sealed class DuplicateItem(LibraryImage image, BitmapSource? thumbnail, bool isBest) : INotifyPropertyChanged {
    public LibraryImage Image { get; } = image;
    public BitmapSource? Thumbnail { get; } = thumbnail;
    public bool IsBest { get; } = isBest;
    public Visibility BestVisibility => IsBest ? Visibility.Visible : Visibility.Collapsed;
    public string Path => Image.FilePath;
    public string FileName => Image.FileName;
    public string Folder => Image.Folder;
    public string Details => $"{Image.Width} × {Image.Height} · {DuplicatesWindow.FormatSize(Image.FileSize)}";

    public bool Delete {
        get;
        set {
            if (field != value) {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GroupView(int number, DuplicateGroup group, IEnumerable<DuplicateItem> items) {
    public DuplicateGroup Group { get; } = group;
    public ObservableCollection<DuplicateItem> Items { get; } = [.. items];
    public string Header => $"Group {number} · {Items.Count} images · {Group.Similarity:P0} similar";
}

/// <summary>
/// Shows groups of similar images; you tick the copies to remove and they go to the Recycle Bin and out of the library.
/// </summary>
public partial class DuplicatesWindow {
    // Thousands of groups would make the window crawl; most people clean up from the top anyway
    private const int MaxGroupsShown = 400;

    private readonly DuplicatesPlugin plugin;
    private readonly IPluginHost host;
    private readonly DuplicateScanner scanner;
    private readonly ObservableCollection<GroupView> groups = [];
    private CancellationTokenSource? scan;
    private LibraryImage? focus;

    public DuplicatesWindow(DuplicatesPlugin plugin, IPluginHost host, DuplicateScanner scanner) {
        InitializeComponent();
        this.plugin = plugin;
        this.host = host;
        this.scanner = scanner;
        GroupList.ItemsSource = groups;

        AlgorithmBox.ItemsSource = Enum.GetValues<HashAlgorithm>().Select(DuplicateScanner.DisplayName).ToList();
        AlgorithmBox.SelectedItem = DuplicateScanner.DisplayName(plugin.Algorithm);
        SimilaritySlider.Value = Math.Round(plugin.MinimumSimilarity * 100);
        Closed += (_, _) => scan?.Cancel();
    }

    private HashAlgorithm SelectedAlgorithm =>
        Enum.GetValues<HashAlgorithm>().FirstOrDefault(algorithm => DuplicateScanner.DisplayName(algorithm) == AlgorithmBox.SelectedItem as string);

    public async Task ScanAsync(LibraryImage? onlyMatching) {
        focus = onlyMatching;
        scan?.Cancel();
        CancellationTokenSource cancellation = scan = new CancellationTokenSource();

        // Remember the choices for next time
        host.SetSetting(plugin, DuplicatesPlugin.AlgorithmSetting, DuplicateScanner.DisplayName(SelectedAlgorithm));
        host.SetSetting(plugin, DuplicatesPlugin.SimilaritySetting, SimilaritySlider.Value.ToString(CultureInfo.InvariantCulture));

        groups.Clear();
        UpdateSelection();
        EmptyText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ScanButton.IsEnabled = false;
        FocusPanel.Visibility = focus != null ? Visibility.Visible : Visibility.Collapsed;
        FocusText.Text = focus != null ? $"Showing duplicates of {focus.FileName}" : string.Empty;
        SummaryText.Text = string.Empty;

        try {
            List<LibraryImage> images = host.Library.GetAllImages();
            Progress<(int Done, int Total)> progress = new(value => {
                Progress.Value = value.Total > 0 ? value.Done * 100.0 / value.Total : 0;
                ProgressText.Text = $"Checking {value.Done:N0} / {value.Total:N0}";
            });

            DuplicateScanResult result = await scanner.ScanAsync(images, SelectedAlgorithm, SimilaritySlider.Value / 100, progress, cancellation.Token);
            List<DuplicateGroup> found = [.. result.Groups.Where(group => focus == null || group.Images.Any(image => image.Id == focus.Id))];

            ProgressText.Text = "Loading thumbnails…";
            List<GroupView> views = await Task.Run(() => found.Take(MaxGroupsShown).Select((group, index) => new GroupView(index + 1, group,
                group.Images.Select((image, position) => new DuplicateItem(image, LoadThumbnail(image), isBest: position == 0)))).ToList(), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;

            foreach (GroupView view in views) {
                foreach (DuplicateItem item in view.Items) {
                    item.PropertyChanged += (_, _) => UpdateSelection();
                }

                groups.Add(view);
            }

            int copies = found.Sum(group => group.Images.Count - 1);
            SummaryText.Text = found.Count == 0
                                   ? string.Empty
                                   : $"{found.Count:N0} groups with {copies:N0} extra copies, from {result.Hashed:N0} images checked"
                                     + (result.Unreadable > 0 ? $" ({result.Unreadable:N0} couldn't be read)" : string.Empty)
                                     + (found.Count > MaxGroupsShown ? $". Showing the first {MaxGroupsShown}; clean these up and scan again for more." : ".");
            if (found.Count == 0) {
                EmptyText.Text = focus != null
                                     ? $"No duplicates of {focus.FileName} at {SimilaritySlider.Value:0} % similarity."
                                     : $"No duplicates found among {result.Hashed:N0} images at {SimilaritySlider.Value:0} % similarity. Lower the minimum similarity to find looser matches.";
                EmptyText.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) {
            // Closed or rescanned
        }
        finally {
            if (ReferenceEquals(scan, cancellation)) {
                ProgressPanel.Visibility = Visibility.Collapsed;
                ScanButton.IsEnabled = true;
            }
        }
    }

    private BitmapSource? LoadThumbnail(LibraryImage image) {
        try {
            if (host.Library.LoadThumbnail(image.ThumbnailId) is not { } bytes) return null;
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException) {
            return null;
        }
    }

    private IEnumerable<DuplicateItem> AllItems => groups.SelectMany(group => group.Items);

    private void UpdateSelection() {
        List<DuplicateItem> selected = [.. AllItems.Where(item => item.Delete)];
        SelectionText.Text = selected.Count == 0 ? string.Empty : $"{selected.Count:N0} selected · {FormatSize(selected.Sum(item => item.Image.FileSize))}";
        DeleteButton.IsEnabled = selected.Count > 0;
    }

    private void SelectExtras_Click(object sender, RoutedEventArgs e) {
        foreach (GroupView group in groups) {
            foreach (DuplicateItem item in group.Items) {
                item.Delete = !item.IsBest;
            }
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e) {
        foreach (DuplicateItem item in AllItems) {
            item.Delete = false;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) {
        List<DuplicateItem> selected = [.. AllItems.Where(item => item.Delete)];
        if (selected.Count == 0) return;

        // Deleting every copy in a group is allowed, but worth a second look
        int wholeGroups = groups.Count(group => group.Items.All(item => item.Delete));
        string warning = wholeGroups > 0 ? $"\n\nIn {wholeGroups} group(s) every copy is selected, so no copy of those images will be left." : string.Empty;
        if (MessageBox.Show(this, $"Send {selected.Count:N0} images ({FormatSize(selected.Sum(item => item.Image.FileSize))}) to the Recycle Bin?\n\n" +
                                  $"They're also removed from your library. You can restore them from the Recycle Bin.{warning}",
                            "Delete duplicates", MessageBoxButton.OKCancel, wholeGroups > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.OK) {
            return;
        }

        List<DuplicateItem> deleted = [.. selected.Where(item => RecycleBin.Send(item.Path))];
        host.Library.DeleteImages(deleted.Select(item => item.Image.Id));

        foreach (GroupView group in groups.ToList()) {
            foreach (DuplicateItem item in group.Items.Where(deleted.Contains).ToList()) {
                group.Items.Remove(item);
            }

            if (group.Items.Count < 2) {
                groups.Remove(group);
            }
        }

        UpdateSelection();
        int failed = selected.Count - deleted.Count;
        host.Notify(failed == 0
                        ? $"Sent {deleted.Count:N0} duplicates to the Recycle Bin."
                        : $"Sent {deleted.Count:N0} duplicates to the Recycle Bin; {failed:N0} couldn't be moved (in use or read-only).",
                    failed == 0 ? NotificationKind.Success : NotificationKind.Warning);
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync(focus);

    private void ShowAll_Click(object sender, RoutedEventArgs e) => _ = ScanAsync(null);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    internal static string FormatSize(long bytes) => bytes switch {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B"
    };
}
