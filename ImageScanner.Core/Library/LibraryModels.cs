using LiteDB;

namespace ImageScanner.Core.Library;

/// <summary>
/// An image tracked by the organizer library. Unlike <see cref="ImageRecord"/>, user data
/// (tags, category, comic membership, rating) survives rescans.
/// </summary>
public class LibraryImage {
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime DateAdded { get; set; }

    public string? Category { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Description { get; set; }
    public int Rating { get; set; }
    public bool IsFavorite { get; set; }

    // AI suggestions waiting for review
    public List<string> SuggestedTags { get; set; } = [];
    public string? SuggestedCategory { get; set; }
    public string? SuggestedBy { get; set; }

    // Comic grouping
    public int? ComicId { get; set; }
    public int PageNumber { get; set; }

    public string? CameraMaker { get; set; }
    public string? CameraModel { get; set; }
    public string? ThumbnailId { get; set; }

    [BsonIgnore]
    public bool HasPendingSuggestions => SuggestedTags.Count > 0 || !string.IsNullOrEmpty(SuggestedCategory);

    [BsonIgnore]
    public string Dimensions => Width > 0 ? $"{Width} × {Height}" : string.Empty;
}

/// <summary>
/// A group of images treated as an ordered set of comic pages.
/// </summary>
public class Comic {
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Series { get; set; }
    public string? FolderPath { get; set; }
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = [];
    public int CoverImageId { get; set; }
    public int PageCount { get; set; }
    public bool IsAutoDetected { get; set; }
    public DateTime DateAdded { get; set; }
}

public class TagDefinition {
    [BsonId]
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? Description { get; set; }
}

public class CategoryDefinition {
    [BsonId]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// A folder or drive the user has imported, so it can be rescanned later.
/// </summary>
public class ScanSource {
    [BsonId]
    public string RootPath { get; set; } = string.Empty;
    public DateTime LastScanned { get; set; }
    public int ImageCount { get; set; }
    public List<string> IgnoredFolders { get; set; } = [];

    // Auto-detected comics the user ungrouped; not recreated on rescan
    public List<string> DismissedComicFolders { get; set; } = [];
}

public record TagCount(string Name, int Count, string? Color);

public record CategoryCount(string Name, int Count);
