using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Converter;

public sealed class ResultRow(LibraryImage image) : INotifyPropertyChanged {
    public string Source { get; } = image.FilePath;
    public string Name { get; } = image.FileName;
    public string OriginalText { get; } = ImageConverter.FormatSize(image.FileSize);

    public ConversionResult? Result {
        get;
        set {
            field = value;
            OnPropertyChanged(nameof(ConvertedText));
            OnPropertyChanged(nameof(ChangeText));
            OnPropertyChanged(nameof(ChangeBrush));
            OnPropertyChanged(nameof(OutputPath));
        }
    }

    public string ConvertedText => Result?.ConvertedBytes is long bytes ? ImageConverter.FormatSize(bytes) : Result == null ? "…" : "—";

    public string ChangeText => Result == null ? "Waiting"
                                : Result.ConvertedBytes is long bytes ? ImageConverter.Change(Result.OriginalBytes, bytes)
                                : Result.Note ?? "Failed";

    public Brush ChangeBrush => (Brush)Application.Current.FindResource(Result?.ConvertedBytes is long bytes
                                                                            ? bytes < Result.OriginalBytes ? "SuccessBrush" : "DangerBrush"
                                                                            : "SecondaryTextBrush");

    public string? OutputPath => Result?.Output;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Picks a format and where to save, converts the selected images and compares the sizes.
/// </summary>
public partial class ConvertWindow {
    private readonly ConverterPlugin plugin;
    private readonly IPluginHost host;
    private readonly ObservableCollection<ResultRow> rows;
    private CancellationTokenSource? conversion;
    private string? lastOutputFolder;

    public ConvertWindow(ConverterPlugin plugin, IPluginHost host, IReadOnlyList<LibraryImage> images) {
        InitializeComponent();
        this.plugin = plugin;
        this.host = host;
        rows = [.. images.Select(image => new ResultRow(image))];
        ResultsGrid.ItemsSource = rows;

        ConvertOptions saved = plugin.SavedOptions();
        FormatBox.ItemsSource = Enum.GetValues<TargetFormat>().Select(ImageConverter.DisplayName).ToList();
        FormatBox.SelectedItem = ImageConverter.DisplayName(saved.Format);
        QualitySlider.Value = saved.Quality;
        LocationBox.ItemsSource = Enum.GetValues<OutputLocation>().Select(ImageConverter.DisplayName).ToList();
        LocationBox.SelectedItem = ImageConverter.DisplayName(saved.Location);
        FolderBox.Text = saved.Folder ?? string.Empty;
        SmallerOnlyBox.IsChecked = saved.KeepOnlyIfSmaller;
        MetadataBox.IsChecked = saved.KeepMetadata;

        long total = images.Sum(image => image.FileSize);
        SummaryText.Text = $"{images.Count:N0} images selected · {ImageConverter.FormatSize(total)}";
        DetailText.Text = "Originals are never changed; converted copies are saved separately.";
        ConvertButton.Content = $"Convert {images.Count:N0}";
        Closing += (_, e) => {
            if (conversion != null) {
                // Stop after the images being converted right now instead of leaving work running in the background
                conversion.Cancel();
            }
        };
    }

    private TargetFormat SelectedFormat =>
        Enum.GetValues<TargetFormat>().FirstOrDefault(format => ImageConverter.DisplayName(format) == FormatBox.SelectedItem as string);

    private OutputLocation SelectedLocation =>
        Enum.GetValues<OutputLocation>().FirstOrDefault(location => ImageConverter.DisplayName(location) == LocationBox.SelectedItem as string);

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e) => QualitySlider.IsEnabled = ImageConverter.UsesQuality(SelectedFormat);

    private void Location_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        FolderPanel.Visibility = SelectedLocation == OutputLocation.ChosenFolder ? Visibility.Visible : Visibility.Hidden;

    private void Browse_Click(object sender, RoutedEventArgs e) {
        Microsoft.Win32.OpenFolderDialog dialog = new() { Title = "Save converted images in" };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e) {
        if (conversion != null) {
            conversion.Cancel();
            return;
        }

        ConvertOptions options = new(SelectedFormat, (int)QualitySlider.Value, SelectedLocation, FolderBox.Text.Trim(),
                                     SmallerOnlyBox.IsChecked == true, MetadataBox.IsChecked == true);
        if (options.Location == OutputLocation.ChosenFolder && string.IsNullOrWhiteSpace(options.Folder)) {
            HandyControl.Controls.MessageBox.Show(this, "Choose the folder to save converted images in.", "Convert images", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        plugin.Save(options);
        foreach (ResultRow row in rows) {
            row.Result = null;
        }

        conversion = new CancellationTokenSource();
        OptionsPanel.IsEnabled = false;
        ConvertButton.Content = "Stop";
        OpenFolderButton.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        int done = 0;
        Stopwatch watch = Stopwatch.StartNew();
        Dictionary<string, ResultRow> bySource = rows.GroupBy(row => row.Source).ToDictionary(group => group.Key, group => group.First());

        Progress<ConversionResult> progress = new(result => {
            if (bySource.TryGetValue(result.Source, out ResultRow? row)) row.Result = result;
            Progress.Value = ++done * 100.0 / rows.Count;
            SummaryText.Text = $"Converting {done:N0} / {rows.Count:N0}…";
            DetailText.Text = string.Empty;
        });

        bool cancelled = false;
        try {
            await ImageConverter.ConvertAsync([.. bySource.Keys], options, progress, conversion.Token);
        }
        catch (OperationCanceledException) {
            cancelled = true;
        }
        finally {
            conversion = null;
            OptionsPanel.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
            ConvertButton.Content = "Convert again";
        }

        ShowSummary(options.Format, watch.Elapsed, cancelled);
    }

    /// <summary>The headline: how the converted files compare with the originals they replace.</summary>
    private void ShowSummary(TargetFormat format, TimeSpan elapsed, bool cancelled) {
        List<ConversionResult> converted = [.. rows.Select(row => row.Result).OfType<ConversionResult>().Where(result => result.Succeeded)];
        int skipped = rows.Count(row => row.Result is { Succeeded: false });
        long before = converted.Sum(result => result.OriginalBytes);
        long after = converted.Sum(result => result.ConvertedBytes!.Value);

        if (converted.Count == 0) {
            SummaryText.Text = cancelled ? "Stopped before any image was converted." : "No images were converted.";
        }
        else {
            long saved = before - after;
            SummaryText.Text = $"{converted.Count:N0} images: {ImageConverter.FormatSize(before)} → {ImageConverter.FormatSize(after)} · " +
                               (saved >= 0 ? $"{ImageConverter.FormatSize(saved)} saved ({ImageConverter.Change(before, after)})"
                                           : $"{ImageConverter.FormatSize(-saved)} more ({ImageConverter.Change(before, after)})");
        }

        DetailText.Text = string.Join(" · ", new[] {
            cancelled ? "Stopped early" : null,
            converted.Count > 0 ? $"Converted to {ImageConverter.Extension(format)} in {elapsed.TotalSeconds:0} s" : null,
            skipped > 0 ? $"{skipped:N0} skipped (see the Change column)" : null,
            converted.Count > 0 ? "Originals were not changed" : null
        }.OfType<string>());

        lastOutputFolder = converted.Select(result => Path.GetDirectoryName(result.Output)).FirstOrDefault();
        OpenFolderButton.Visibility = lastOutputFolder != null ? Visibility.Visible : Visibility.Collapsed;
        if (converted.Count > 0) {
            host.Notify(SummaryText.Text, NotificationKind.Success);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) {
        if (lastOutputFolder != null && Directory.Exists(lastOutputFolder)) {
            Process.Start(new ProcessStartInfo(lastOutputFolder) { UseShellExecute = true });
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
