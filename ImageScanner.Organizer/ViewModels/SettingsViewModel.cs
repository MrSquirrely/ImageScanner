using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Services;
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

    [ObservableProperty]
    public partial string? Value { get; set; } = value;

    public bool BoolValue {
        get => bool.TryParse(Value, out bool result) && result;
        set {
            Value = value.ToString();
            OnPropertyChanged();
        }
    }
}

public partial class PluginEntryViewModel : ObservableObject {
    private readonly PluginManager manager;

    public PluginEntryViewModel(PluginInfo info, PluginManager manager) {
        this.manager = manager;
        Info = info;
        isEnabled = info.IsEnabled;
        if (info.Plugin is IConfigurablePlugin configurable) {
            foreach (PluginSetting setting in configurable.Settings) {
                Settings.Add(new PluginSettingViewModel(setting, manager.GetSetting(info.Plugin, setting.Key)));
            }
        }
    }

    public PluginInfo Info { get; }
    public ObservableCollection<PluginSettingViewModel> Settings { get; } = [];
    public bool HasSettings => Settings.Count > 0;
    public string Header => $"{Info.Plugin.Name} {Info.Plugin.Version} · {Info.Plugin.Author}";

    private bool isEnabled;

    public bool IsEnabled {
        get => isEnabled;
        set {
            if (SetProperty(ref isEnabled, value)) {
                manager.SetEnabled(Info, value);
                Growl.Info("Restart Image Organizer to apply plugin changes.");
            }
        }
    }

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
        foreach (PluginEntryViewModel entry in PluginEntries) {
            entry.Save();
        }

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
