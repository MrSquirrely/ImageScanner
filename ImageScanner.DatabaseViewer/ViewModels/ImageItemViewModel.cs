using System.IO;
using System.Windows.Media.Imaging;

using ImageScanner.Core;

namespace ImageScanner.DatabaseViewer.ViewModels;

public class ImageItemViewModel {
    public ImageRecord Record { get; }
    public BitmapSource? Thumbnail { get; }
    public string DisplayDimensions => $"{Record.Width} × {Record.Height}";
    public string DisplayFileSize => $"{Record.FileSize / 1024.0:F1} KB";

    public ImageItemViewModel(ImageRecord record, ImageDatabaseReader reader) {
        Record = record;

        if (!string.IsNullOrEmpty(record.ThumbnailId)) {
            using MemoryStream? stream = reader.LoadThumbnailStream(record.ThumbnailId);
            if (stream != null) {
                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze(); // Crucial: Allows cross-thread data-binding and releases lock
                Thumbnail = image;
            }
        }
    }
}