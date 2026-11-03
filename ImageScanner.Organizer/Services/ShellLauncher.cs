using System.ComponentModel;
using System.Diagnostics;

namespace ImageScanner.Organizer.Services;

/// <summary>
/// Opens files with the user's default Windows app.
/// </summary>
public static class ShellLauncher {
    /// <summary>
    /// Tries to open a file in its default app. Returns false with a user-facing reason when Windows can't,
    /// most commonly "Application not found" (CO_E_APPNOTFOUND) after the default app was uninstalled.
    /// </summary>
    public static bool TryOpenWithDefaultApp(string path, out string? error) {
        error = null;
        if (!File.Exists(path)) {
            error = $"The file no longer exists: {path}";
            return false;
        }

        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception) {
            error = $"Windows couldn't find a working default app for {Path.GetExtension(path).ToLowerInvariant()} files " +
                    "(it may have been uninstalled). Choose one under Windows Settings › Apps › Default apps.";
            return false;
        }
    }
}
