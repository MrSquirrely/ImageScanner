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

    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingComma();

    [GeneratedRegex(@"""?(?:keywords|tags)""?\s*:\s*\[(?<items>[^\]]*)", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordArray();

    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""")]
    private static partial Regex QuotedString();

    [GeneratedRegex(@"""?(?:description|caption)""?\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionField();
}
