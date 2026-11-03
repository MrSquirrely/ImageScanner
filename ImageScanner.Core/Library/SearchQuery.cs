using System.Text;

namespace ImageScanner.Core.Library;

/// <summary>
/// Parsed search expression. Supported syntax (terms are AND-ed, prefix with '-' to negate):
/// <code>
///   sunset beach            free text in file name, folder, tags, category, description
///   tag:cat  -tag:dog       has / lacks a tag (quotes allowed: tag:"blue sky")
///   artist:"stanley lau"    by an artist (also by:)
///   cat:Wallpapers          category (also category:)
///   comic:"My Comic"        belongs to a comic whose title contains the text
///   ext:png                 file extension
///   folder:Downloads        folder path contains text
///   rating>=3 rating:5      rating comparisons
///   is:fav is:untagged is:comic is:pending is:uncategorized is:noartist
/// </code>
/// </summary>
public class SearchQuery {
    private readonly List<Func<LibraryImage, ImageLibrary, bool>> predicates = [];

    public static SearchQuery Empty { get; } = new();

    public bool IsEmpty => predicates.Count == 0;

    public static SearchQuery Parse(string? text) {
        SearchQuery query = new();
        if (string.IsNullOrWhiteSpace(text)) {
            return query;
        }

        foreach (string rawToken in Tokenize(text)) {
            bool negate = rawToken.Length > 1 && rawToken[0] == '-';
            string token = negate ? rawToken[1..] : rawToken;
            Func<LibraryImage, ImageLibrary, bool>? predicate = BuildPredicate(token);
            if (predicate == null) {
                continue;
            }

            query.predicates.Add(negate ? (image, library) => !predicate(image, library) : predicate);
        }

        return query;
    }

    public bool Matches(LibraryImage image, ImageLibrary library) {
        foreach (Func<LibraryImage, ImageLibrary, bool> predicate in predicates) {
            if (!predicate(image, library)) {
                return false;
            }
        }

        return true;
    }

    private static Func<LibraryImage, ImageLibrary, bool>? BuildPredicate(string token) {
        foreach (string op in new[] { ">=", "<=", ">", "<" }) {
            int opIndex = token.IndexOf(op, StringComparison.Ordinal);
            if (opIndex > 0 && token[..opIndex].Equals("rating", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(token[(opIndex + op.Length)..], out int ratingValue)) {
                return op switch {
                    ">=" => (image, _) => image.Rating >= ratingValue,
                    "<=" => (image, _) => image.Rating <= ratingValue,
                    ">" => (image, _) => image.Rating > ratingValue,
                    _ => (image, _) => image.Rating < ratingValue
                };
            }
        }

        int colonIndex = token.IndexOf(':');
        if (colonIndex <= 0) {
            string freeText = token;
            return (image, _) => MatchesFreeText(image, freeText);
        }

        string key = token[..colonIndex].ToLowerInvariant();
        string value = token[(colonIndex + 1)..];
        if (value.Length == 0) {
            return null;
        }

        return key switch {
            "tag" or "tags" => (image, _) => image.Tags.Contains(value, StringComparer.OrdinalIgnoreCase),
            "artist" or "by" => (image, _) => image.Artists.Contains(ImageLibrary.NormalizeArtist(value), StringComparer.OrdinalIgnoreCase),
            "cat" or "category" => (image, _) => string.Equals(image.Category, value, StringComparison.OrdinalIgnoreCase),
            "ext" => (image, _) => image.Extension.TrimStart('.').Equals(value.TrimStart('.'), StringComparison.OrdinalIgnoreCase),
            "folder" or "path" => (image, _) => image.Folder.Contains(value, StringComparison.OrdinalIgnoreCase),
            "comic" => (image, library) => image.ComicId is int comicId
                                            && library.GetComic(comicId)?.Title.Contains(value, StringComparison.OrdinalIgnoreCase) == true,
            "comicid" => int.TryParse(value, out int id) ? (image, _) => image.ComicId == id : null,
            "rating" => int.TryParse(value, out int rating) ? (image, _) => image.Rating == rating : null,
            "is" => value.ToLowerInvariant() switch {
                "fav" or "favorite" or "favourite" => (image, _) => image.IsFavorite,
                "untagged" => (image, _) => image.Tags.Count == 0,
                "noartist" => (image, _) => image.Artists.Count == 0,
                "tagged" => (image, _) => image.Tags.Count > 0,
                "comic" => (image, _) => image.ComicId != null,
                "pending" => (image, _) => image.HasPendingSuggestions,
                "uncategorized" or "uncategorised" => (image, _) => string.IsNullOrEmpty(image.Category),
                _ => null
            },
            _ => (image, _) => MatchesFreeText(image, token)
        };
    }

    private static bool MatchesFreeText(LibraryImage image, string text) =>
        image.FileName.Contains(text, StringComparison.OrdinalIgnoreCase)
        || image.Folder.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (image.Category?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || (image.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || image.Tags.Any(tag => tag.Contains(text, StringComparison.OrdinalIgnoreCase))
        || image.Artists.Any(artist => artist.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Splits on whitespace while keeping quoted sections (e.g. tag:"blue sky") together.
    /// </summary>
    private static IEnumerable<string> Tokenize(string text) {
        StringBuilder current = new();
        bool inQuotes = false;
        foreach (char character in text) {
            if (character == '"') {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes) {
                if (current.Length > 0) {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0) {
            yield return current.ToString();
        }
    }
}
