using System.Windows;
using System.Windows.Controls;

using HandyControl.Controls;

using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Services;
using ImageScanner.Organizer.ViewModels;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Views;

public partial class ImportWindow {
    private readonly AppSettings settings;

    public ImportWindow(AppSettings settings, PluginManager plugins) {
        InitializeComponent();
        this.settings = settings;

        DriveBox.ItemsSource = DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => drive.Name).ToList();
        IgnoredBox.Text = settings.IgnoredFolders;
        DetectComicsBox.IsChecked = settings.DetectComics;
        SuggestBox.IsChecked = settings.SuggestTagsOnImport;
        AutoApplyBox.IsChecked = settings.AutoApplySuggestions;

        ITagSuggestionProvider? provider = plugins.ActiveTagProvider;
        AiHint.Text = provider == null ? "No AI provider is available; enable one in Settings."
                      : !provider.IsConfigured ? $"{provider.DisplayName} needs an API key. Add it in Settings first."
                      : $"Uses {provider.DisplayName}. Each new image is one paid API request.";
        if (provider is not { IsConfigured: true }) {
            SuggestBox.IsChecked = false;
            SuggestBox.IsEnabled = false;
        }

        SuggestBox_Changed(this, new RoutedEventArgs());
    }

    public ImportRequest? Result { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e) {
        Microsoft.Win32.OpenFolderDialog dialog = new() { Title = "Choose a folder to import" };
        if (dialog.ShowDialog(this) == true) {
            PathBox.Text = dialog.FolderName;
        }
    }

    private void DriveBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (DriveBox.SelectedItem is string drive) {
            PathBox.Text = drive;
        }
    }

    private void SuggestBox_Changed(object sender, RoutedEventArgs e) => AutoApplyBox.IsEnabled = SuggestBox.IsChecked == true;

    private void Import_Click(object sender, RoutedEventArgs e) {
        string path = PathBox.Text.Trim();
        if (!Directory.Exists(path)) {
            HandyControl.Controls.MessageBox.Show("Choose a folder or drive that exists.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        settings.IgnoredFolders = IgnoredBox.Text;
        settings.DetectComics = DetectComicsBox.IsChecked == true;
        settings.SuggestTagsOnImport = SuggestBox.IsChecked == true;
        settings.AutoApplySuggestions = AutoApplyBox.IsChecked == true;
        settings.Save();

        Result = new ImportRequest(path, [.. settings.GetIgnoredFolders()], settings.DetectComics, settings.SuggestTagsOnImport, settings.AutoApplySuggestions);
        DialogResult = true;
    }
}
