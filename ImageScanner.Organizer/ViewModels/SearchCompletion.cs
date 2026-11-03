namespace ImageScanner.Organizer.ViewModels;

/// <summary>
/// Completes the word being typed in the search box: "tag:bl" → "tag:\"blue sky\"", "stan" → "artist:\"stanley lau\"",
/// "is:" → "is:fav" … Each suggestion is the whole search text with that word completed, because picking one replaces the text.
/// </summary>
public static class SearchCompletion {
    private static readonly string[] IsKeywords = ["fav", "untagged", "tagged", "comic", "pending", "uncategorized", "noartist"];
    private static readonly string[] Keywords = ["tag:", "artist:", "cat:", "is:", "rating>="];

    public static List<string> Suggest(string text, IEnumerable<string> tags, IEnumerable<string> artists, IEnumerable<string> categories, int max = 12) {
        // The word being typed starts after the last space that isn't inside quotes
        int start = 0;
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '"') inQuotes = !inQuotes;
            else if (char.IsWhiteSpace(text[i]) && !inQuotes) start = i + 1;
        }

        string before = text[..start];
        string word = text[start..];
        string negation = word.StartsWith('-') ? "-" : string.Empty;
        word = word[negation.Length..];
        if (word.Length == 0) {
            return [];
        }

        List<string> completions = [];
        int colon = word.IndexOf(':');
        if (colon > 0) {
            string key = word[..colon].ToLowerInvariant();
            string partial = word[(colon + 1)..].Trim('"');
            IEnumerable<string> values = key switch {
                "tag" or "tags" => tags,
                "artist" or "by" => artists,
                "cat" or "category" => categories,
                "is" => IsKeywords,
                _ => []
            };
            completions.AddRange(Matching(values, partial).Select(value => $"{key}:{Quote(value)}"));
        }
        else {
            completions.AddRange(Keywords.Where(keyword => keyword.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
            completions.AddRange(Matching(tags, word).Select(tag => $"tag:{Quote(tag)}"));
            completions.AddRange(Matching(artists, word).Select(artist => $"artist:{Quote(artist)}"));
            completions.AddRange(Matching(categories, word).Select(category => $"cat:{Quote(category)}"));
        }

        return [.. completions.Select(completion => before + negation + completion)
                              .Where(suggestion => !string.Equals(suggestion, text, StringComparison.OrdinalIgnoreCase))
                              .Take(max)];
    }

    // Prefix matches first, then names that merely contain the text
    private static IEnumerable<string> Matching(IEnumerable<string> values, string partial) =>
        values.Where(value => value.Contains(partial, StringComparison.OrdinalIgnoreCase))
              .OrderBy(value => value.StartsWith(partial, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
              .ThenBy(value => value.Length)
              .Take(8);

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
