using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImageScanner.Plugins.LocalTagger;

public record ParsedResponse(List<string> Keywords, string? Description, string? Category);

/// <summary>
/// Extracts keywords, a description and a category from a local model's reply. Small models often
/// wrap JSON in markdown fences, add commentary or trailing commas, change key casing, or ignore the
/// format entirely and answer with a list, so each of those is handled.
/// </summary>
public static partial class ResponseParser {
    public static ParsedResponse Parse(string? reply) {
        if (string.IsNullOrWhiteSpace(reply)) {
            return new ParsedResponse([], null, null);
        }

        // Reasoning models may emit a thinking section before the answer
        string text = ThinkBlock().Replace(reply, string.Empty).Trim();

        if (TryParseJson(text) is ParsedResponse fromJson && (fromJson.Keywords.Count > 0 || fromJson.Description != null)) {
            return fromJson;
        }

        // Not valid JSON: pull a quoted keyword array out by pattern, then fall back to a plain list
        List<string> keywords = [];
        Match array = KeywordArray().Match(text);
        if (array.Success) {
            keywords.AddRange(QuotedString().Matches(array.Groups["items"].Value).Select(match => match.Groups[1].Value));
        }

        string? description = DescriptionField().Match(text) is { Success: true } descriptionMatch ? descriptionMatch.Groups[1].Value : null;

        if (keywords.Count == 0) {
            keywords.AddRange(text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Where(item => item.Length is > 0 and <= 40 && !item.Contains(':')));
        }

        return new ParsedResponse(keywords, description, null);
    }

    /// <summary>
    /// Reads the reply to a tag prompt ("Write a list of booru-style tags…"): normally a comma-separated
    /// list, but models also answer with one tag per line, bullets, a "Tags:" label or JSON.
    /// </summary>
    public static List<string> ParseTagList(string? reply) {
        string text = CleanReply(reply);
        if (text.Length == 0) {
            return [];
        }

        if (text.Contains('{') && TryParseJson(text) is { Keywords.Count: > 0 } json) {
            return json.Keywords;
        }

        text = LeadingLabel().Replace(text, string.Empty);
        List<string> tags = [];
        foreach (string item in text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string tag = ListMarker().Replace(item, string.Empty).Trim().Trim('"', '\'', '.', '`');

            // Anything sentence-length is commentary, not a tag
            if (tag.Length is > 0 and <= 60) {
                tags.Add(tag);
            }
        }

        return tags;
    }

    /// <summary>
    /// Reads the reply to a caption prompt: prose, possibly with a "Caption:" label, quotes or a code fence.
    /// </summary>
    public static string? ParseCaption(string? reply) {
        string text = LeadingCaptionLabel().Replace(CleanReply(reply), string.Empty);
        text = Whitespace().Replace(text, " ").Trim().Trim('"');
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Reads the reply to the artist prompt. Anything that isn't a short name ("none", "unknown",
    /// "There is no signature…") means the model couldn't tell.
    /// </summary>
    public static string? ParseArtist(string? reply) {
        string text = Whitespace().Replace(CleanReply(reply), " ").Trim();
        text = LeadingArtistLabel().Replace(text, string.Empty).Trim().Trim('"', '\'', '.', '*', ' ');
        if (text.Length is < 2 or > 40 || text.Split(' ').Length > 4 || text.Any(c => c is '.' or ',' or ':' or ';' or '!' or '?')) {
            return null;
        }

        string[] notAnArtist = ["none", "unknown", "n/a", "no artist", "no signature", "not visible", "anonymous", "unsigned", "nothing", "no"];
        return notAnArtist.Any(word => text.Equals(word, StringComparison.OrdinalIgnoreCase)) ? null : text;
    }

    private static string CleanReply(string? reply) {
        if (string.IsNullOrWhiteSpace(reply)) {
            return string.Empty;
        }

        string text = ThinkBlock().Replace(reply, string.Empty);
        return CodeFence().Replace(text, string.Empty).Trim();
    }

    private static ParsedResponse? TryParseJson(string text) {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start) {
            return null;
        }

        string json = TrailingComma().Replace(text[start..(end + 1)], "$1");
        try {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return null;
            }

            List<string> keywords = [];
            string? description = null;
            string? category = null;
            foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
                switch (property.Name.ToLowerInvariant()) {
                    case "keywords" or "tags" or "keyword":
                        keywords.AddRange(ReadStrings(property.Value));
                        break;
                    case "description" or "caption":
                        description = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        break;
                    case "category":
                        category = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        break;
                }
            }

            return new ParsedResponse(keywords, string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                                      string.IsNullOrWhiteSpace(category) ? null : category.Trim());
        }
        catch (JsonException) {
            return null;
        }
    }

    private static IEnumerable<string> ReadStrings(JsonElement element) => element.ValueKind switch {
        JsonValueKind.Array => element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!),
        // Some models answer "keywords": "a, b, c"
        JsonValueKind.String => element.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        _ => []
    };

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"```[a-zA-Z]*")]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"^\s*(here (is|are)[^:\n]*|(booru[- ]style |e621 |danbooru )?(tags|keywords))\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLabel();

    [GeneratedRegex(@"^\s*(here (is|are)[^:\n]*|caption|description)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingCaptionLabel();

    [GeneratedRegex(@"^(the )?(artist|signature|watermark|artist name)( is)?\s*[:\-]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingArtistLabel();

    [GeneratedRegex(@"^\s*(\d+[.)]|[-*•])\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingComma();

    [GeneratedRegex(@"""?(?:keywords|tags)""?\s*:\s*\[(?<items>[^\]]*)", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordArray();

    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""")]
    private static partial Regex QuotedString();

    [GeneratedRegex(@"""?(?:description|caption)""?\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionField();
}
