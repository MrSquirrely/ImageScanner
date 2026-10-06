using System.IO.Enumeration;

using LiteDB;

using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageScanner.Core;

/// <summary>
/// Scans directories for image files, extracts metadata, and saves it to a LiteDB database.
/// </summary>
/// <param name="databasePathValue">The path to the LiteDB database file.</param>
/// <param name="rootDirectoryValue">The root directory to scan for image files.</param>
/// <param name="ignoredFoldersValue">A list of folders to ignore during the scan.</param>
/// <param name="databasePassword">The password for the LiteDB database.</param>
public class DirectoryScanner(string databasePathValue, string rootDirectoryValue, IEnumerable<string>? ignoredFoldersValue = null, string? databasePassword = null) {
    private readonly HashSet<string> supportedExtensionsHashSet =
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".ico"];
    private readonly HashSet<string> ignoredFoldersHashSet =
        ignoredFoldersValue?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    /// <summary>
    /// Counts the number of image files in the specified root directory, including subdirectories, while ignoring specified folders.
    /// </summary>
    /// <param name="rootDirectory">The root directory to scan for image files.</param>
    /// <returns>The number of image files found.</returns>
    public int CountImageFiles(string rootDirectory) {
        EnumerationOptions options = new() {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        FileSystemEnumerable<string> enumerable = new(
            rootDirectory,
            (ref entry) => entry.FileName.ToString(),
            options) {
            ShouldIncludePredicate = (ref entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref entry) => !ignoredFoldersHashSet.Contains(entry.FileName.ToString())
        };

        return enumerable.Count(fileName => supportedExtensionsHashSet.Contains(Path.GetExtension(fileName).ToLowerInvariant()));
    }

    /// <summary>
    /// Scans the specified root directory for image files, extracts metadata, and saves it to the LiteDB database. Optionally generates thumbnails for the images.
    /// </summary>
    /// <param name="progress">The progress reporter for tracking the scanning process.</param>
    /// <param name="generateThumbnails">Indicates whether to generate thumbnails for the images.</param>
    public void ScanAndSave(IProgress<int>? progress = null, bool generateThumbnails = false) {
        string dbFileName = databasePathValue.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                                ? databasePathValue
                                : $"{databasePathValue}.db";

        string fullDbPath = Path.Combine(rootDirectoryValue, dbFileName);

        ConnectionString connectionString = new() {
            Filename = fullDbPath,
            Connection = ConnectionType.Shared
        };

        if (!string.IsNullOrWhiteSpace(databasePassword)) {
            connectionString.Password = databasePassword;
        }

        using LiteDatabase liteDatabase = new(connectionString);
        ILiteCollection<ImageRecord> collection = liteDatabase.GetCollection<ImageRecord>("images");
        collection.EnsureIndex(record => record.FilePath, true);
        collection.EnsureIndex(record => record.Category);
        collection.EnsureIndex(record => record.Tags);

        // Pre-populate existing records using normalized full paths as dictionary keys
        Dictionary<string, ImageRecord> unverifiedRecords = new(StringComparer.OrdinalIgnoreCase);
        foreach (ImageRecord existingRecord in collection.FindAll()) {
            if (string.IsNullOrEmpty(existingRecord.FilePath)) {
                continue;
            }

            string normalizedExistingPath = Path.GetFullPath(existingRecord.FilePath);
            unverifiedRecords[normalizedExistingPath] = existingRecord;
        }

        int processedCount = 0;
        List<ImageRecord> insertBatch = [];
        List<ImageRecord> updateBatch = [];

        EnumerationOptions options = new() {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        FileSystemEnumerable<string> enumerable = new(rootDirectoryValue, (ref FileSystemEntry entry) => entry.ToFullPath(), options) {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !ignoredFoldersHashSet.Contains(entry.FileName.ToString())
        };

        foreach (string rawFilePath in enumerable) {
            string extension = Path.GetExtension(rawFilePath).ToLowerInvariant();
            if (!supportedExtensionsHashSet.Contains(extension)) {
                continue;
            }

            // Normalize the file path to ensure identical dictionary matching
            string normalizedPath = Path.GetFullPath(rawFilePath);
            FileInfo fileInfo = new(normalizedPath);
            bool isUpdate = false;
            ImageRecord record;

            if (unverifiedRecords.Remove(normalizedPath, out ImageRecord? existingRecord)) {
                // Tolerate sub-millisecond BSON serialization truncation
                TimeSpan timeDifference = (fileInfo.LastWriteTimeUtc - existingRecord.LastModified).Duration();
                bool metadataUnchanged = timeDifference < TimeSpan.FromSeconds(1)
                                         && fileInfo.Length == existingRecord.FileSize;

                bool thumbnailSatisfied = !generateThumbnails
                                          || !string.IsNullOrEmpty(existingRecord.ThumbnailId);

                // Skip files that have not changed and already meet thumbnail requirements
                if (metadataUnchanged && thumbnailSatisfied) {
                    processedCount++;
                    progress?.Report(processedCount);
                    continue;
                }

                isUpdate = true;
                record = existingRecord;

                // Remove existing thumbnail only if regenerating it
                if (generateThumbnails && !string.IsNullOrEmpty(record.ThumbnailId)) {
                    liteDatabase.FileStorage.Delete(record.ThumbnailId);
                    record.ThumbnailId = null;
                }
            }
            else {
                record = new ImageRecord { FilePath = normalizedPath };
            }

            record.FileName = fileInfo.Name;
            record.Extension = fileInfo.Extension;
            record.FileSize = fileInfo.Length;
            record.LastModified = fileInfo.LastWriteTimeUtc;
            record.DateScanned = DateTime.UtcNow;
            record.Category = string.Empty;
            record.Tags = [];

            try {
                if (generateThumbnails) {
                    using Image image = Image.Load(normalizedPath);

                    record.Width = image.Width;
                    record.Height = image.Height;

                    ExtractExifData(image.Metadata.ExifProfile, record);

                    ResizeOptions resizeOptions = new() {
                        Size = new Size(150, 150),
                        Mode = ResizeMode.Max
                    };

                    image.Mutate(operation => operation.Resize(resizeOptions));

                    using MemoryStream thumbnailStream = new();
                    image.SaveAsWebp(thumbnailStream);
                    thumbnailStream.Position = 0;

                    string thumbnailId = $"$/thumbnails/{Guid.NewGuid()}.webp";
                    liteDatabase.FileStorage.Upload(thumbnailId, fileInfo.Name, thumbnailStream);
                    record.ThumbnailId = thumbnailId;
                }
                else {
                    ImageInfo imageInfo = Image.Identify(normalizedPath);
                    record.Width = imageInfo.Width;
                    record.Height = imageInfo.Height;
                    ExtractExifData(imageInfo.Metadata.ExifProfile, record);
                }
            }
            catch (UnknownImageFormatException) {
                // Skip unparseable formats (e.g., corrupt headers or vector files)
                continue;
            }
            catch (Exception) {
                // Skip locked files or unreadable files so the batch continues
                continue;
            }

            if (isUpdate) {
                updateBatch.Add(record);
                if (updateBatch.Count >= 1000) {
                    collection.Update(updateBatch);
                    updateBatch.Clear();
                }
            }
            else {
                insertBatch.Add(record);
                if (insertBatch.Count >= 1000) {
                    collection.InsertBulk(insertBatch);
                    insertBatch.Clear();
                }
            }

            processedCount++;
            progress?.Report(processedCount);
        }

        if (insertBatch.Count > 0) {
            collection.InsertBulk(insertBatch);
        }

        if (updateBatch.Count > 0) {
            collection.Update(updateBatch);
        }

        // Any records left in unverifiedRecords no longer exist on disk
        foreach (ImageRecord deletedRecord in unverifiedRecords.Values) {
            if (!string.IsNullOrEmpty(deletedRecord.ThumbnailId)) {
                liteDatabase.FileStorage.Delete(deletedRecord.ThumbnailId);
            }
            collection.Delete(deletedRecord.Id);
        }
    }

    /// <summary>
    /// Gets statistics about the total number of images and thumbnails stored in the LiteDB database.
    /// </summary>
    /// <returns>A tuple containing the total number of images and thumbnails.</returns>
    public (int TotalImages, int TotalThumbnails) GetStats() {
        string dbFileName = databasePathValue.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                                ? databasePathValue
                                : $"{databasePathValue}.db";

        string fullDbPath = Path.Combine(rootDirectoryValue, dbFileName);

        ConnectionString connectionString = new() {
            Filename = fullDbPath
        };

        if (!string.IsNullOrWhiteSpace(databasePassword)) {
            connectionString.Password = databasePassword;
        }

        // If the database file doesn't exist yet, return 0 to avoid exceptions
        if (!File.Exists(fullDbPath)) {
            return (0, 0);
        }

        using LiteDatabase liteDatabase = new(connectionString);
        ILiteCollection<ImageRecord> collection = liteDatabase.GetCollection<ImageRecord>("images");

        int totalImages = collection.Count();

        int totalThumbnails = collection.Count(Query.Not("ThumbnailId", BsonValue.Null));

        return (totalImages, totalThumbnails);
    }

    /// <summary>
    /// Extracts EXIF data from the provided ExifProfile and populates the corresponding fields in the ImageRecord.
    /// </summary>
    /// <param name="exifProfile">The ExifProfile from which to extract data.</param>
    /// <param name="record">The ImageRecord to populate with EXIF data.</param>
    private static void ExtractExifData(ExifProfile? exifProfile, ImageRecord record) {
        if (exifProfile == null) return;

        if (exifProfile.TryGetValue(ExifTag.Make, out IExifValue<string>? makeValue)) {
            record.CameraMaker = makeValue.GetValue()?.ToString();
        }

        if (exifProfile.TryGetValue(ExifTag.Model, out IExifValue<string>? modelValue)) {
            record.CameraModel = modelValue.GetValue()?.ToString();
        }
    }
}