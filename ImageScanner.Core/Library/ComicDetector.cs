using System.Text.RegularExpressions;

namespace ImageScanner.Core.Library;

public record ComicCandidate(string Title, string FolderPath, List<LibraryImage> Pages);

/// <summary>
/// Finds folders that look like comic chapters/issues: enough images whose file names are
/// numbered pages, or a folder name that reads like a comic (Chapter 3, Vol. 2, #12, ...).
/// </summary>
public static partial class ComicDetector {
    public static List<ComicCandidate> FindCandidates(IEnumerable<LibraryImage> images, int minimumPages = 3) {
        List<ComicCandidate> candidates = [];

        foreach (IGrouping<string, LibraryImage> folder in images.Where(image => image.ComicId == null)
                                                                  .GroupBy(image => image.Folder, StringComparer.OrdinalIgnoreCase)) {
            List<LibraryImage> pages = [.. folder.OrderBy(image => image.FileName, NaturalStringComparer.Instance)];
            if (pages.Count < minimumPages) {
                continue;
            }

            string folderName = Path.GetFileName(folder.Key.TrimEnd(Path.DirectorySeparatorChar));
            bool comicLikeFolder = ComicFolderRegex().IsMatch(folderName);
            if (!comicLikeFolder && !LooksLikeNumberedPages(pages)) {
                continue;
            }

            candidates.Add(new ComicCandidate(BuildTitle(folder.Key), folder.Key, pages));
        }

        return candidates;
    }

    /// <summary>
    /// True when most file names carry a page number and those numbers are close to a consecutive run.
    /// </summary>
    private static bool LooksLikeNumberedPages(List<LibraryImage> pages) {
        List<int> numbers = [];
        foreach (LibraryImage page in pages) {
            Match match = LastNumberRegex().Match(Path.GetFileNameWithoutExtension(page.FileName));
            if (match.Success && int.TryParse(match.Value, out int number)) {
                numbers.Add(number);
            }
        }

        if (numbers.Count < pages.Count * 0.8) {
            return false;
        }

        numbers.Sort();
        int distinct = numbers.Distinct().Count();
        int span = numbers[^1] - numbers[0] + 1;

        // Camera dumps (IMG_4821, IMG_4907 ...) have big gaps; comic pages are dense
        return distinct >= numbers.Count * 0.9 && span <= distinct * 1.5 + 2;
    }

    /// <summary>
    /// "Series/Chapter 03" becomes "Series - Chapter 03" so chapters from the same series stay identifiable.
    /// </summary>
    private static string BuildTitle(string folderPath) {
        string trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        string? parent = Path.GetFileName(Path.GetDirectoryName(trimmed));
        bool genericName = GenericChapterRegex().IsMatch(name);
        return genericName && !string.IsNullOrEmpty(parent) ? $"{parent} - {name}" : name;
    }

    [GeneratedRegex(@"(chapter|\bch\.?\s*\d|\bvol\.?\s*\d|volume|issue|#\d|episode|\bep\.?\s*\d|comic|manga|manhwa|doujin|webtoon)", RegexOptions.IgnoreCase)]
    private static partial Regex ComicFolderRegex();

    [GeneratedRegex(@"^(chapter|ch\.?|vol\.?|volume|issue|episode|ep\.?|part|#)?\s*\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericChapterRegex();

    [GeneratedRegex(@"\d+(?=\D*$)")]
    private static partial Regex LastNumberRegex();
}
