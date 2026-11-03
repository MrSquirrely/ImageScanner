using System.Text.RegularExpressions;

using LiteDB;

namespace ImageScanner.Core.Library;

public enum LibraryChangeKind {
    Images,
    Tags,
    Categories,
    Comics,
    Sources,
    All
}

/// <summary>
/// The organizer catalog: a LiteDB database plus an in-memory index of every image so that
/// searching large libraries stays instant. All mutations go through this class, which writes
/// to disk and raises <see cref="Changed"/>. Thread-safe.
/// </summary>
public sealed partial class ImageLibrary : IDisposable {
    private const string ImagesCollection = "library_images";
    private const string ComicsCollection = "comics";
    private const string TagsCollection = "tags";
    private const string CategoriesCollection = "categories";
    private const string SourcesCollection = "sources";

    private readonly LiteDatabase database;
    private readonly ILiteCollection<LibraryImage> images;
    private readonly ILiteCollection<Comic> comics;
    private readonly ILiteCollection<TagDefinition> tagDefinitions;
    private readonly ILiteCollection<CategoryDefinition> categoryDefinitions;
    private readonly ILiteCollection<ScanSource> sources;

    private readonly Lock sync = new();
    private readonly Dictionary<int, LibraryImage> imagesById = [];
    private readonly Dictionary<string, LibraryImage> imagesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Comic> comicsById = [];

    public event EventHandler<LibraryChangeKind>? Changed;

    public string DatabasePath { get; }

    public ImageLibrary(string databasePath) {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        database = new LiteDatabase(new ConnectionString {
            Filename = DatabasePath,
            Connection = ConnectionType.Direct
        });

        images = database.GetCollection<LibraryImage>(ImagesCollection);
        images.EnsureIndex(image => image.FilePath, true);
        images.EnsureIndex(image => image.ComicId);
        comics = database.GetCollection<Comic>(ComicsCollection);
        tagDefinitions = database.GetCollection<TagDefinition>(TagsCollection);
        categoryDefinitions = database.GetCollection<CategoryDefinition>(CategoriesCollection);
        sources = database.GetCollection<ScanSource>(SourcesCollection);

        foreach (LibraryImage image in images.FindAll()) {
            imagesById[image.Id] = image;
            imagesByPath[image.FilePath] = image;
        }

        foreach (Comic comic in comics.FindAll()) {
            comicsById[comic.Id] = comic;
        }
    }

    public int Count {
        get {
            lock (sync) {
                return imagesById.Count;
            }
        }
    }

    /// <summary>
    /// Normalizes a tag to a trimmed, lower-case, single-spaced form so "Blue  Sky" and "blue sky" match.
    /// </summary>
    public static string NormalizeTag(string tag) => WhitespaceRegex().Replace(tag.Trim(), " ").ToLowerInvariant();

    private void Raise(LibraryChangeKind kind) => Changed?.Invoke(this, kind);

    #region Images

    public List<LibraryImage> Search(SearchQuery query) {
        lock (sync) {
            return [.. imagesById.Values.Where(image => query.Matches(image, this))];
        }
    }

    public List<LibraryImage> GetAllImages() {
        lock (sync) {
            return [.. imagesById.Values];
        }
    }

    public LibraryImage? GetImage(int id) {
        lock (sync) {
            return imagesById.GetValueOrDefault(id);
        }
    }

    public LibraryImage? GetImageByPath(string path) {
        lock (sync) {
            return imagesByPath.GetValueOrDefault(Path.GetFullPath(path));
        }
    }

    /// <summary>
    /// Inserts new images (Id == 0) and updates existing ones in a single transaction.
    /// </summary>
    public void Save(IReadOnlyCollection<LibraryImage> batch, bool raiseChanged = true) {
        if (batch.Count == 0) {
            return;
        }

        lock (sync) {
            database.BeginTrans();
            try {
                foreach (LibraryImage image in batch) {
                    image.Tags = NormalizeTags(image.Tags);
                    if (image.Id == 0) {
                        images.Insert(image);
                    }
                    else {
                        images.Update(image);
                    }

                    imagesById[image.Id] = image;
                    imagesByPath[image.FilePath] = image;
                }
                database.Commit();
            }
            catch {
                database.Rollback();
                throw;
            }
        }

        if (raiseChanged) {
            Raise(LibraryChangeKind.Images);
        }
    }

    public void Save(LibraryImage image) => Save([image]);

    public void DeleteImages(IEnumerable<int> ids) {
        HashSet<int> touchedComics = [];
        lock (sync) {
            foreach (int id in ids) {
                if (!imagesById.Remove(id, out LibraryImage? image)) {
                    continue;
                }

                imagesByPath.Remove(image.FilePath);
                if (!string.IsNullOrEmpty(image.ThumbnailId)) {
                    database.FileStorage.Delete(image.ThumbnailId);
                }

                if (image.ComicId is int comicId) {
                    touchedComics.Add(comicId);
                }

                images.Delete(id);
            }

            foreach (int comicId in touchedComics) {
                RenumberComicUnlocked(comicId);
            }
        }

        Raise(LibraryChangeKind.All);
    }

    public void SetFavorite(IEnumerable<int> ids, bool isFavorite) => Mutate(ids, image => image.IsFavorite = isFavorite);

    public void SetRating(IEnumerable<int> ids, int rating) => Mutate(ids, image => image.Rating = Math.Clamp(rating, 0, 5));

    private void Mutate(IEnumerable<int> ids, Action<LibraryImage> change, LibraryChangeKind kind = LibraryChangeKind.Images) {
        List<LibraryImage> changed = [];
        lock (sync) {
            foreach (int id in ids) {
                if (imagesById.TryGetValue(id, out LibraryImage? image)) {
                    change(image);
                    changed.Add(image);
                }
            }
        }

        Save(changed, raiseChanged: false);
        Raise(kind);
    }

    #endregion

    #region Tags

    public List<TagCount> GetTagCounts() {
        lock (sync) {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            foreach (TagDefinition definition in tagDefinitions.FindAll()) {
                counts[definition.Name] = 0;
            }

            foreach (LibraryImage image in imagesById.Values) {
                foreach (string tag in image.Tags) {
                    counts[tag] = counts.GetValueOrDefault(tag) + 1;
                }
            }

            Dictionary<string, string?> colors = tagDefinitions.FindAll().ToDictionary(tag => tag.Name, tag => tag.Color, StringComparer.OrdinalIgnoreCase);
            return [.. counts.Select(pair => new TagCount(pair.Key, pair.Value, colors.GetValueOrDefault(pair.Key)))
                             .OrderByDescending(tag => tag.Count)
                             .ThenBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public List<string> GetAllTagNames() => [.. GetTagCounts().Select(tag => tag.Name)];

    public void AddTags(IEnumerable<int> ids, IEnumerable<string> tags) {
        List<string> normalized = NormalizeTags(tags);
        Mutate(ids, image => image.Tags = NormalizeTags(image.Tags.Concat(normalized)), LibraryChangeKind.Tags);
    }

    public void RemoveTags(IEnumerable<int> ids, IEnumerable<string> tags) {
        HashSet<string> remove = new(NormalizeTags(tags), StringComparer.OrdinalIgnoreCase);
        Mutate(ids, image => image.Tags.RemoveAll(remove.Contains), LibraryChangeKind.Tags);
    }

    /// <summary>
    /// Renames a tag everywhere. If the new name already exists the two tags are merged.
    /// </summary>
    public void RenameTag(string oldName, string newName) {
        string from = NormalizeTag(oldName);
        string to = NormalizeTag(newName);
        if (to.Length == 0 || from == to) {
            return;
        }

        List<int> affected;
        lock (sync) {
            affected = [.. imagesById.Values.Where(image => image.Tags.Contains(from)).Select(image => image.Id)];
            TagDefinition? definition = tagDefinitions.FindById(from);
            if (definition != null) {
                tagDefinitions.Delete(from);
                definition.Name = to;
                tagDefinitions.Upsert(definition);
            }
        }

        Mutate(affected, image => image.Tags = NormalizeTags(image.Tags.Select(tag => tag == from ? to : tag)), LibraryChangeKind.Tags);
    }

    public void DeleteTag(string name) {
        string tag = NormalizeTag(name);
        List<int> affected;
        lock (sync) {
            affected = [.. imagesById.Values.Where(image => image.Tags.Contains(tag)).Select(image => image.Id)];
            tagDefinitions.Delete(tag);
        }

        Mutate(affected, image => image.Tags.Remove(tag), LibraryChangeKind.Tags);
    }

    public void SetTagColor(string name, string? color) {
        lock (sync) {
            string tag = NormalizeTag(name);
            TagDefinition definition = tagDefinitions.FindById(tag) ?? new TagDefinition { Name = tag };
            definition.Color = color;
            tagDefinitions.Upsert(definition);
        }

        Raise(LibraryChangeKind.Tags);
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        [.. tags.Select(NormalizeTag).Where(tag => tag.Length > 0).Distinct()];

    #endregion

    #region Categories

    public List<CategoryCount> GetCategoryCounts() {
        lock (sync) {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            foreach (CategoryDefinition definition in categoryDefinitions.FindAll()) {
                counts[definition.Name] = 0;
            }

            foreach (LibraryImage image in imagesById.Values) {
                if (!string.IsNullOrWhiteSpace(image.Category)) {
                    counts[image.Category] = counts.GetValueOrDefault(image.Category) + 1;
                }
            }

            return [.. counts.Select(pair => new CategoryCount(pair.Key, pair.Value))
                             .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public void AddCategory(string name) {
        string trimmed = name.Trim();
        if (trimmed.Length == 0) {
            return;
        }

        lock (sync) {
            categoryDefinitions.Upsert(new CategoryDefinition { Name = trimmed });
        }

        Raise(LibraryChangeKind.Categories);
    }

    public void SetCategory(IEnumerable<int> ids, string? category) {
        string? value = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (value != null) {
            lock (sync) {
                if (categoryDefinitions.FindById(value) == null) {
                    categoryDefinitions.Insert(new CategoryDefinition { Name = value });
                }
            }
        }

        Mutate(ids, image => image.Category = value, LibraryChangeKind.Categories);
    }

    public void RenameCategory(string oldName, string newName) {
        string to = newName.Trim();
        if (to.Length == 0) {
            return;
        }

        List<int> affected;
        lock (sync) {
            categoryDefinitions.Delete(oldName);
            categoryDefinitions.Upsert(new CategoryDefinition { Name = to });
            affected = [.. imagesById.Values.Where(image => string.Equals(image.Category, oldName, StringComparison.OrdinalIgnoreCase)).Select(image => image.Id)];
        }

        Mutate(affected, image => image.Category = to, LibraryChangeKind.Categories);
    }

    public void DeleteCategory(string name) {
        List<int> affected;
        lock (sync) {
            categoryDefinitions.Delete(name);
            affected = [.. imagesById.Values.Where(image => string.Equals(image.Category, name, StringComparison.OrdinalIgnoreCase)).Select(image => image.Id)];
        }

        Mutate(affected, image => image.Category = null, LibraryChangeKind.Categories);
    }

    #endregion

    #region AI suggestions

    public void SetSuggestions(int id, IEnumerable<string> tags, string? category, string? suggestedBy) {
        lock (sync) {
            if (!imagesById.TryGetValue(id, out LibraryImage? image)) {
                return;
            }

            // Only keep suggestions the image doesn't already have
            image.SuggestedTags = [.. NormalizeTags(tags).Except(image.Tags)];
            image.SuggestedCategory = string.IsNullOrWhiteSpace(category) || string.Equals(category, image.Category, StringComparison.OrdinalIgnoreCase)
                                          ? null
                                          : category.Trim();
            image.SuggestedBy = suggestedBy;
            images.Update(image);
        }

        Raise(LibraryChangeKind.Images);
    }

    /// <summary>
    /// Moves pending suggestions onto the images. When <paramref name="onlyTags"/> is given only those tags are accepted.
    /// </summary>
    public void AcceptSuggestions(IEnumerable<int> ids, IEnumerable<string>? onlyTags = null, bool acceptCategory = true) {
        HashSet<string>? allowed = onlyTags == null ? null : new HashSet<string>(NormalizeTags(onlyTags), StringComparer.OrdinalIgnoreCase);
        List<string> newCategories = [];
        Mutate(ids, image => {
            IEnumerable<string> accepted = allowed == null ? image.SuggestedTags : image.SuggestedTags.Where(allowed.Contains);
            image.Tags = NormalizeTags(image.Tags.Concat(accepted));
            if (acceptCategory && !string.IsNullOrEmpty(image.SuggestedCategory)) {
                image.Category = image.SuggestedCategory;
                newCategories.Add(image.SuggestedCategory);
            }

            image.SuggestedTags = [];
            image.SuggestedCategory = null;
        }, LibraryChangeKind.All);

        lock (sync) {
            foreach (string category in newCategories.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (categoryDefinitions.FindById(category) == null) {
                    categoryDefinitions.Insert(new CategoryDefinition { Name = category });
                }
            }
        }
    }

    public void RejectSuggestions(IEnumerable<int> ids) => Mutate(ids, image => {
        image.SuggestedTags = [];
        image.SuggestedCategory = null;
    });

    #endregion

    #region Comics

    public List<Comic> GetComics() {
        lock (sync) {
            return [.. comicsById.Values.OrderBy(comic => comic.Title, NaturalStringComparer.Instance)];
        }
    }

    public Comic? GetComic(int id) {
        lock (sync) {
            return comicsById.GetValueOrDefault(id);
        }
    }

    public List<LibraryImage> GetComicPages(int comicId) {
        lock (sync) {
            return [.. imagesById.Values.Where(image => image.ComicId == comicId).OrderBy(image => image.PageNumber)];
        }
    }

    /// <summary>
    /// Creates a comic from images in the given page order. Images already in another comic are moved.
    /// </summary>
    public Comic CreateComic(string title, IReadOnlyList<int> orderedImageIds, bool isAutoDetected = false, string? folderPath = null) {
        Comic comic = new() {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled comic" : title.Trim(),
            FolderPath = folderPath,
            IsAutoDetected = isAutoDetected,
            DateAdded = DateTime.UtcNow
        };

        lock (sync) {
            comics.Insert(comic);
            comicsById[comic.Id] = comic;
            SetPagesUnlocked(comic, orderedImageIds);
        }

        Raise(LibraryChangeKind.Comics);
        return comic;
    }

    /// <summary>
    /// Replaces a comic's page list and order.
    /// </summary>
    public void SetComicPages(int comicId, IReadOnlyList<int> orderedImageIds) {
        lock (sync) {
            if (!comicsById.TryGetValue(comicId, out Comic? comic)) {
                return;
            }

            foreach (LibraryImage page in imagesById.Values.Where(image => image.ComicId == comicId && !orderedImageIds.Contains(image.Id)).ToList()) {
                page.ComicId = null;
                page.PageNumber = 0;
                images.Update(page);
            }

            SetPagesUnlocked(comic, orderedImageIds);
        }

        Raise(LibraryChangeKind.Comics);
    }

    public void AddToComic(int comicId, IEnumerable<int> imageIds) {
        List<int> pages = [.. GetComicPages(comicId).Select(page => page.Id)];
        pages.AddRange(imageIds.Where(id => !pages.Contains(id)));
        SetComicPages(comicId, pages);
    }

    public void RemoveFromComic(IEnumerable<int> imageIds) {
        HashSet<int> touched = [];
        lock (sync) {
            foreach (int id in imageIds) {
                if (imagesById.TryGetValue(id, out LibraryImage? image) && image.ComicId is int comicId) {
                    touched.Add(comicId);
                    image.ComicId = null;
                    image.PageNumber = 0;
                    images.Update(image);
                }
            }

            foreach (int comicId in touched) {
                RenumberComicUnlocked(comicId);
            }
        }

        Raise(LibraryChangeKind.Comics);
    }

    public void UpdateComic(Comic comic) {
        lock (sync) {
            comic.Tags = NormalizeTags(comic.Tags);
            comics.Update(comic);
            comicsById[comic.Id] = comic;
        }

        Raise(LibraryChangeKind.Comics);
    }

    /// <summary>
    /// Deletes the comic grouping; the page images stay in the library.
    /// </summary>
    public void DeleteComic(int comicId) {
        lock (sync) {
            foreach (LibraryImage page in imagesById.Values.Where(image => image.ComicId == comicId)) {
                page.ComicId = null;
                page.PageNumber = 0;
                images.Update(page);
            }

            if (comicsById.Remove(comicId, out Comic? comic) && comic.IsAutoDetected && comic.FolderPath != null) {
                ScanSource? source = sources.FindAll().FirstOrDefault(existing => comic.FolderPath.StartsWith(existing.RootPath, StringComparison.OrdinalIgnoreCase));
                if (source != null && !source.DismissedComicFolders.Contains(comic.FolderPath, StringComparer.OrdinalIgnoreCase)) {
                    source.DismissedComicFolders.Add(comic.FolderPath);
                    sources.Update(source);
                }
            }

            comics.Delete(comicId);
        }

        Raise(LibraryChangeKind.Comics);
    }

    private void SetPagesUnlocked(Comic comic, IReadOnlyList<int> orderedImageIds) {
        HashSet<int> previousComics = [];
        int pageNumber = 1;
        foreach (int id in orderedImageIds) {
            if (!imagesById.TryGetValue(id, out LibraryImage? image)) {
                continue;
            }

            if (image.ComicId is int previous && previous != comic.Id) {
                previousComics.Add(previous);
            }

            image.ComicId = comic.Id;
            image.PageNumber = pageNumber++;
            images.Update(image);
        }

        comic.PageCount = pageNumber - 1;
        comic.CoverImageId = orderedImageIds.FirstOrDefault(imagesById.ContainsKey);
        comics.Update(comic);

        foreach (int previous in previousComics) {
            RenumberComicUnlocked(previous);
        }
    }

    private void RenumberComicUnlocked(int comicId) {
        if (!comicsById.TryGetValue(comicId, out Comic? comic)) {
            return;
        }

        List<LibraryImage> pages = [.. imagesById.Values.Where(image => image.ComicId == comicId).OrderBy(image => image.PageNumber)];
        if (pages.Count == 0) {
            comics.Delete(comicId);
            comicsById.Remove(comicId);
            return;
        }

        for (int index = 0; index < pages.Count; index++) {
            pages[index].PageNumber = index + 1;
            images.Update(pages[index]);
        }

        comic.PageCount = pages.Count;
        comic.CoverImageId = pages[0].Id;
        comics.Update(comic);
    }

    #endregion

    #region Thumbnails

    public string SaveThumbnail(byte[] jpegBytes, string fileName) {
        string thumbnailId = $"$/thumbs/{Guid.NewGuid():N}.jpg";
        using MemoryStream stream = new(jpegBytes);
        lock (sync) {
            database.FileStorage.Upload(thumbnailId, fileName, stream);
        }

        return thumbnailId;
    }

    public byte[]? LoadThumbnail(string? thumbnailId) {
        if (string.IsNullOrEmpty(thumbnailId)) {
            return null;
        }

        lock (sync) {
            LiteFileInfo<string>? file = database.FileStorage.FindById(thumbnailId);
            if (file == null) {
                return null;
            }

            using MemoryStream stream = new();
            file.CopyTo(stream);
            return stream.ToArray();
        }
    }

    public void DeleteThumbnail(string? thumbnailId) {
        if (string.IsNullOrEmpty(thumbnailId)) {
            return;
        }

        lock (sync) {
            database.FileStorage.Delete(thumbnailId);
        }
    }

    #endregion

    #region Sources

    public List<ScanSource> GetSources() {
        lock (sync) {
            return [.. sources.FindAll().OrderBy(source => source.RootPath)];
        }
    }

    public void SaveSource(ScanSource source) {
        lock (sync) {
            sources.Upsert(source);
        }

        Raise(LibraryChangeKind.Sources);
    }

    /// <summary>
    /// Forgets a scanned folder and optionally removes its images from the library (files on disk are untouched).
    /// </summary>
    public void RemoveSource(string rootPath, bool removeImages) {
        lock (sync) {
            sources.Delete(rootPath);
        }

        if (removeImages) {
            string prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            DeleteImages(GetAllImages().Where(image => image.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(image => image.Id).ToList());
        }

        Raise(LibraryChangeKind.Sources);
    }

    #endregion

    public void Dispose() => database.Dispose();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
