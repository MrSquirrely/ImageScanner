namespace ImageScanner.Core.Library;

/// <summary>
/// What the scanner needs to know about a file it can't read itself (a video, for example).
/// </summary>
/// <param name="Width">Frame width in pixels, or 0 when unknown.</param>
/// <param name="Height">Frame height in pixels, or 0 when unknown.</param>
/// <param name="Thumbnail">A JPEG no larger than the requested thumbnail size.</param>
/// <param name="Duration">Running time for videos; null for still images.</param>
public sealed record MediaInfo(int Width, int Height, byte[] Thumbnail, TimeSpan? Duration = null);

/// <summary>
/// Teaches the scanner extra file types. The scanner imports files with these extensions and asks the reader for
/// their size, duration and thumbnail instead of decoding them as images.
/// </summary>
public interface IMediaReader {
    /// <summary>Extensions with the leading dot, e.g. ".mp4". Matched without regard to case.</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>True when the files are videos: they get a play badge and open in a video-capable viewer.</summary>
    bool IsVideo { get; }

    /// <summary>
    /// Reads one file. Called from several threads at once. Throw when the file can't be read: the scanner counts
    /// it as unreadable and carries on.
    /// </summary>
    MediaInfo Read(string path, int thumbnailSize);
}