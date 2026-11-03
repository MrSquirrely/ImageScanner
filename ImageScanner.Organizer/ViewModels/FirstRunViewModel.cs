using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Services;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.ViewModels;

public partial class WizardPluginItem(PluginInfo info, bool isEnabled) : ObservableObject {
    public PluginInfo Info { get; } = info;
    public string Name => Info.Plugin.Name;
    public string Description => Info.Plugin.Description;
    public string Source => Info.IsBuiltIn ? "Built-in" : $"{Info.Plugin.Author} · v{Info.Plugin.Version}";

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = isEnabled;
}

/// <summary>
/// A tagging engine offered on the wizard's AI step: a plugin implementing <see cref="ITagProviderPlugin"/>, or "None".
/// </summary>
public class TaggingEngineOption(PluginInfo? info, string name, string summary, IReadOnlyList<PluginSettingViewModel> settings) {
    public PluginInfo? Info { get; } = info;
    public string Name { get; } = name;
    public string Summary { get; } = summary;
    public IReadOnlyList<PluginSettingViewModel> Settings { get; } = settings;
    public bool HasSettings => Settings.Count > 0;
    public string? ProviderId => (Info?.Plugin as ITagProviderPlugin)?.ProviderId;
}

/// <summary>
/// First-run setup: theme, library location, AI tagging, viewer and plugins.
/// </summary>
public partial class FirstRunViewModel : ObservableObject {
    public static readonly string[] StepNames = ["Welcome", "Library", "AI tagging", "Viewing", "Plugins", "Finish"];

    private readonly AppSettings settings;

    public FirstRunViewModel(AppSettings settings, PluginManager plugins) {
        this.settings = settings;
        DarkTheme = settings.DarkTheme;
        LibraryPath = settings.LibraryPath;
        UseSystemViewer = settings.UseSystemViewer;
        Plugins = [.. plugins.Plugins.Select(info => new WizardPluginItem(info, !settings.DisabledPlugins.Contains(info.Plugin.Id)))];

        TaggingEngines = [
            // The default engine is listed first
            .. plugins.Plugins
                      .Where(info => info.Plugin is ITagProviderPlugin)
                      .OrderByDescending(info => ((ITagProviderPlugin)info.Plugin).ProviderId == AppSettings.DefaultTagProviderId)
                      .Select(info => new TaggingEngineOption(
                          info, info.Plugin.Name, ((ITagProviderPlugin)info.Plugin).ProviderSummary,
                          info.Plugin is IConfigurablePlugin configurable
                              ? [.. configurable.Settings.Select(setting => new PluginSettingViewModel(setting, plugins.GetSetting(info.Plugin, setting.Key)))]
                              : [])),
            new TaggingEngineOption(null, "Don't use AI tagging", "You can still tag images by hand, and turn AI tagging on later in Settings.", [])
        ];
        SelectedEngine = TaggingEngines.FirstOrDefault(engine => engine.ProviderId == settings.ActiveTagProviderId)
                         ?? TaggingEngines.FirstOrDefault(engine => engine.ProviderId == AppSettings.DefaultTagProviderId)
                         ?? TaggingEngines[0];
    }

    public string[] Steps => StepNames;
    public ObservableCollection<WizardPluginItem> Plugins { get; }
    public IReadOnlyList<TaggingEngineOption> TaggingEngines { get; }

    /// <summary>True when the user finished the wizard and asked to import a folder straight away.</summary>
    public bool StartImport => ImportAfterSetup;

    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep), nameof(IsLastStep))]
    public partial int StepIndex { get; set; }

    [ObservableProperty]
    public partial bool DarkTheme { get; set; }

    [ObservableProperty]
    public partial string LibraryPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesAi))]
    public partial TaggingEngineOption SelectedEngine { get; set; }

    public bool UsesAi => SelectedEngine.Info != null;

    [ObservableProperty]
    public partial bool SuggestOnImport { get; set; } = true;

    [ObservableProperty]
    public partial bool AutoApply { get; set; }

    [ObservableProperty]
    public partial bool UseSystemViewer { get; set; }

    [ObservableProperty]
    public partial bool ImportAfterSetup { get; set; } = true;

    public bool IsFirstStep => StepIndex == 0;
    public bool IsLastStep => StepIndex == StepNames.Length - 1;

    partial void OnDarkThemeChanged(bool value) => App.ApplyTheme(value);

    [RelayCommand]
    private void Next() {
        if (IsLastStep) {
            Finish();
        }
        else {
            StepIndex++;
        }
    }

    [RelayCommand]
    private void Back() {
        if (!IsFirstStep) StepIndex--;
    }

    [RelayCommand]
    private void BrowseLibrary() {
        Microsoft.Win32.SaveFileDialog dialog = new() {
            Title = "Where should the library database be stored?",
            Filter = "Library database (*.db)|*.db",
            FileName = Path.GetFileName(LibraryPath),
            InitialDirectory = Path.GetDirectoryName(LibraryPath),
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == true) {
            LibraryPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Keeps the defaults but marks setup as done so the wizard doesn't appear again.
    /// </summary>
    [RelayCommand]
    private void Skip() {
        ImportAfterSetup = false;
        settings.FirstRunCompleted = true;
        settings.Save();
        CloseRequested?.Invoke(true);
    }

    private void Finish() {
        settings.DarkTheme = DarkTheme;
        settings.LibraryPath = string.IsNullOrWhiteSpace(LibraryPath) ? settings.LibraryPath : LibraryPath.Trim();
        settings.UseSystemViewer = UseSystemViewer;
        settings.SuggestTagsOnImport = UsesAi && SuggestOnImport;
        settings.AutoApplySuggestions = UsesAi && AutoApply;
        if (SelectedEngine.Info is PluginInfo engine) {
            settings.ActiveTagProviderId = SelectedEngine.ProviderId;
            foreach (PluginSettingViewModel setting in SelectedEngine.Settings) {
                settings.SetPluginSetting(engine.Plugin.Id, setting.Definition.Key, setting.Value, setting.IsSecret);
            }

            // The chosen engine must be enabled even if it was switched off on the Plugins step
            Plugins.First(plugin => plugin.Info == engine).IsEnabled = true;
        }

        settings.DisabledPlugins = [.. Plugins.Where(plugin => !plugin.IsEnabled).Select(plugin => plugin.Info.Plugin.Id)];
        settings.FirstRunCompleted = true;
        settings.Save();
        CloseRequested?.Invoke(true);
    }
}
