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
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg", ".ico"];
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
            Filename = fullDbPath
        };

        if (!string.IsNullOrWhiteSpace(databasePassword)) {
            connectionString.Password = databasePassword;
        }

        using LiteDatabase liteDatabase = new(connectionString);
        ILiteCollection<ImageRecord> collection = liteDatabase.GetCollection<ImageRecord>("images");
        collection.EnsureIndex(x => x.FilePath, true);

        Dictionary<string, ImageRecord> unverifiedRecords = new(StringComparer.OrdinalIgnoreCase);
        foreach (ImageRecord existingRecord in collection.FindAll()) {
            if (existingRecord.FilePath != null) {
                unverifiedRecords[existingRecord.FilePath] = existingRecord;
            }
        }
        HashSet<string> existingPaths = new(
                collection.Query().Select(x => x.FilePath).ToEnumerable()!,
                StringComparer.OrdinalIgnoreCase
            );

        int processedCount = 0;
        List<ImageRecord> insertBatch = [];
        List<ImageRecord> updateBatch = [];

        EnumerationOptions options = new() {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        FileSystemEnumerable<string> enumerable = new(rootDirectoryValue, (ref entry) => entry.ToFullPath(), options) {
            ShouldIncludePredicate = (ref entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref entry) => !ignoredFoldersHashSet.Contains(entry.FileName.ToString())
        };

        foreach (string filePath in enumerable) {
            if (unverifiedRecords.Remove(filePath)) {
                continue;
            }

            if (existingPaths.Contains(filePath)) {
                continue;
            }
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (!supportedExtensionsHashSet.Contains(extension)) {
                continue;
            }

            FileInfo fileInfo = new(filePath);
            bool isUpdate = false;
            ImageRecord record;

            if (unverifiedRecords.Remove(filePath, out ImageRecord? existingRecord)) {
                if ((fileInfo.LastWriteTimeUtc <= existingRecord.LastModified) && (fileInfo.Length == existingRecord.FileSize)) {
                    continue;
                }

                isUpdate = true;
                record = existingRecord;
                if (generateThumbnails && !string.IsNullOrEmpty(record.ThumbnailId)) {
                    liteDatabase.FileStorage.Delete(record.ThumbnailId);
                    record.ThumbnailId = null;
                }
            }
            else {
                record = new ImageRecord() { FilePath = fileInfo.FullName };
            }

            record.FileName = fileInfo.Name;
            record.Extension = fileInfo.Extension;
            record.FileSize = fileInfo.Length;
            record.LastModified = fileInfo.LastWriteTimeUtc;
            record.DateScanned = DateTime.UtcNow;

            try {
                if (generateThumbnails) {
                    using Image image = Image.Load(filePath);

                    record.Width = image.Width;
                    record.Height = image.Height;

                    ExtractExifData(image.Metadata.ExifProfile, record);

                    ResizeOptions resizeOptions = new() {
                        Size = new Size(150, 150),
                        Mode = ResizeMode.Max
                    };

                    image.Mutate(x => x.Resize(resizeOptions));

                    using MemoryStream thumbnailStream = new();
                    image.SaveAsWebp(thumbnailStream);
                    thumbnailStream.Position = 0;

                    string thumbnailId = $"$/thumbnails/{Guid.NewGuid()}.webp";
                    liteDatabase.FileStorage.Upload(thumbnailId, fileInfo.Name, thumbnailStream);
                    record.ThumbnailId = thumbnailId;
                }
                else {
                    ImageInfo imageInfo = Image.Identify(filePath);
                    record.Width = imageInfo.Width;
                    record.Height = imageInfo.Height;
                    ExtractExifData(imageInfo.Metadata.ExifProfile, record);
                }
            }
            catch (UnknownImageFormatException) {
                // File extension matched, but the internal header is not a valid image
            }
            catch (Exception) {
                // Catch-all for locked files or corrupted headers so the scan continues
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