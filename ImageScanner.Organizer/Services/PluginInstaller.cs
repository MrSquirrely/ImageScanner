using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace ImageScanner.Organizer.Services;

/// <summary>
/// Installs marketplace plugins. A download is only accepted if its SHA-256 matches the listing, and it is
/// staged in a pending folder that's moved into place on the next start: a plugin that's already loaded keeps
/// its DLLs locked, so updating it in place isn't possible while the app runs.
/// </summary>
public sealed class PluginInstaller(AppSettings settings) {
    private const long MaxDownloadBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    public static string PluginsDirectory => Path.Combine(AppSettings.AppDataDirectory, "Plugins");
    public static string PendingDirectory => Path.Combine(AppSettings.AppDataDirectory, "Plugins.pending");

    public static bool IsPending(string pluginId) => Directory.Exists(Path.Combine(PendingDirectory, FolderName(pluginId)));

    /// <summary>
    /// Downloads, verifies and stages the listing's zip. Throws <see cref="InvalidOperationException"/> with a
    /// message for the user when the download can't be trusted or isn't a plugin.
    /// </summary>
    public async Task InstallAsync(MarketplaceListing listing, IProgress<string>? progress, CancellationToken cancellationToken) {
        if (!listing.IsInstallable) {
            throw new InvalidOperationException("This listing doesn't have a valid download link and SHA-256, so it can't be installed.");
        }

        string temp = Path.Combine(Path.GetTempPath(), $"pixshelf-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try {
            progress?.Report("Downloading…");
            string zipPath = Path.Combine(temp, "plugin.zip");
            await DownloadAsync(listing.DownloadUrl!, zipPath, progress, cancellationToken);
            progress?.Report("Checking…");
            await InstallZipAsync(listing, zipPath, cancellationToken);
        }
        finally {
            try {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException) {
                // Leftovers in %TEMP% are harmless
            }
        }
    }

    /// <summary>
    /// Verifies a downloaded zip against the listing's SHA-256 and stages its contents for the next start.
    /// </summary>
    public async Task InstallZipAsync(MarketplaceListing listing, string zipPath, CancellationToken cancellationToken) {
        string temp = Path.Combine(Path.GetTempPath(), $"pixshelf-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try {
            string actual;
            await using (FileStream stream = File.OpenRead(zipPath)) {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            }

            if (!string.Equals(actual, listing.Sha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    "The download doesn't match the SHA-256 in the listing, so it wasn't installed. The file may have been changed " +
                    "since the listing was posted; let the author know in the discussion.");
            }

            string staged = Path.Combine(temp, "staged");
            ExtractSafely(zipPath, staged);
            List<string> assemblies = [.. Directory.EnumerateFiles(staged, "*.dll", SearchOption.AllDirectories)];
            if (assemblies.Count == 0) {
                throw new InvalidOperationException("The download doesn't contain a plugin (no .dll files).");
            }

            // A zip of a folder: use the folder's contents so the plugin DLL sits directly in Plugins\<id>
            string root = Directory.GetFiles(staged).Length == 0 && Directory.GetDirectories(staged) is [string only] ? only : staged;

            string pending = Path.Combine(PendingDirectory, FolderName(listing.PluginId!));
            if (Directory.Exists(pending)) {
                Directory.Delete(pending, recursive: true);
            }

            Directory.CreateDirectory(PendingDirectory);
            Directory.Move(root, pending);

            if (listing.IsTeamPost) {
                // Posted by the PixShelf team and verified against the posted hash: show these DLLs as official
                foreach (string assembly in Directory.EnumerateFiles(pending, "*.dll", SearchOption.AllDirectories)) {
                    await using FileStream stream = File.OpenRead(assembly);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (!settings.TrustedPluginHashes.Contains(hash)) {
                        settings.TrustedPluginHashes.Add(hash);
                    }
                }

                settings.Save();
            }
        }
        finally {
            try {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException) {
                // Leftovers in %TEMP% are harmless
            }
        }
    }

    /// <summary>
    /// Moves staged plugins into the Plugins folder. Runs at startup before any plugin is loaded.
    /// </summary>
    public static void ApplyPendingInstalls() {
        if (!Directory.Exists(PendingDirectory)) {
            return;
        }

        foreach (string staged in Directory.EnumerateDirectories(PendingDirectory)) {
            string target = Path.Combine(PluginsDirectory, Path.GetFileName(staged));
            try {
                if (Directory.Exists(target)) {
                    Directory.Delete(target, recursive: true);
                }

                Directory.CreateDirectory(PluginsDirectory);
                Directory.Move(staged, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                // Something still holds the old files; try again next start
            }
        }

        if (!Directory.EnumerateFileSystemEntries(PendingDirectory).Any()) {
            Directory.Delete(PendingDirectory);
        }
    }

    private static async Task DownloadAsync(string url, string path, IProgress<string>? progress, CancellationToken cancellationToken) {
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"The download failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        // A redirect (as GitHub releases use) must not downgrade to plain HTTP
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) {
            throw new InvalidOperationException("The download was redirected to an insecure (non-HTTPS) address, so it was stopped.");
        }

        long? total = response.Content.Headers.ContentLength;
        if (total > MaxDownloadBytes) {
            throw new InvalidOperationException("The download is larger than 200 MB, which is more than a plugin should need.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(path);
        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0) {
            received += read;
            if (received > MaxDownloadBytes) {
                throw new InvalidOperationException("The download is larger than 200 MB, which is more than a plugin should need.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report(total > 0 ? $"Downloading… {received * 100 / total}%" : $"Downloading… {received / 1024:N0} KB");
        }
    }

    /// <summary>
    /// Extracts a zip, refusing entries that would land outside the target folder ("zip slip").
    /// </summary>
    private static void ExtractSafely(string zipPath, string destination) {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        try {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries) {
                string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("The download contains files that would be written outside the plugin folder, so it wasn't installed.");
                }

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) {
                    Directory.CreateDirectory(path);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }
        catch (InvalidDataException exception) {
            throw new InvalidOperationException("The download isn't a valid .zip file.", exception);
        }
    }

    /// <summary>A safe folder name from a plugin id ("you.watermark-finder" stays as it is).</summary>
    public static string FolderName(string pluginId) {
        string name = new([.. pluginId.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')]);
        return name.Trim('.') is { Length: > 0 } trimmed ? trimmed : "plugin";
    }

    private static HttpClient CreateClient() {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PixShelf", "1.0"));
        return client;
    }
}
