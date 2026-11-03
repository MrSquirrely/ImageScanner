using ImageMagick;

namespace ImageScanner.Plugins.Converter;

public enum TargetFormat {
    Avif,
    WebP,
    JpegXl,
    Jpeg,
    Png
}

public enum OutputLocation {
    ConvertedSubfolder,
    NextToOriginal,
    ChosenFolder
}

public sealed record ConvertOptions(TargetFormat Format, int Quality, OutputLocation Location, string? Folder, bool KeepOnlyIfSmaller, bool KeepMetadata);

/// <param name="ConvertedBytes">Size of the new file, or null when it failed or was discarded.</param>
public sealed record ConversionResult(string Source, string? Output, long OriginalBytes, long? ConvertedBytes, string? Note) {
    public bool Succeeded => ConvertedBytes != null;
}

/// <summary>
/// Converts images with ImageMagick. Originals are never modified; new files get the same name with the new
/// extension (plus " (2)", " (3)" … if that name is taken).
/// </summary>
public static class ImageConverter {
    public static string DisplayName(TargetFormat format) => format switch {
        TargetFormat.Avif => "AVIF (smallest, newer apps)",
        TargetFormat.WebP => "WebP (small, works almost everywhere)",
        TargetFormat.JpegXl => "JPEG XL",
        TargetFormat.Jpeg => "JPEG",
        _ => "PNG (lossless)"
    };

    public static string Extension(TargetFormat format) => format switch {
        TargetFormat.Avif => ".avif",
        TargetFormat.WebP => ".webp",
        TargetFormat.JpegXl => ".jxl",
        TargetFormat.Jpeg => ".jpg",
        _ => ".png"
    };

    public static string DisplayName(OutputLocation location) => location switch {
        OutputLocation.ConvertedSubfolder => "In a \"converted\" folder next to each image",
        OutputLocation.NextToOriginal => "Next to the original",
        _ => "In a folder I choose"
    };

    public static bool UsesQuality(TargetFormat format) => format != TargetFormat.Png;

    public static async Task<IReadOnlyList<ConversionResult>> ConvertAsync(IReadOnlyList<string> files, ConvertOptions options,
                                                                           IProgress<ConversionResult>? progress, CancellationToken cancellationToken) {
        ConversionResult[] results = new ConversionResult[files.Count];

        // Encoders (AVIF especially) already use several cores per image; two images at a time keeps the PC usable
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), new ParallelOptions {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 2),
            CancellationToken = cancellationToken
        }, (index, token) => {
            results[index] = ConvertOne(files[index], options, token);
            progress?.Report(results[index]);
            return ValueTask.CompletedTask;
        });

        return results;
    }

    public static ConversionResult ConvertOne(string source, ConvertOptions options, CancellationToken cancellationToken) {
        long originalBytes = File.Exists(source) ? new FileInfo(source).Length : 0;
        if (originalBytes == 0) {
            return new ConversionResult(source, null, 0, null, "File not found");
        }

        if (string.Equals(Path.GetExtension(source), Extension(options.Format), StringComparison.OrdinalIgnoreCase)
            || options.Format == TargetFormat.Jpeg && Path.GetExtension(source).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) {
            return new ConversionResult(source, null, originalBytes, null, "Already in this format");
        }

        string? output = null;
        try {
            string folder = options.Location switch {
                OutputLocation.NextToOriginal => Path.GetDirectoryName(source)!,
                OutputLocation.ChosenFolder when !string.IsNullOrWhiteSpace(options.Folder) => options.Folder,
                _ => Path.Combine(Path.GetDirectoryName(source)!, "converted")
            };
            Directory.CreateDirectory(folder);
            output = UniquePath(Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + Extension(options.Format)));

            cancellationToken.ThrowIfCancellationRequested();
            using (MagickImage image = new(source)) {
                // Bake in the EXIF rotation so the result looks the same even where metadata is dropped
                image.AutoOrient();
                if (!options.KeepMetadata) {
                    image.Strip();
                }

                if (UsesQuality(options.Format)) {
                    image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);
                }

                if (options.Format == TargetFormat.Avif) {
                    // 0 (slowest, smallest) … 9 (fastest); 6 is a good balance for batches
                    image.Settings.SetDefine(MagickFormat.Heic, "speed", "6");
                }

                image.Write(output, options.Format switch {
                    TargetFormat.Avif => MagickFormat.Avif,
                    TargetFormat.WebP => MagickFormat.WebP,
                    TargetFormat.JpegXl => MagickFormat.Jxl,
                    TargetFormat.Jpeg => MagickFormat.Jpeg,
                    _ => MagickFormat.Png
                });
            }

            long convertedBytes = new FileInfo(output).Length;
            if (options.KeepOnlyIfSmaller && convertedBytes >= originalBytes) {
                File.Delete(output);
                return new ConversionResult(source, null, originalBytes, null, $"Not kept: {FormatSize(convertedBytes)} isn't smaller");
            }

            return new ConversionResult(source, output, originalBytes, convertedBytes, null);
        }
        catch (Exception exception) when (exception is MagickException or IOException or UnauthorizedAccessException) {
            if (output != null && File.Exists(output)) {
                File.Delete(output);
            }

            return new ConversionResult(source, null, originalBytes, null, exception is MagickException ? "Couldn't read or encode this image" : exception.Message);
        }
    }

    private static string UniquePath(string path) {
        string folder = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string candidate = path;
        for (int number = 2; File.Exists(candidate); number++) {
            candidate = Path.Combine(folder, $"{name} ({number}){extension}");
        }

        return candidate;
    }

    public static string FormatSize(long bytes) => bytes switch {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B"
    };

    /// <summary>"75% smaller" / "12% larger" / "same size".</summary>
    public static string Change(long original, long converted) {
        if (original <= 0) return string.Empty;
        double change = (converted - original) * 100.0 / original;
        return Math.Abs(change) < 0.5 ? "same size" : change < 0 ? $"{-change:0}% smaller" : $"{change:0}% larger";
    }
}
