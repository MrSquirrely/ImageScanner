using System.Windows.Media;
using System.Windows.Media.Imaging;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Viewer;

/// <summary>
/// Loads a still image at full size: with Windows' decoder for the formats it knows (turned the way the camera's
/// orientation tag says), and through ImageSharp for the rest (WebP, TGA, QOI ...).
/// </summary>
internal static class StillLoader {
    private static readonly HashSet<string> NativeExtensions =
        new([".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico"], StringComparer.OrdinalIgnoreCase);

    private const string OrientationQuery = "/app1/ifd/{ushort=274}";

    public static BitmapSource Load(string path) {
        if (NativeExtensions.Contains(Path.GetExtension(path))) {
            try {
                return LoadNative(path);
            }
            catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException or OverflowException) {
                // A file Windows can't decode after all: let ImageSharp try
            }
        }

        using MemoryStream stream = new(ImageProcessing.EncodePngForDisplay(path, 8192));
        return Decode(stream);
    }

    private static BitmapSource LoadNative(string path) {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Decode(stream);
    }

    private static BitmapSource Decode(Stream stream) {
        BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource source = frame;
        Transform? turn = TransformFor(ReadOrientation(frame));
        if (turn is not null) {
            source = new TransformedBitmap(frame, turn);
        }

        // A copy of the pixels with no tie to the decoder, which belongs to this background thread: the window
        // can then rotate it on the UI thread
        WriteableBitmap pixels = new(source);
        pixels.Freeze();
        return pixels;
    }

    private static int ReadOrientation(BitmapFrame frame) {
        try {
            return (frame.Metadata is BitmapMetadata metadata) && metadata.ContainsQuery(OrientationQuery) && (metadata.GetQuery(OrientationQuery) is ushort value)
                       ? value
                       : 1;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException) {
            return 1;
        }
    }

    /// <summary>EXIF orientation 2-8 as the transform that puts the picture upright; null for 1 (already upright).</summary>
    private static Transform? TransformFor(int orientation) {
        return orientation switch {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = [new RotateTransform(90), new ScaleTransform(-1, 1)] },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = [new RotateTransform(270), new ScaleTransform(-1, 1)] },
            8 => new RotateTransform(270),
            _ => null
        };
    }
}