using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ImageScanner.Organizer.Services;

/// <summary>
/// User settings persisted as JSON under %LOCALAPPDATA%\ImageScanner\Organizer.
/// </summary>
public class AppSettings {
    private const string SecretPrefix = "dpapi:";

    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageScanner", "Organizer");

    private static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    // False until the first-run wizard has been completed (or skipped)
    public bool FirstRunCompleted { get; set; }

    public string LibraryPath { get; set; } = Path.Combine(AppDataDirectory, "library.db");
    public bool DarkTheme { get; set; }

    // Open images with the Windows default app instead of the built-in viewer
    public bool UseSystemViewer { get; set; }
    public double TileSize { get; set; } = 160;
    public string SortBy { get; set; } = "Date added";

    // Import defaults
    public bool DetectComics { get; set; } = true;
    public bool SuggestTagsOnImport { get; set; }
    public bool AutoApplySuggestions { get; set; }
    public string IgnoredFolders { get; set; } = "$RECYCLE.BIN; System Volume Information; node_modules; .git; Windows; Program Files; Program Files (x86); AppData";

    // AI tagging
    public string? ActiveTagProviderId { get; set; } = "builtin.claude";
    public int MaxSuggestedTags { get; set; } = 10;
    public int AiConcurrency { get; set; } = 3;

    public List<string> DisabledPlugins { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> PluginSettings { get; set; } = [];

    public static AppSettings Load() {
        try {
            if (File.Exists(SettingsPath)) {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
        }
        catch (Exception) {
            // A corrupt settings file shouldn't stop the app from starting; fall back to defaults
        }

        return new AppSettings();
    }

    public void Save() {
        Directory.CreateDirectory(AppDataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string? GetPluginSetting(string pluginId, string key) {
        if (!PluginSettings.TryGetValue(pluginId, out Dictionary<string, string>? values) || !values.TryGetValue(key, out string? value)) {
            return null;
        }

        if (!value.StartsWith(SecretPrefix, StringComparison.Ordinal)) {
            return value;
        }

        try {
            byte[] decrypted = ProtectedData.Unprotect(Convert.FromBase64String(value[SecretPrefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// Stores a plugin setting. Secrets are encrypted with DPAPI so they're only readable by this Windows user.
    /// </summary>
    public void SetPluginSetting(string pluginId, string key, string? value, bool isSecret = false) {
        if (!PluginSettings.TryGetValue(pluginId, out Dictionary<string, string>? values)) {
            values = [];
            PluginSettings[pluginId] = values;
        }

        if (string.IsNullOrEmpty(value)) {
            values.Remove(key);
            return;
        }

        values[key] = isSecret
                          ? SecretPrefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser))
                          : value;
    }

    public IEnumerable<string> GetIgnoredFolders() =>
        IgnoredFolders.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
