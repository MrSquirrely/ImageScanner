using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Plugins;

public partial class PluginInfo(IPlugin plugin, string source, bool isBuiltIn, bool isOfficial) : ObservableObject {
    public IPlugin Plugin { get; } = plugin;
    public string Source { get; } = source;
    public bool IsBuiltIn { get; } = isBuiltIn;

    /// <summary>
    /// Made by the PixShelf team: built into the app, or a DLL identical to one bundled with this build.
    /// </summary>
    public bool IsOfficial { get; } = isOfficial;

    /// <summary>Installed in the user's Plugins folder (from the Marketplace or by hand), so it can be uninstalled.</summary>
    public bool IsDownloaded => !IsBuiltIn && PluginInstaller.IsDownloaded(Source);

    /// <summary>Shipped with PixShelf: built in, or in the app's own Plugins folder.</summary>
    public bool IsIncluded => !IsDownloaded;

    /// <summary>Uninstalled during this session; the files are removed when PixShelf restarts.</summary>
    [ObservableProperty]
    public partial bool IsUninstallPending { get; set; } = !isBuiltIn && PluginInstaller.IsDownloaded(source)
                                                        && PluginInstaller.IsUninstallPending(PluginInstaller.FolderOf(source));

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string? Error { get; set; }

    public List<string> Features { get; } = [];

    public string FeatureSummary => Features.Count == 0 ? "No features registered" : string.Join(" · ", Features);

    public bool HasSettings => Plugin is IConfigurablePlugin { Settings.Count: > 0 };

    /// <summary>The icon as a font glyph, or null when the plugin supplied an image (or nothing usable).</summary>
    public string? IconGlyph => IconImage == null ? (Plugin.Icon is { Length: > 0 and <= 2 } glyph ? glyph : "") : null;

    /// <summary>The icon image when the plugin's Icon is a file path; resolved relative to the plugin's folder.</summary>
    public ImageSource? IconImage => field ??= LoadIconImage();

    private ImageSource? LoadIconImage() {
        string icon = Plugin.Icon;
        if (icon is not { Length: > 2 }) {
            return null;
        }

        try {
            string folder = IsBuiltIn ? AppContext.BaseDirectory : Path.GetDirectoryName(Source)!;
            string path = Path.IsPathRooted(icon) ? icon : Path.Combine(folder, icon);
            if (!File.Exists(path)) {
                return null;
            }

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 64;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) {
            // A broken icon file falls back to the default glyph
            return null;
        }
    }
}

/// <summary>
/// Discovers, loads and hosts plugins. Built-in features (Explorer actions) are
/// registered through the same API third-party plugins use.
/// </summary>
public sealed class PluginManager(AppSettings settings) : IPluginHost {
    private PluginInfo? initializing;

    public ObservableCollection<PluginInfo> Plugins { get; } = [];
    public List<ITagSuggestionProvider> TagProviders { get; } = [];
    public List<IImageAction> ImageActions { get; } = [];
    public List<IImportProcessor> ImportProcessors { get; } = [];
    public List<IPluginCommand> Commands { get; } = [];
    public List<IMediaReader> MediaReaders { get; } = [];
    public List<IMediaViewer> MediaViewers { get; } = [];

    public Func<IReadOnlyList<LibraryImage>> SelectionProvider { get; set; } = () => [];
    public Action<string> SearchHandler { get; set; } = _ => { };

    // Until the main window is up there's nothing to rescan with
    public Func<bool, Task<bool>> RescanHandler { get; set; } = _ => Task.FromResult(false);

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
            Add(new PluginInfo(plugin, "Built-in", isBuiltIn: true, isOfficial: true));
        }

        foreach (string directory in PluginDirectories.Where(Directory.Exists)) {
            foreach (string pluginFolder in Directory.EnumerateDirectories(directory)) {
                LoadFolder(pluginFolder);
            }
        }
    }

    private void LoadFolder(string folder) {
        // Convention: Plugins\<Name>\<Name>.dll is the entry assembly. Marketplace installs are named after the plugin id,
        // so otherwise use the DLLs that have their own .deps.json (a plugin's entry point; dependencies don't), and only
        // as a last resort try every DLL
        string conventional = Path.Combine(folder, Path.GetFileName(folder) + ".dll");
        List<string> withDeps = [.. Directory.EnumerateFiles(folder, "*.dll").Where(dll => File.Exists(Path.ChangeExtension(dll, ".deps.json")))];
        IEnumerable<string> candidates = File.Exists(conventional) ? [conventional]
                                         : withDeps.Count > 0 ? withDeps
                                         : Directory.EnumerateFiles(folder, "*.dll");

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

            bool isOfficial = IsOfficialAssembly(assemblyPath);
            foreach (Type type in pluginTypes) {
                try {
                    IPlugin plugin = (IPlugin)Activator.CreateInstance(type)!;
                    Add(new PluginInfo(plugin, assemblyPath, isBuiltIn: false, isOfficial));
                }
                catch (Exception exception) {
                    Growl.Error($"Plugin {type.FullName} failed to load: {exception.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Schedules a downloaded plugin's folder for removal on the next start (or takes that back).
    /// </summary>
    public void SetUninstalled(PluginInfo info, bool uninstall) {
        if (!info.IsDownloaded) {
            return;
        }

        string folder = PluginInstaller.FolderOf(info.Source);
        if (uninstall) {
            PluginInstaller.ScheduleUninstall(folder);
        }
        else {
            PluginInstaller.CancelUninstall(folder);
        }

        info.IsUninstallPending = uninstall;
    }

    /// <summary>
    /// True when the file is byte-for-byte one of the plugins bundled with this build (see GenerateOfficialPluginHashes),
    /// or one installed from a Marketplace post by the PixShelf team after its download hash was verified.
    /// </summary>
    private bool IsOfficialAssembly(string assemblyPath) {
        try {
            using FileStream stream = File.OpenRead(assemblyPath);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            return OfficialPluginHashes.Sha256.Contains(hash, StringComparer.OrdinalIgnoreCase)
                   || settings.TrustedPluginHashes.Contains(hash, StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException) {
            return false;
        }
    }

    /// <summary>
    /// Adds a discovered plugin. When the same plugin is found twice (an included plugin and a newer copy installed
    /// from the Marketplace), the higher version wins; uninstalling the update brings back the included one.
    /// </summary>
    private void Add(PluginInfo info) {
        int index = Plugins.ToList().FindIndex(existing => existing.Plugin.Id == info.Plugin.Id);
        if (index < 0) {
            Plugins.Add(info);
        }
        else if (info.Plugin.Version > Plugins[index].Plugin.Version) {
            Plugins[index] = info;
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

    public Task<bool> RescanLibraryAsync(bool quiet = false) =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => RescanHandler(quiet)).Task.Unwrap();

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

    public void RegisterMediaReader(IMediaReader reader) {
        MediaReaders.Add(reader);
        initializing?.Features.Add($"File types: {string.Join(" ", reader.Extensions)}");
    }

    public void RegisterMediaViewer(IMediaViewer viewer) {
        MediaViewers.Add(viewer);
        initializing?.Features.Add($"Viewer: {viewer.Name}");
    }

    #endregion
}
