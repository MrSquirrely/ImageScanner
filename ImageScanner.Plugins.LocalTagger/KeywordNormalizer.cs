using System.Text;
using System.Text.RegularExpressions;

namespace ImageScanner.Plugins.LocalTagger;

public sealed class KeywordOptions {
    public int MaxWordsPerKeyword { get; init; } = 2;
    public bool LatinOnly { get; init; } = true;
    public bool Depluralize { get; init; }
}

/// <summary>
/// Cleans up keywords from small local vision models, which tend to return sentences, duplicates,
/// "CamelCase" run-ons, numbering and stray punctuation instead of tidy tags.
/// </summary>
public static partial class KeywordNormalizer {
    // Words that describe the request rather than the picture
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase) {
        "image", "picture", "photo", "photograph", "keyword", "keywords", "tag", "tags", "description", "none", "n/a", "unknown"
    };

    // "x and y" phrases that name one thing and must not be split
    private static readonly HashSet<string> FixedPhrases = new(StringComparer.OrdinalIgnoreCase) {
        "black and white", "salt and pepper", "rock and roll", "fish and chips", "bed and breakfast", "arts and crafts",
        "hide and seek", "trick or treat", "pros and cons", "rise and shine", "peanut butter and jelly"
    };

    // Plural forms that a simple suffix rule would mangle
    private static readonly Dictionary<string, string> IrregularPlurals = new(StringComparer.OrdinalIgnoreCase) {
        ["people"] = "person", ["men"] = "man", ["women"] = "woman", ["children"] = "child", ["feet"] = "foot",
        ["teeth"] = "tooth", ["mice"] = "mouse", ["geese"] = "goose", ["leaves"] = "leaf", ["knives"] = "knife",
        ["wolves"] = "wolf", ["shelves"] = "shelf", ["lives"] = "life", ["wives"] = "wife", ["halves"] = "half"
    };

    // Words ending in "s" that are not plurals
    private static readonly HashSet<string> SingularS = new(StringComparer.OrdinalIgnoreCase) {
        "glass", "grass", "dress", "class", "moss", "boss", "cross", "chess", "bus", "gas", "lens", "canvas", "cactus",
        "octopus", "virus", "bonus", "status", "campus", "circus", "news", "series", "species", "jeans", "pants",
        "shorts", "glasses", "sunglasses", "scissors", "clothes", "christmas", "texas", "paris", "always", "is", "this",
        "his", "its", "yes", "us", "plus", "atlas", "iris", "analysis", "basis", "oasis", "axis", "chaos", "corps"
    };

    /// <summary>
    /// Normalizes a batch of raw keywords: lower-case, split CamelCase, drop punctuation and noise,
    /// cap the word count, split "x and y" into two keywords, optionally singularize, de-duplicate.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string> rawKeywords, KeywordOptions options) {
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in rawKeywords) {
            foreach (string keyword in NormalizeOne(raw, options)) {
                if (seen.Add(keyword)) {
                    result.Add(keyword);
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> NormalizeOne(string raw, KeywordOptions options) {
        string text = CamelCaseBoundary().Replace(raw.Trim(), " ");
        text = text.ToLowerInvariant();
        if (options.LatinOnly) {
            text = StripNonLatin(text);
        }

        // Drop list markers like "1." or "- " that some models prefix
        text = ListMarker().Replace(text, string.Empty);
        text = text.Replace('_', ' ');
        text = Punctuation().Replace(text, " ");
        text = Whitespace().Replace(text, " ").Trim().Trim('-');
        if (text.Length == 0 || NoiseWords.Contains(text)) {
            yield break;
        }

        string[] words = text.Split(' ');

        if (FixedPhrases.Contains(text)) {
            yield return text;
            yield break;
        }

        // "black and white" stays, but "cats and dogs" becomes two keywords
        if (words.Length == 3 && words[1] is "and" or "or" && !FixedPhrases.Contains(text)) {
            foreach (string part in new[] { words[0], words[2] }) {
                if (IsAcceptable([part])) {
                    yield return options.Depluralize ? Singularize(part) : part;
                }
            }

            yield break;
        }

        if (words.Length > options.MaxWordsPerKeyword || !IsAcceptable(words)) {
            yield break;
        }

        if (options.Depluralize) {
            words[^1] = Singularize(words[^1]);
        }

        yield return string.Join(' ', words);
    }

    private static bool IsAcceptable(string[] words) {
        // Long leading numbers are usually file names, dates or IDs rather than descriptions
        if (LongNumber().IsMatch(words[0])) {
            return false;
        }

        foreach (string word in words) {
            string bare = word.Replace("-", string.Empty);
            if (bare.Length < 2 || NoiseWords.Contains(word)) {
                return false;
            }
        }

        return true;
    }

    private static string StripNonLatin(string text) {
        // Decompose accents (é -> e + ´) and keep only ASCII so tags stay searchable with a plain keyboard
        StringBuilder builder = new(text.Length);
        foreach (char character in text.Normalize(NormalizationForm.FormD)) {
            if (character < 128) {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static string Singularize(string word) {
        if (word.Length < 4 || SingularS.Contains(word)) {
            return word;
        }

        if (IrregularPlurals.TryGetValue(word, out string? singular)) {
            return singular;
        }

        if (word.EndsWith("ies", StringComparison.Ordinal)) {
            return word[..^3] + "y";
        }

        if (word.EndsWith("ches", StringComparison.Ordinal) || word.EndsWith("shes", StringComparison.Ordinal)
            || word.EndsWith("xes", StringComparison.Ordinal) || word.EndsWith("sses", StringComparison.Ordinal)) {
            return word[..^2];
        }

        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal)) {
            return word[..^1];
        }

        return word;
    }

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelCaseBoundary();

    [GeneratedRegex(@"^\s*(\d+[.)]|[-*•])\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"[^\w\s-]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\d{3,}")]
    private static partial Regex LongNumber();
}
