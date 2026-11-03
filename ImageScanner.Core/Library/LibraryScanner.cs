using System.IO.Enumeration;

namespace ImageScanner.Core.Library;

public class ScanOptions {
    public required string RootPath { get; init; }
    public IReadOnlyCollection<string> IgnoredFolders { get; init; } = [];
    public bool RemoveMissing { get; init; } = true;
    public bool DetectComics { get; init; } = true;
    public int MinimumComicPages { get; init; } = 3;
    public int ThumbnailSize { get; init; } = 256;
    public int MaxParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
}

public record ScanProgress(string Phase, int Processed, int Total, string? CurrentFile = null);

public record ScanResult(int Added, int Updated, int Removed, int Failed, List<Comic> ComicsCreated, List<LibraryImage> NewImages);

/// <summary>
/// Imports a folder or whole drive into an <see cref="ImageLibrary"/>. Unchanged files are skipped,
/// changed files are refreshed without losing tags, and new files get a thumbnail.
/// </summary>
public class LibraryScanner(ImageLibrary library) {
    private const int SaveBatchSize = 250;

    public async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) {
        string root = Path.GetFullPath(options.RootPath);
        if (!Directory.Exists(root)) {
            throw new DirectoryNotFoundException(root);
        }

        progress?.Report(new ScanProgress("Finding images", 0, 0));
        List<string> files = await Task.Run(() => EnumerateImages(root, options.IgnoredFolders, cancellationToken), cancellationToken);

        HashSet<string> seen = new(files, StringComparer.OrdinalIgnoreCase);
        List<(string Path, LibraryImage? Existing)> work = [];
        foreach (string file in files) {
            LibraryImage? existing = library.GetImageByPath(file);
            if (existing != null) {
                FileInfo info = new(file);
                bool unchanged = (info.LastWriteTimeUtc - existing.LastModified).Duration() < TimeSpan.FromSeconds(1)
                                 && info.Length == existing.FileSize
                                 && !string.IsNullOrEmpty(existing.ThumbnailId);
                if (unchanged) {
                    continue;
                }
            }

            work.Add((file, existing));
        }

        int processed = 0;
        int failed = 0;
        int updated = 0;
        List<LibraryImage> newImages = [];
        List<LibraryImage> pending = [];
        Lock batchLock = new();

        void Flush() {
            List<LibraryImage> toSave;
            lock (batchLock) {
                toSave = [.. pending];
                pending.Clear();
            }

            library.Save(toSave, raiseChanged: false);
        }

        await Parallel.ForEachAsync(work, new ParallelOptions {
            MaxDegreeOfParallelism = options.MaxParallelism,
            CancellationToken = cancellationToken
        }, (item, _) => {
            try {
                FileInfo info = new(item.Path);
                (int width, int height, string? maker, string? model, byte[] thumbnail) = ImageProcessing.ReadForLibrary(item.Path, options.ThumbnailSize);

                LibraryImage image = item.Existing ?? new LibraryImage {
                    FilePath = item.Path,
                    DateAdded = DateTime.UtcNow
                };

                library.DeleteThumbnail(image.ThumbnailId);
                image.ThumbnailId = library.SaveThumbnail(thumbnail, info.Name);
                image.FileName = info.Name;
                image.Folder = info.DirectoryName ?? string.Empty;
                image.Extension = info.Extension.ToLowerInvariant();
                image.FileSize = info.Length;
                image.LastModified = info.LastWriteTimeUtc;
                image.Width = width;
                image.Height = height;
                image.CameraMaker = maker;
                image.CameraModel = model;

                bool flushNow;
                lock (batchLock) {
                    pending.Add(image);
                    if (item.Existing == null) {
                        newImages.Add(image);
                    }
                    else {
                        updated++;
                    }
                    flushNow = pending.Count >= SaveBatchSize;
                }

                if (flushNow) {
                    Flush();
                }
            }
            catch (Exception) {
                // Corrupt, locked or unsupported files are skipped; the rest of the import continues
                Interlocked.Increment(ref failed);
            }

            int done = Interlocked.Increment(ref processed);
            if (done % 10 == 0 || done == work.Count) {
                progress?.Report(new ScanProgress("Importing", done, work.Count, item.Path));
            }

            return ValueTask.CompletedTask;
        });

        Flush();

        int removed = 0;
        if (options.RemoveMissing) {
            progress?.Report(new ScanProgress("Removing missing files", work.Count, work.Count));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            List<int> missing = [.. library.GetAllImages()
                                           .Where(image => image.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !seen.Contains(image.FilePath))
                                           .Select(image => image.Id)];
            if (missing.Count > 0) {
                library.DeleteImages(missing);
                removed = missing.Count;
            }
        }

        ScanSource source = library.GetSources().FirstOrDefault(existing => string.Equals(existing.RootPath, root, StringComparison.OrdinalIgnoreCase))
                            ?? new ScanSource { RootPath = root };

        List<Comic> comicsCreated = [];
        if (options.DetectComics) {
            progress?.Report(new ScanProgress("Detecting comics", work.Count, work.Count));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            HashSet<string> dismissed = new(library.GetSources().SelectMany(existing => existing.DismissedComicFolders), StringComparer.OrdinalIgnoreCase);
            IEnumerable<LibraryImage> candidatesPool = library.GetAllImages()
                                                              .Where(image => image.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                                                              && !dismissed.Contains(image.Folder));
            foreach (ComicCandidate candidate in ComicDetector.FindCandidates(candidatesPool, options.MinimumComicPages)) {
                comicsCreated.Add(library.CreateComic(candidate.Title, [.. candidate.Pages.Select(page => page.Id)], isAutoDetected: true, candidate.FolderPath));
            }
        }

        source.LastScanned = DateTime.UtcNow;
        source.ImageCount = files.Count - failed;
        source.IgnoredFolders = [.. options.IgnoredFolders];
        library.SaveSource(source);

        progress?.Report(new ScanProgress("Done", work.Count, work.Count));
        return new ScanResult(newImages.Count, updated, removed, failed, comicsCreated, newImages);
    }

    private static List<string> EnumerateImages(string root, IReadOnlyCollection<string> ignoredFolders, CancellationToken cancellationToken) {
        HashSet<string> ignored = new(ignoredFolders, StringComparer.OrdinalIgnoreCase);
        EnumerationOptions options = new() {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden
        };

        FileSystemEnumerable<string> enumerable = new(root, (ref FileSystemEntry entry) => entry.ToFullPath(), options) {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && ImageProcessing.SupportedExtensions.Contains(Path.GetExtension(entry.FileName).ToString()),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !ignored.Contains(entry.FileName.ToString())
                                                                    && !ignored.Contains(entry.ToFullPath())
        };

        List<string> files = [];
        foreach (string file in enumerable) {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(Path.GetFullPath(file));
        }

        return files;
    }
}
