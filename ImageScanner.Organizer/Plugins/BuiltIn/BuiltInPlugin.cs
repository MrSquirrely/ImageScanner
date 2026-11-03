using System.Diagnostics;
using System.Windows;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Plugins.BuiltIn;

/// <summary>
/// Features that ship with the app, registered through the public plugin API.
/// </summary>
public sealed class BuiltInPlugin : IPlugin {
    public string Id => "builtin";
    public string Name => "Built-in features";
    public string Description => "Windows Explorer actions: open in the default app, show in Explorer, copy file path.";
    public string Author => "ImageScanner";
    public Version Version => new(1, 0);
    public string Icon => "\uE838";

    public void Initialize(IPluginHost host) {
        host.RegisterImageAction(new DelegateImageAction("Open in default app", images => {
            foreach (LibraryImage image in images.Take(10)) {
                if (!ShellLauncher.TryOpenWithDefaultApp(image.FilePath, out string? error)) {
                    host.Notify(error!, NotificationKind.Warning);
                    break;
                }
            }
        }));
        host.RegisterImageAction(new DelegateImageAction("Show in Explorer", images =>
            Process.Start("explorer.exe", $"/select,\"{images[0].FilePath}\"")));
        host.RegisterImageAction(new DelegateImageAction("Copy file path", images =>
            Clipboard.SetText(string.Join(Environment.NewLine, images.Select(image => image.FilePath)))));
    }

    private sealed class DelegateImageAction(string name, Action<IReadOnlyList<LibraryImage>> execute) : IImageAction {
        public string Name => name;

        public Task ExecuteAsync(IReadOnlyList<LibraryImage> images, CancellationToken cancellationToken) {
            execute(images);
            return Task.CompletedTask;
        }
    }
}
