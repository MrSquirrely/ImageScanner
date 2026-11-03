using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Plugins.BuiltIn;
using ImageScanner.Organizer.Services;

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
/// First-run setup: theme, library location, AI tagging, viewer and plugins.
/// </summary>
public partial class FirstRunViewModel : ObservableObject {
    public static readonly string[] StepNames = ["Welcome", "Library", "AI tagging", "Viewing", "Plugins", "Finish"];

    private readonly AppSettings settings;

    public FirstRunViewModel(AppSettings settings, PluginManager plugins) {
        this.settings = settings;
        DarkTheme = settings.DarkTheme;
        LibraryPath = settings.LibraryPath;
        ApiKey = settings.GetPluginSetting("builtin", BuiltInPlugin.ApiKeySetting) ?? string.Empty;
        HasEnvironmentKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        UseSystemViewer = settings.UseSystemViewer;
        Plugins = [.. plugins.Plugins.Select(info => new WizardPluginItem(info, !settings.DisabledPlugins.Contains(info.Plugin.Id)))];
    }

    public string[] Steps => StepNames;
    public ObservableCollection<WizardPluginItem> Plugins { get; }
    public bool HasEnvironmentKey { get; }

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
    public partial bool EnableAi { get; set; } = true;

    [ObservableProperty]
    public partial string ApiKey { get; set; }

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
        settings.SuggestTagsOnImport = EnableAi && SuggestOnImport;
        settings.AutoApplySuggestions = EnableAi && AutoApply;
        if (EnableAi) {
            settings.ActiveTagProviderId = ClaudeTagProvider.ProviderId;
            settings.SetPluginSetting("builtin", BuiltInPlugin.ApiKeySetting, ApiKey.Trim(), isSecret: true);
        }

        settings.DisabledPlugins = [.. Plugins.Where(plugin => !plugin.IsEnabled).Select(plugin => plugin.Info.Plugin.Id)];
        settings.FirstRunCompleted = true;
        settings.Save();
        CloseRequested?.Invoke(true);
    }
}
