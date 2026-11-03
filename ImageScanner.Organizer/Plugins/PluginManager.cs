using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Plugins;

public partial class PluginInfo(IPlugin plugin, string source, bool isBuiltIn) : ObservableObject {
    public IPlugin Plugin { get; } = plugin;
    public string Source { get; } = source;
    public bool IsBuiltIn { get; } = isBuiltIn;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string? Error { get; set; }

    public List<string> Features { get; } = [];

    public string FeatureSummary => Features.Count == 0 ? "No features registered" : string.Join(" · ", Features);
}

/// <summary>
/// Discovers, loads and hosts plugins. Built-in features (Claude tagging, Explorer actions) are
/// registered through the same API third-party plugins use.
/// </summary>
public sealed class PluginManager(AppSettings settings) : IPluginHost {
    private PluginInfo? initializing;

    public ObservableCollection<PluginInfo> Plugins { get; } = [];
    public List<ITagSuggestionProvider> TagProviders { get; } = [];
    public List<IImageAction> ImageActions { get; } = [];
    public List<IImportProcessor> ImportProcessors { get; } = [];
    public List<IPluginCommand> Commands { get; } = [];

    public Func<IReadOnlyList<LibraryImage>> SelectionProvider { get; set; } = () => [];
    public Action<string> SearchHandler { get; set; } = _ => { };

    public static IEnumerable<string> PluginDirectories => [
        Path.Combine(AppContext.BaseDirectory, "Plugins"),
        Path.Combine(AppSettings.AppDataDirectory, "Plugins")
    ];

    // Set before InitializeEnabled; the first-run wizard may change which library file is opened
    public ImageLibrary Library { get; set; } = null!;

    public IReadOnlyList<LibraryImage> SelectedImages => SelectionProvider();

    public ITagSuggestionProvider? ActiveTagProvider =>
        TagProviders.FirstOrDefault(provider => provider.Id == settings.ActiveTagProviderId) ?? TagProviders.FirstOrDefault();

    public void LoadAll(IEnumerable<IPlugin> builtInPlugins) {
        Discover(builtInPlugins);
        InitializeEnabled();
    }

    /// <summary>
    /// Finds built-in and folder plugins without initializing them, so the first-run wizard can list them.
    /// </summary>
    public void Discover(IEnumerable<IPlugin> builtInPlugins) {
        foreach (IPlugin plugin in builtInPlugins) {
            Add(new PluginInfo(plugin, "Built-in", isBuiltIn: true));
        }

        foreach (string directory in PluginDirectories.Where(Directory.Exists)) {
            foreach (string pluginFolder in Directory.EnumerateDirectories(directory)) {
                LoadFolder(pluginFolder);
            }
        }
    }

    private void LoadFolder(string folder) {
        // Convention: Plugins\<Name>\<Name>.dll is the entry assembly; otherwise try every DLL in the folder
        string conventional = Path.Combine(folder, Path.GetFileName(folder) + ".dll");
        IEnumerable<string> candidates = File.Exists(conventional) ? [conventional] : Directory.EnumerateFiles(folder, "*.dll");

        foreach (string assemblyPath in candidates) {
            string name = Path.GetFileNameWithoutExtension(assemblyPath);
            if (name.StartsWith("ImageScanner.Core", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ImageScanner.Plugins.Abstractions", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            Assembly assembly;
            Type[] pluginTypes;
            try {
                assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
                pluginTypes = [.. assembly.GetExportedTypes().Where(type => typeof(IPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })];
            }
            catch (Exception) {
                // Not a .NET assembly or not loadable; ignore quietly unless it's the conventional entry point
                continue;
            }

            foreach (Type type in pluginTypes) {
                try {
                    IPlugin plugin = (IPlugin)Activator.CreateInstance(type)!;
                    Add(new PluginInfo(plugin, assemblyPath, isBuiltIn: false));
                }
                catch (Exception exception) {
                    Growl.Error($"Plugin {type.FullName} failed to load: {exception.Message}");
                }
            }
        }
    }

    private void Add(PluginInfo info) {
        if (Plugins.All(existing => existing.Plugin.Id != info.Plugin.Id)) {
            Plugins.Add(info);
        }
    }

    /// <summary>
    /// Initializes every discovered plugin that isn't disabled in settings.
    /// </summary>
    public void InitializeEnabled() {
        foreach (PluginInfo info in Plugins) {
            info.IsEnabled = !settings.DisabledPlugins.Contains(info.Plugin.Id);
            if (info.IsEnabled) {
                Activate(info);
            }
        }
    }

    private void Activate(PluginInfo info) {
        initializing = info;
        try {
            info.Plugin.Initialize(this);
        }
        catch (Exception exception) {
            info.Error = exception.Message;
        }
        finally {
            initializing = null;
        }
    }

    public void SetEnabled(PluginInfo info, bool enabled) {
        settings.DisabledPlugins.Remove(info.Plugin.Id);
        if (!enabled) {
            settings.DisabledPlugins.Add(info.Plugin.Id);
        }

        settings.Save();
    }

    public void ShutdownAll() {
        foreach (PluginInfo info in Plugins.Where(info => info.IsEnabled)) {
            try {
                info.Plugin.Shutdown();
            }
            catch (Exception) {
                // Never let a plugin block app exit
            }
        }
    }

    #region IPluginHost

    public string GetDataDirectory(IPlugin plugin) {
        string directory = Path.Combine(AppSettings.AppDataDirectory, "PluginData", plugin.Id);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string? GetSetting(IPlugin plugin, string key) => settings.GetPluginSetting(plugin.Id, key)
                                                            ?? (plugin as IConfigurablePlugin)?.Settings.FirstOrDefault(setting => setting.Key == key)?.DefaultValue;

    public void SetSetting(IPlugin plugin, string key, string? value) {
        bool isSecret = (plugin as IConfigurablePlugin)?.Settings.Any(setting => setting.Key == key && setting.Kind == PluginSettingKind.Secret) == true;
        settings.SetPluginSetting(plugin.Id, key, value, isSecret);
        settings.Save();
    }

    public void Notify(string message, NotificationKind kind = NotificationKind.Info) {
        System.Windows.Application.Current.Dispatcher.Invoke(() => {
            switch (kind) {
                case NotificationKind.Success: Growl.Success(message); break;
                case NotificationKind.Warning: Growl.Warning(message); break;
                case NotificationKind.Error: Growl.Error(message); break;
                default: Growl.Info(message); break;
            }
        });
    }

    public void ShowSearch(string query) => System.Windows.Application.Current.Dispatcher.Invoke(() => SearchHandler(query));

    public void RegisterTagSuggestionProvider(ITagSuggestionProvider provider) {
        TagProviders.Add(provider);
        initializing?.Features.Add($"AI tagger: {provider.DisplayName}");
    }

    public void RegisterImageAction(IImageAction action) {
        ImageActions.Add(action);
        initializing?.Features.Add($"Action: {action.Name}");
    }

    public void RegisterImportProcessor(IImportProcessor processor) {
        ImportProcessors.Add(processor);
        initializing?.Features.Add($"Import step: {processor.Name}");
    }

    public void RegisterCommand(IPluginCommand command) {
        Commands.Add(command);
        initializing?.Features.Add($"Command: {command.Name}");
    }

    #endregion
}
