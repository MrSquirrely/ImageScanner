using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;

namespace ImageScanner.Organizer.Views;

/// <summary>
/// Full-size viewer with zoom/pan (HandyControl ImageViewer) and arrow-key navigation through the current results.
/// </summary>
public class ImageViewerWindow : HandyControl.Controls.Window {
    private readonly IReadOnlyList<LibraryImage> images;
    private readonly ImageViewer viewer = new() { ShowImgMap = true };
    private readonly TextBlock caption = new() { Margin = new Thickness(12, 6, 12, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private int index;
    private int loadVersion;

    public ImageViewerWindow(IReadOnlyList<LibraryImage> images, int startIndex) {
        this.images = images;
        index = startIndex;
        Width = 1100;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "RegionBrush");

        DockPanel root = new();
        DockPanel.SetDock(caption, Dock.Bottom);
        root.Children.Add(caption);
        root.Children.Add(viewer);
        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += async (_, _) => await ShowCurrentAsync();
    }

    private async Task ShowCurrentAsync() {
        if (images.Count == 0) return;
        int version = ++loadVersion;
        LibraryImage image = images[index];
        Title = image.FileName;
        caption.Text = $"{index + 1} / {images.Count}   ·   {image.FilePath}   ·   {image.Dimensions}   ·   ← → to browse, Esc to close";

        BitmapSource? bitmap = await ImageLoader.LoadFullAsync(image.FilePath);
        if (version == loadVersion && bitmap != null) {
            viewer.ImageSource = BitmapFrame.Create(bitmap);
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Right or Key.PageDown or Key.Space when index < images.Count - 1:
                index++;
                e.Handled = true;
                await ShowCurrentAsync();
                break;
            case Key.Left or Key.PageUp or Key.Back when index > 0:
                index--;
                e.Handled = true;
                await ShowCurrentAsync();
                break;
            case Key.Escape:
                Close();
                break;
        }
    }
}
