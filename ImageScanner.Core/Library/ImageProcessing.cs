using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace ImageScanner.Core.Library;

/// <summary>
/// ImageSharp helpers shared by the scanner, the UI (for formats WPF can't decode) and AI tagging.
/// </summary>
public static class ImageProcessing {
    public static readonly HashSet<string> SupportedExtensions =
        new([".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".ico", ".tga", ".pbm", ".qoi"], StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Reads dimensions and EXIF and produces a JPEG thumbnail in one decode.
    /// </summary>
    public static (int Width, int Height, string? CameraMaker, string? CameraModel, byte[] Thumbnail) ReadForLibrary(string path, int thumbnailSize) {
        ImageInfo info = Image.Identify(path);
        string? maker = null;
        string? model = null;
        ExifProfile? exif = info.Metadata.ExifProfile;
        if (exif != null) {
            if (exif.TryGetValue(ExifTag.Make, out IExifValue<string>? makeValue)) maker = makeValue.Value?.Trim();
            if (exif.TryGetValue(ExifTag.Model, out IExifValue<string>? modelValue)) model = modelValue.Value?.Trim();
        }

        byte[] thumbnail = EncodeJpeg(path, thumbnailSize, 80);
        return (info.Width, info.Height, maker, model, thumbnail);
    }

    /// <summary>
    /// Decodes an image scaled so its longest edge is at most <paramref name="maxEdge"/> and re-encodes it as JPEG.
    /// </summary>
    public static byte[] EncodeJpeg(string path, int maxEdge, int quality) {
        DecoderOptions decoderOptions = new() {
            TargetSize = new Size(maxEdge, maxEdge),
            MaxFrames = 1
        };

        using Image image = Image.Load(decoderOptions, path);
        image.Mutate(context => {
            context.AutoOrient();
            if (image.Width > maxEdge || image.Height > maxEdge) {
                context.Resize(new ResizeOptions { Size = new Size(maxEdge, maxEdge), Mode = ResizeMode.Max });
            }

            // JPEG has no alpha; flatten transparent images onto white instead of black
            context.BackgroundColor(Color.White);
        });

        using MemoryStream output = new();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = quality });
        return output.ToArray();
    }

    /// <summary>
    /// Full-size PNG re-encode for previewing formats WPF can't decode natively (WebP, TGA, QOI ...).
    /// </summary>
    public static byte[] EncodePngForDisplay(string path, int maxEdge = 4096) {
        DecoderOptions decoderOptions = new() {
            TargetSize = new Size(maxEdge, maxEdge),
            MaxFrames = 1
        };

        using Image image = Image.Load(decoderOptions, path);
        image.Mutate(context => context.AutoOrient());
        using MemoryStream output = new();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }
}
