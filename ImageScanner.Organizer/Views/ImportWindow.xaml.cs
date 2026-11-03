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

    /// <summary>
    /// Whole-drive imports can take a long time; the Windows drive also holds thousands of system icons and app images.
    /// Asks before continuing. Returns true for ordinary folders.
    /// </summary>
    private bool ConfirmDriveScan(string path) {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        bool isDriveRoot = root != null && string.Equals(fullPath.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        if (!isDriveRoot) {
            return true;
        }

        string driveName = root!.TrimEnd('\\', '/');
        string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        bool isSystemDrive = string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase);
        bool aiOn = SuggestBox.IsChecked == true;

        string message = isSystemDrive
                             ? $"You're about to scan the {driveName} drive, which is where Windows is installed.\n\n" +
                               "It contains thousands of system icons, app graphics and cached images you probably don't want in your library, " +
                               "and scanning it can take a long time. Consider importing your Pictures, Downloads or Documents folders instead.\n\n" +
                               "Scan the whole drive anyway?"
                             : $"You're about to scan the entire {driveName} drive.\n\n" +
                               "Scanning a whole drive can take a while, depending on how many files it holds. You can keep using the app and cancel at any time.\n\n" +
                               "Continue?";
        if (aiOn) {
            message += "\n\nAI tag suggestions are turned on: every new image found will be sent as a separate paid API request.";
        }

        return HandyControl.Controls.MessageBox.Show(message, isSystemDrive ? "Scan the system drive?" : "Scan an entire drive?",
                                                     MessageBoxButton.YesNo, isSystemDrive ? MessageBoxImage.Warning : MessageBoxImage.Information)
               == MessageBoxResult.Yes;
    }

    private void SuggestBox_Changed(object sender, RoutedEventArgs e) => AutoApplyBox.IsEnabled = SuggestBox.IsChecked == true;

    private void Import_Click(object sender, RoutedEventArgs e) {
        string path = PathBox.Text.Trim();
        if (!Directory.Exists(path)) {
            HandyControl.Controls.MessageBox.Show("Choose a folder or drive that exists.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmDriveScan(path)) {
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
