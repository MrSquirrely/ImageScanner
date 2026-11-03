using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;

using DuplaImage.Lib;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Duplicates;

public enum HashAlgorithm {
    Difference,
    Dct,
    Median,
    Average
}

public sealed record DuplicateGroup(IReadOnlyList<LibraryImage> Images, double Similarity);

public sealed record DuplicateScanResult(IReadOnlyList<DuplicateGroup> Groups, int Hashed, int Unreadable);

/// <summary>
/// Finds groups of visually similar images with DuplaImage.Lib's perceptual hashes. Hashes are cached per file
/// (path, size and modified time), so a rescan only hashes new or changed images.
/// </summary>
public sealed class DuplicateScanner(string cacheDirectory) {
    private readonly ImageHashes hasher = new(new ImageSharpTransformer());

    private string CachePath => Path.Combine(cacheDirectory, "hashes.json");

    public static string DisplayName(HashAlgorithm algorithm) => algorithm switch {
        HashAlgorithm.Difference => "Difference (fast, recommended)",
        HashAlgorithm.Dct => "DCT / pHash (tolerates edits best, slower)",
        HashAlgorithm.Median => "Median",
        _ => "Average (fastest, more false matches)"
    };

    public async Task<DuplicateScanResult> ScanAsync(IReadOnlyList<LibraryImage> images, HashAlgorithm algorithm, double minimumSimilarity,
                                            IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken) {
        Dictionary<string, CachedHash> cache = LoadCache();
        ConcurrentDictionary<int, ulong> hashes = new();
        int done = 0;
        int unreadable = 0;

        await Parallel.ForEachAsync(images, new ParallelOptions {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            CancellationToken = cancellationToken
        }, (image, token) => {
            ulong? hash = HashOf(image, algorithm, cache);
            if (hash is ulong value) {
                hashes[image.Id] = value;
            }
            else {
                Interlocked.Increment(ref unreadable);
            }

            int current = Interlocked.Increment(ref done);
            if (current % 25 == 0 || current == images.Count) {
                progress?.Report((current, images.Count));
            }

            return ValueTask.CompletedTask;
        });

        SaveCache(cache);
        List<DuplicateGroup> groups = await Task.Run(() => Group(images, hashes, minimumSimilarity), cancellationToken);
        return new DuplicateScanResult(groups, hashes.Count, unreadable);
    }

    private ulong? HashOf(LibraryImage image, HashAlgorithm algorithm, Dictionary<string, CachedHash> cache) {
        FileInfo file = new(image.FilePath);
        if (!file.Exists) {
            return null;
        }

        string key = $"{algorithm}|{image.FilePath}";
        lock (cache) {
            if (cache.TryGetValue(key, out CachedHash? cached) && cached.Size == file.Length && cached.Modified == file.LastWriteTimeUtc.Ticks) {
                return cached.Hash;
            }
        }

        try {
            // The library's path overloads never close the file, which would block deleting it; pass our own stream
            using FileStream stream = new(image.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            ulong hash = algorithm switch {
                HashAlgorithm.Dct => hasher.CalculateDctHash(stream),
                HashAlgorithm.Median => hasher.CalculateMedianHash64(stream),
                HashAlgorithm.Average => hasher.CalculateAverageHash64(stream),
                _ => hasher.CalculateDifferenceHash64(stream)
            };

            lock (cache) {
                cache[key] = new CachedHash(file.Length, file.LastWriteTimeUtc.Ticks, hash);
            }

            return hash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SixLabors.ImageSharp.ImageFormatException
                                              or SixLabors.ImageSharp.UnknownImageFormatException or InvalidOperationException or NotSupportedException) {
            return null;
        }
    }

    /// <summary>
    /// Links every pair of images whose hashes are similar enough (union-find), so A~B and B~C end up in one group.
    /// Similarity is the share of equal bits, as DuplaImage.Lib's CompareHashes reports it.
    /// </summary>
    public static List<DuplicateGroup> Group(IReadOnlyList<LibraryImage> images, IReadOnlyDictionary<int, ulong> hashes, double minimumSimilarity) {
        List<LibraryImage> hashed = [.. images.Where(image => hashes.ContainsKey(image.Id))];
        ulong[] values = [.. hashed.Select(image => hashes[image.Id])];
        int maxDistance = (int)Math.Floor((1 - minimumSimilarity) * 64 + 1e-9);
        int[] parent = [.. Enumerable.Range(0, hashed.Count)];
        Dictionary<int, int> worstDistance = [];

        int Find(int index) {
            while (parent[index] != index) {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        for (int i = 0; i < values.Length; i++) {
            for (int j = i + 1; j < values.Length; j++) {
                int distance = BitOperations.PopCount(values[i] ^ values[j]);
                if (distance > maxDistance) {
                    continue;
                }

                int a = Find(i), b = Find(j);
                int worst = Math.Max(distance, Math.Max(worstDistance.GetValueOrDefault(a), worstDistance.GetValueOrDefault(b)));
                if (a != b) {
                    parent[b] = a;
                }

                worstDistance[a] = worst;
            }
        }

        return [.. Enumerable.Range(0, hashed.Count)
                             .GroupBy(Find)
                             .Where(group => group.Count() > 1)
                             .Select(group => new DuplicateGroup(
                                 [.. group.Select(index => hashed[index]).OrderByDescending(Quality)],
                                 1 - worstDistance.GetValueOrDefault(group.Key) / 64.0))
                             .OrderByDescending(group => group.Images.Count)
                             .ThenByDescending(group => group.Similarity)];
    }

    /// <summary>Which copy to keep: the most pixels, then the biggest file (usually the least compressed).</summary>
    public static long Quality(LibraryImage image) => (long)image.Width * image.Height * 1000 + Math.Min(image.FileSize / 1024, 999);

    #region Cache

    private sealed record CachedHash(long Size, long Modified, ulong Hash);

    private Dictionary<string, CachedHash> LoadCache() {
        try {
            return File.Exists(CachePath)
                       ? JsonSerializer.Deserialize<Dictionary<string, CachedHash>>(File.ReadAllText(CachePath)) ?? []
                       : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException) {
            return [];
        }
    }

    private void SaveCache(Dictionary<string, CachedHash> cache) {
        try {
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
        }
        catch (IOException) {
            // Only a speed-up; the next scan hashes again
        }
    }

    #endregion
}
