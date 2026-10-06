namespace ImageScanner.Core;

/// <summary>
/// Represents a record of an image file with its metadata for storage in LiteDB.
/// </summary>
public class ImageRecord {
	public int Id { get; set; } // LiteDB uses ID automatically as the primary key
	public string? FilePath { get; set; } // Full path to the image file
    public string? FileName { get; set; } // Name of the image file
    public string? Extension { get; set; } // File extension of the image
    public long FileSize { get; set; } // Size of the image file in bytes
    public DateTime DateScanned { get; set; } // Date and time when the image was scanned
    public DateTime LastModified { get; set; } // Last modified date of the image file
    public int Width { get; set; } // Width of the image in pixels
    public int Height { get; set; } // Height of the image in pixels
    public string? CameraMaker { get; set; } // Maker of the camera used to take the image
    public string? CameraModel { get; set; } // Model of the camera used to take the image
    public string? ThumbnailId { get; set; } // ID of the thumbnail image stored in LiteDB's FileStorage


}