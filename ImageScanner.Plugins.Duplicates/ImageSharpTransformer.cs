using DuplaImage.Lib;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageScanner.Plugins.Duplicates;

/// <summary>
/// DuplaImage.Lib's image transformer on ImageSharp 4 (the version PixShelf shares with plugins): decodes the image
/// as 8-bit grayscale and resizes it to the hash size, ignoring the aspect ratio as the library expects.
/// </summary>
internal sealed class ImageSharpTransformer : IImageTransformer {
    public byte[] TransformImage(Stream stream, uint width, uint height) {
        // Decoding straight to a small size is much faster for large photos
        DecoderOptions options = new() { TargetSize = new Size(Math.Max(64, (int)width * 4), Math.Max(64, (int)height * 4)) };
        using Image<L8> image = Image.Load<L8>(options, stream);
        image.Mutate(context => context.Resize((int)width, (int)height, KnownResamplers.Bicubic));

        byte[] pixels = new byte[width * height];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }
}
