using System.IO;
using System.Windows.Media.Imaging;

using ImageScanner.Core.Library;

namespace ImageScanner.Organizer.Services;

/// <summary>
/// Decodes thumbnails and full images off the UI thread. Thumbnails are cached (LRU) so scrolling
/// back through the gallery doesn't hit the database again.
/// </summary>
public sealed class ImageLoader(ImageLibrary library) {
    private const int CacheCapacity = 3000;

    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image)>> cache = [];
    private readonly LinkedList<(string Key, BitmapSource Image)> lru = [];
    private readonly Lock cacheLock = new();
    private readonly SemaphoreSlim decodeGate = new(Math.Max(2, Environment.ProcessorCount / 2));

    public async Task<BitmapSource?> LoadThumbnailAsync(LibraryImage image) {
        string? key = image.ThumbnailId;
        if (key == null) {
            return null;
        }

        lock (cacheLock) {
            if (cache.TryGetValue(key, out LinkedListNode<(string Key, BitmapSource Image)>? node)) {
                lru.Remove(node);
                lru.AddFirst(node);
                return node.Value.Image;
            }
        }

        await decodeGate.WaitAsync();
        try {
            BitmapSource? bitmap = await Task.Run(() => {
                byte[]? bytes = library.LoadThumbnail(key);
                return bytes == null ? null : Decode(bytes, 0);
            });

            if (bitmap != null) {
                lock (cacheLock) {
                    if (!cache.ContainsKey(key)) {
                        cache[key] = lru.AddFirst((key, bitmap));
                        if (cache.Count > CacheCapacity) {
                            cache.Remove(lru.Last!.Value.Key);
                            lru.RemoveLast();
                        }
                    }
                }
            }

            return bitmap;
        }
        finally {
            decodeGate.Release();
        }
    }

    /// <summary>
    /// Loads a full image for preview, decoding at most <paramref name="decodeWidth"/> pixels wide.
    /// Formats WPF can't read (WebP without the Windows codec, TGA, QOI) go through ImageSharp.
    /// </summary>
    public static Task<BitmapSource?> LoadFullAsync(string path, int decodeWidth = 0) => Task.Run(() => {
        if (!File.Exists(path)) {
            return null;
        }

        try {
            return Decode(File.ReadAllBytes(path), decodeWidth);
        }
        catch (Exception) {
            try {
                return Decode(ImageProcessing.EncodePngForDisplay(path), decodeWidth);
            }
            catch (Exception) {
                return null;
            }
        }
    });

    private static BitmapSource Decode(byte[] bytes, int decodeWidth) {
        using MemoryStream stream = new(bytes);
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0) {
            bitmap.DecodePixelWidth = decodeWidth;
        }
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
