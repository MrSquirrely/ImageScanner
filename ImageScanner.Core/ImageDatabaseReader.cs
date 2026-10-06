using LiteDB;

namespace ImageScanner.Core;

public class ImageDatabaseReader(string databasePath, string? password = null) : IDisposable {
    private readonly LiteDatabase database = new(
            new ConnectionString {
                Filename = databasePath.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                               ? databasePath
                               : $"{databasePath}.db",
                Connection = ConnectionType.Shared,
                Password = string.IsNullOrWhiteSpace(password) ? null : password
            }
        );

    public List<ImageRecord> GetAllImages() {
        ILiteCollection<ImageRecord> collection = database.GetCollection<ImageRecord>("images");
        return collection.FindAll().OrderByDescending(record => record.DateScanned).ToList();
    }

    public List<ImageRecord> SearchImages(string query) {
        ILiteCollection<ImageRecord> collection = database.GetCollection<ImageRecord>("images");
        return [
            .. collection.Find(record =>
                                   record.FileName != null &&
                                   record.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
        ];
    }

    public MemoryStream? LoadThumbnailStream(string thumbnailId) {
        if (string.IsNullOrEmpty(thumbnailId)) {
            return null;
        }

        LiteFileInfo<string> file = database.FileStorage.FindById(thumbnailId);
        if (file == null) {
            return null;
        }

        MemoryStream stream = new();
        file.CopyTo(stream);
        stream.Position = 0;
        return stream;
    }

    public void Dispose() {
        database.Dispose();
        GC.SuppressFinalize(this);
    }
}