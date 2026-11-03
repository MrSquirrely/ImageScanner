using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Services;
using ImageScanner.Organizer.Views;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.ViewModels;

public partial class PluginSettingViewModel(PluginSetting definition, string? value) : ObservableObject {
    public PluginSetting Definition { get; } = definition;
    public string Label => Definition.Label;
    public string? Description => Definition.Description;
    public IReadOnlyList<string> Choices => Definition.Choices ?? [];
    public bool IsText => Definition.Kind is PluginSettingKind.Text or PluginSettingKind.Number;
    public bool IsSecret => Definition.Kind == PluginSettingKind.Secret;
    public bool IsToggle => Definition.Kind == PluginSettingKind.Toggle;
    public bool IsChoice => Definition.Kind == PluginSettingKind.Choice;
    public bool IsPath => Definition.Kind is PluginSettingKind.FilePath or PluginSettingKind.FolderPath;

    [ObservableProperty]
    public partial string? Value { get; set; } = value;

    [RelayCommand]
    private void Browse() {
        // Start where the current value points, so changing a model next to the old one is one click
        string? current = string.IsNullOrWhiteSpace(Value) ? null : Value.Trim().Trim('"');
        string? startFolder = current == null ? null
                              : Directory.Exists(current) ? current
                              : Path.GetDirectoryName(current) is { } parent && Directory.Exists(parent) ? parent
                              : null;

        if (Definition.Kind == PluginSettingKind.FolderPath) {
            Microsoft.Win32.OpenFolderDialog folderDialog = new() { Title = Label };
            if (startFolder != null) folderDialog.InitialDirectory = startFolder;
            if (folderDialog.ShowDialog() == true) Value = folderDialog.FolderName;
            return;
        }

        Microsoft.Win32.OpenFileDialog fileDialog = new() {
            Title = Label,
            Filter = string.IsNullOrWhiteSpace(Definition.FileFilter) ? "All files (*.*)|*.*" : Definition.FileFilter,
            CheckFileExists = true
        };
        if (startFolder != null) fileDialog.InitialDirectory = startFolder;
        if (current != null && File.Exists(current)) fileDialog.FileName = Path.GetFileName(current);
        if (fileDialog.ShowDialog() == true) Value = fileDialog.FileName;
    }

    public bool BoolValue {
        get => bool.TryParse(Value, out bool result) && result;
        set {
            Value = value.ToString();
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// One plugin card, on the Settings → Plugins tab or the first-run wizard. In Settings (<paramref name="applyImmediately"/>)
/// the switch is saved at once; the wizard reads <see cref="IsEnabled"/> when it finishes instead.
/// </summary>
public partial class PluginEntryViewModel(PluginInfo info, PluginManager manager, bool applyImmediately = true, bool? initiallyEnabled = null) : ObservableObject {
    private bool isEnabled = initiallyEnabled ?? info.IsEnabled;

    public PluginInfo Info { get; } = info;
    public string Name => Info.Plugin.Name;
    public string Description => Info.Plugin.Description;
    public string Byline => Info.IsBuiltIn ? $"Built-in · v{Info.Plugin.Version}" : $"{Info.Plugin.Author} · v{Info.Plugin.Version}";
    public bool HasSettings => Info.HasSettings;

    public bool IsEnabled {
        get => isEnabled;
        set {
            if (SetProperty(ref isEnabled, value) && applyImmediately) {
                manager.SetEnabled(Info, value);
                Growl.Info("Restart Image Organizer to apply plugin changes.");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSettings))]
    private void OpenSettings() {
        PluginSettingsWindow window = new(new PluginSettingsViewModel(Info, manager)) {
            Owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(window => window.IsActive)
        };
        window.ShowDialog();
    }
}

/// <summary>
/// The settings dialog for one plugin. Values are written only when the user saves.
/// </summary>
public partial class PluginSettingsViewModel(PluginInfo info, PluginManager manager) {
    public PluginInfo Info { get; } = info;
    public string Title => $"{Info.Plugin.Name} settings";

    public IReadOnlyList<PluginSettingViewModel> Settings { get; } =
        info.Plugin is IConfigurablePlugin configurable
            ? [.. configurable.Settings.Select(setting => new PluginSettingViewModel(setting, manager.GetSetting(info.Plugin, setting.Key)))]
            : [];

    public void Save() {
        foreach (PluginSettingViewModel setting in Settings) {
            manager.SetSetting(Info.Plugin, setting.Definition.Key, setting.Value);
        }
    }
}

public partial class SettingsViewModel : ObservableObject {
    private readonly AppSettings settings;
    private readonly ImageLibrary library;

    public SettingsViewModel(AppSettings settings, PluginManager plugins, ImageLibrary library) {
        this.settings = settings;
        this.library = library;
        Providers = [.. plugins.TagProviders];
        SelectedProvider = plugins.ActiveTagProvider;
        DarkTheme = settings.DarkTheme;
        UseSystemViewer = settings.UseSystemViewer;
        MaxSuggestedTags = settings.MaxSuggestedTags;
        AiConcurrency = settings.AiConcurrency;
        SuggestTagsOnImport = settings.SuggestTagsOnImport;
        AutoApplySuggestions = settings.AutoApplySuggestions;
        DetectComics = settings.DetectComics;
        IgnoredFolders = settings.IgnoredFolders;
        LibraryPath = settings.LibraryPath;
        PluginEntries = [.. plugins.Plugins.Select(info => new PluginEntryViewModel(info, plugins))];
        Sources = [.. library.GetSources()];
    }

    public List<ITagSuggestionProvider> Providers { get; }
    public ObservableCollection<PluginEntryViewModel> PluginEntries { get; }
    public ObservableCollection<ScanSource> Sources { get; }
    public string PluginFolder => PluginManager.PluginDirectories.Last();

    [ObservableProperty]
    public partial ITagSuggestionProvider? SelectedProvider { get; set; }

    [ObservableProperty]
    public partial bool DarkTheme { get; set; }

    [ObservableProperty]
    public partial bool UseSystemViewer { get; set; }

    [ObservableProperty]
    public partial int MaxSuggestedTags { get; set; }

    [ObservableProperty]
    public partial int AiConcurrency { get; set; }

    [ObservableProperty]
    public partial bool SuggestTagsOnImport { get; set; }

    [ObservableProperty]
    public partial bool AutoApplySuggestions { get; set; }

    [ObservableProperty]
    public partial bool DetectComics { get; set; }

    [ObservableProperty]
    public partial string IgnoredFolders { get; set; }

    [ObservableProperty]
    public partial string LibraryPath { get; set; }

    partial void OnDarkThemeChanged(bool value) => App.ApplyTheme(value);

    [RelayCommand]
    private void Save() {
        settings.DarkTheme = DarkTheme;
        settings.UseSystemViewer = UseSystemViewer;
        settings.ActiveTagProviderId = SelectedProvider?.Id;
        settings.MaxSuggestedTags = Math.Clamp(MaxSuggestedTags, 1, 50);
        settings.AiConcurrency = Math.Clamp(AiConcurrency, 1, 16);
        settings.SuggestTagsOnImport = SuggestTagsOnImport;
        settings.AutoApplySuggestions = AutoApplySuggestions;
        settings.DetectComics = DetectComics;
        settings.IgnoredFolders = IgnoredFolders;

        bool libraryChanged = !string.Equals(settings.LibraryPath, LibraryPath, StringComparison.OrdinalIgnoreCase);
        settings.LibraryPath = LibraryPath;
        settings.Save();
        Growl.Success(libraryChanged ? "Settings saved. Restart to open the new library file." : "Settings saved.");
    }

    [RelayCommand]
    private void BrowseLibrary() {
        Microsoft.Win32.SaveFileDialog dialog = new() {
            Title = "Choose library database",
            Filter = "Library database (*.db)|*.db",
            FileName = Path.GetFileName(LibraryPath),
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == true) {
            LibraryPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Undoes the live theme preview when the window is cancelled; nothing else is applied until Save.
    /// </summary>
    public void DiscardChanges() => App.ApplyTheme(settings.DarkTheme);

    [RelayCommand]
    private void RerunSetup() {
        settings.FirstRunCompleted = false;
        settings.Save();
        Growl.Info("The setup wizard will open the next time you start Image Organizer.");
    }

    [RelayCommand]
    private void OpenPluginFolder() {
        Directory.CreateDirectory(PluginFolder);
        Process.Start(new ProcessStartInfo(PluginFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void RemoveSource(ScanSource? source) {
        if (source == null) return;
        bool removeImages = HandyControl.Controls.MessageBox.Show(
            $"Also remove the {source.ImageCount:N0} images from \"{source.RootPath}\" from the library?\n\nYes = remove images, No = keep images but stop rescanning this folder.",
            "Remove folder", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
        library.RemoveSource(source.RootPath, removeImages);
        Sources.Remove(source);
    }
}
