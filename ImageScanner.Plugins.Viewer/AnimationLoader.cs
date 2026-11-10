using System.Windows.Media;
using System.Windows.Media.Imaging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageScanner.Plugins.Viewer;

/// <summary>The frames of an animated image, ready to show, with how long each stays on screen.</summary>
internal sealed record Animation(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays) {
    public TimeSpan Duration => TimeSpan.FromTicks(Delays.Sum(static delay => delay.Ticks));
}

/// <summary>
/// Decodes animated GIF, WebP and PNG files with ImageSharp (WPF only shows their first frame). ImageSharp hands
/// back every frame fully composed, so the frames can be shown one after another as they are.
/// </summary>
internal static class AnimationLoader {
    // Decoded frames are held in memory; bigger animations are scaled down to fit this
    private const long MaxDecodedBytes = 768L * 1024 * 1024;

    // Browsers treat very short delays as 100 ms; without this, "0 delay" GIFs spin flat out
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

    private static readonly HashSet<string> AnimatedExtensions = new([".gif", ".webp", ".png", ".apng"], StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the file has more than one frame. Reads only the file's headers.</summary>
    public static bool IsAnimated(string path) {
        if (!AnimatedExtensions.Contains(Path.GetExtension(path))) {
            return false;
        }

        try {
            return Image.Identify(path).FrameCount > 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageFormatException or NotSupportedException) {
            return false;
        }
    }

    public static Animation Load(string path, CancellationToken cancellationToken) {
        using Image<Bgra32> image = Image.Load<Bgra32>(new DecoderOptions(), path);
        long bytes = (long)image.Width * image.Height * 4 * image.Frames.Count;
        if (bytes > MaxDecodedBytes) {
            double scale = Math.Sqrt((double)MaxDecodedBytes / bytes);
            image.Mutate(context => context.Resize(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
        }

        List<BitmapSource> frames = [];
        List<TimeSpan> delays = [];
        int stride = image.Width * 4;
        for (int i = 0; i < image.Frames.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            ImageFrame<Bgra32> frame = image.Frames[i];
            byte[] pixels = new byte[stride * image.Height];
            frame.CopyPixelDataTo(pixels);
            BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            frames.Add(bitmap);
            delays.Add(DelayOf(frame.Metadata));
        }

        return new Animation(frames, delays);
    }

    private static TimeSpan DelayOf(ImageFrameMetadata metadata) {
        TimeSpan delay = metadata.DecodedImageFormat switch {
            GifFormat => TimeSpan.FromMilliseconds(metadata.GetGifMetadata().FrameDelay * 10.0),
            WebpFormat => TimeSpan.FromMilliseconds(metadata.GetWebpMetadata().FrameDelay),
            PngFormat => TimeSpan.FromSeconds(metadata.GetPngMetadata().FrameDelay.ToDouble()),
            _ => DefaultDelay
        };
        return (delay < MinimumDelay) ? DefaultDelay : delay;
    }
}