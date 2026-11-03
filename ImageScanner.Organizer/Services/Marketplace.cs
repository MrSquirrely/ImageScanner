using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ImageScanner.Organizer.Services;

public record MarketplaceCategory(string Name, string Slug, string Emoji, string? Description) {
    /// <summary>"AI Tagging Plugins" → "AI Tagging".</summary>
    public string ShortName => Name.EndsWith(" Plugins", StringComparison.OrdinalIgnoreCase) ? Name[..^" Plugins".Length] : Name;
}

/// <summary>
/// One plugin listed in a GitHub discussion. The fields come from the discussion form in
/// .github/DISCUSSION_TEMPLATE; <see cref="IsInstallable"/> is false when the post is missing a valid download.
/// </summary>
public record MarketplaceListing(
    int Number,
    string Title,
    MarketplaceCategory Category,
    string Author,
    string? AuthorAvatarUrl,
    bool IsTeamPost,
    string DiscussionUrl,
    int Likes,
    int Comments,
    DateTime UpdatedAt,
    string Name,
    string? PluginId,
    string? Version,
    string Summary,
    string? DownloadUrl,
    string? Sha256,
    string? SourceUrl) {
    public bool IsInstallable => PluginId != null && DownloadUrl != null && Sha256 != null;
}

/// <summary>
/// Reads plugin listings from the PixShelf repository's GitHub Discussions. Every category whose name ends in
/// "Plugins" is a marketplace category, so new ones can be added on GitHub without updating the app.
/// Anonymous GitHub API calls are limited to 60 an hour, so results are cached on disk and refreshed with
/// conditional requests (a "not modified" answer doesn't count against the limit).
/// </summary>
public sealed partial class MarketplaceClient {
    public const string Repository = "MrSquirrely/ImageScanner";
    public const string DiscussionsUrl = $"https://github.com/{Repository}/discussions";
    public const string SubmitUrl = $"https://github.com/{Repository}/discussions/new/choose";

    private const int MaxPages = 5;
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);

    // Roles GitHub reports for repository maintainers; their posts get a "Team" badge
    private static readonly string[] TeamAssociations = ["OWNER", "MEMBER", "COLLABORATOR"];

    private static readonly HttpClient Http = CreateClient();

    private static string CachePath => Path.Combine(AppSettings.AppDataDirectory, "marketplace-cache.json");

    public record Result(IReadOnlyList<MarketplaceListing> Listings, DateTime FetchedAt, string? Warning);

    /// <summary>
    /// Returns the listings, from the cache when it's recent unless <paramref name="force"/> is set. Network
    /// problems and the rate limit fall back to the cache with a warning instead of failing.
    /// </summary>
    public async Task<Result> GetListingsAsync(bool force, CancellationToken cancellationToken) {
        Cache? cache = LoadCache();
        if (!force && cache != null && DateTime.UtcNow - cache.FetchedAt < FreshFor) {
            return new Result(Parse(cache.Pages), cache.FetchedAt, null);
        }

        try {
            List<string> pages = [];
            List<string?> etags = [];
            string? url = $"https://api.github.com/repos/{Repository}/discussions?per_page=100";
            for (int page = 0; page < MaxPages && url != null; page++) {
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                string? knownEtag = cache?.ETags.ElementAtOrDefault(page);
                if (knownEtag != null) {
                    request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(knownEtag));
                }

                using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotModified && cache != null && page < cache.Pages.Count) {
                    pages.Add(cache.Pages[page]);
                    etags.Add(knownEtag);
                }
                else if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) {
                    return Fallback(cache, "GitHub's hourly limit for browsing without an account was reached. Showing the last list; try again later.");
                }
                else {
                    response.EnsureSuccessStatusCode();
                    pages.Add(await response.Content.ReadAsStringAsync(cancellationToken));
                    etags.Add(response.Headers.ETag?.ToString());
                }

                url = NextPage(response);
            }

            Cache fresh = new(DateTime.UtcNow, pages, etags);
            SaveCache(fresh);
            return new Result(Parse(pages), fresh.FetchedAt, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested) {
            return Fallback(cache, "Couldn't reach GitHub. Check your internet connection." + (cache != null ? " Showing the last list." : string.Empty));
        }
    }

    private static Result Fallback(Cache? cache, string warning) =>
        cache == null ? new Result([], DateTime.MinValue, warning) : new Result(Parse(cache.Pages), cache.FetchedAt, warning);

    private static string? NextPage(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Link", out IEnumerable<string>? links)
            ? NextLink().Match(string.Join(",", links)) is { Success: true } match ? match.Groups[1].Value : null
            : null;

    #region Parsing

    public static List<MarketplaceListing> Parse(IEnumerable<string> pages) {
        List<MarketplaceListing> listings = [];
        foreach (string page in pages) {
            foreach (Discussion discussion in JsonSerializer.Deserialize<List<Discussion>>(page) ?? []) {
                if (discussion.Category is not { } category
                    || !category.Name.EndsWith("Plugins", StringComparison.OrdinalIgnoreCase)
                    || discussion.State == "closed") {
                    continue;
                }

                Dictionary<string, string> fields = ReadFormFields(discussion.Body ?? string.Empty);
                string? sha = Field(fields, "SHA-256")?.Replace(" ", string.Empty);
                string? download = Field(fields, "Download URL");
                listings.Add(new MarketplaceListing(
                    discussion.Number,
                    discussion.Title,
                    new MarketplaceCategory(category.Name, category.Slug, EmojiFor(category.Emoji), category.Description),
                    discussion.User?.Login ?? "unknown",
                    discussion.User?.AvatarUrl is { } avatar ? $"{avatar}{(avatar.Contains('?') ? '&' : '?')}s=64" : null,
                    TeamAssociations.Contains(discussion.AuthorAssociation),
                    discussion.HtmlUrl,
                    discussion.Reactions?.PlusOne ?? 0,
                    discussion.Comments,
                    discussion.UpdatedAt,
                    Field(fields, "Plugin name") ?? discussion.Title,
                    Field(fields, "Plugin ID"),
                    Field(fields, "Version")?.TrimStart('v', 'V'),
                    Summarize(Field(fields, "Description") ?? (fields.Count == 0 ? discussion.Body : null) ?? string.Empty),
                    download != null && Uri.TryCreate(download, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps ? download : null,
                    sha != null && Sha256Regex().IsMatch(sha) ? sha.ToUpperInvariant() : null,
                    Field(fields, "Source code")));
            }
        }

        return [.. listings.OrderByDescending(listing => listing.IsTeamPost).ThenByDescending(listing => listing.Likes).ThenByDescending(listing => listing.UpdatedAt)];
    }

    /// <summary>
    /// GitHub renders a discussion form as "### Label" headings, each followed by the answer
    /// ("_No response_" when an optional field was left empty).
    /// </summary>
    public static Dictionary<string, string> ReadFormFields(string body) {
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
        string? label = null;
        List<string> value = [];

        void Flush() {
            string text = string.Join("\n", value).Trim();
            if (label != null && text.Length > 0 && text != "_No response_") {
                fields[label] = text;
            }
        }

        foreach (string line in body.Replace("\r\n", "\n").Split('\n')) {
            if (line.StartsWith("### ", StringComparison.Ordinal)) {
                Flush();
                label = line[4..].Trim();
                value.Clear();
            }
            else {
                value.Add(line);
            }
        }

        Flush();
        return fields;
    }

    private static string? Field(Dictionary<string, string> fields, string label) =>
        fields.TryGetValue(label, out string? value) ? value.Trim().Trim('`').Trim() : null;

    /// <summary>The first paragraph as plain text, for the card.</summary>
    private static string Summarize(string markdown) {
        string paragraph = markdown.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        string text = MarkdownLink().Replace(paragraph, "$1");
        text = MarkdownNoise().Replace(text, string.Empty);
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length <= 280 ? text : text[..277].TrimEnd() + "…";
    }

    // Category emoji arrive as GitHub shortcodes; WPF can't render most color emoji anyway, so map the
    // likely ones to symbols and fall back to a puzzle piece
    private static string EmojiFor(string? shortcode) => shortcode switch {
        ":robot:" => "🤖",
        ":framed_picture:" or ":frame_with_picture:" => "🖼",
        ":inbox_tray:" => "📥",
        ":toolbox:" or ":hammer_and_wrench:" or ":wrench:" => "🧰",
        ":art:" => "🎨",
        ":mag:" => "🔍",
        _ => "🧩"
    };

    #endregion

    #region Cache

    private sealed record Cache(DateTime FetchedAt, List<string> Pages, List<string?> ETags);

    private static Cache? LoadCache() {
        try {
            return File.Exists(CachePath) ? JsonSerializer.Deserialize<Cache>(File.ReadAllText(CachePath)) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException) {
            return null;
        }
    }

    private static void SaveCache(Cache cache) {
        try {
            Directory.CreateDirectory(AppSettings.AppDataDirectory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
        }
        catch (IOException) {
            // The cache only saves API calls; failing to write it isn't worth bothering anyone about
        }
    }

    #endregion

    private static HttpClient CreateClient() {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PixShelf", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    [GeneratedRegex(@"<([^>]+)>;\s*rel=""next""")]
    private static partial Regex NextLink();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"[*_`#>]+")]
    private static partial Regex MarkdownNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // The subset of GitHub's discussion JSON the marketplace uses
    private sealed record Discussion(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("comments")] int Comments,
        [property: JsonPropertyName("updated_at")] DateTime UpdatedAt,
        [property: JsonPropertyName("author_association")] string? AuthorAssociation,
        [property: JsonPropertyName("user")] DiscussionUser? User,
        [property: JsonPropertyName("category")] DiscussionCategory? Category,
        [property: JsonPropertyName("reactions")] DiscussionReactions? Reactions);

    private sealed record DiscussionUser([property: JsonPropertyName("login")] string Login, [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

    private sealed record DiscussionCategory(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("emoji")] string? Emoji,
        [property: JsonPropertyName("description")] string? Description);

    private sealed record DiscussionReactions([property: JsonPropertyName("+1")] int PlusOne);
}
