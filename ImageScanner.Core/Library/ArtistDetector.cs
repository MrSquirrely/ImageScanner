using System.Text.RegularExpressions;

namespace ImageScanner.Core.Library;

/// <summary>
/// Reads the artist from file names that downloaders and galleries write it into, which is far more
/// reliable than asking a model: Danbooru/Gelbooru ("…_drawn_by_stanley_lau__a1b2…") and
/// DeviantArt ("title_by_artist_name_d1a2b3c").
/// </summary>
public static partial class ArtistDetector {
    public static string? FromFileName(string fileName) {
        string name = Path.GetFileNameWithoutExtension(fileName);

        Match booru = BooruRegex().Match(name);
        if (booru.Success) {
            return Clean(booru.Groups["artist"].Value);
        }

        Match deviantArt = DeviantArtRegex().Match(name);
        return deviantArt.Success ? Clean(deviantArt.Groups["artist"].Value) : null;
    }

    private static string? Clean(string artist) {
        string cleaned = ImageLibrary.NormalizeArtist(artist).Trim('-', ' ');
        // "drawn_by_sample" and similar leftovers aren't artists
        return cleaned.Length is > 1 and <= 40 && !cleaned.Equals("sample", StringComparison.OrdinalIgnoreCase) ? cleaned : null;
    }

    // Danbooru's "Download" names end in drawn_by_<artist>__<md5 or sample-md5>; several artists are joined with "_and_"
    [GeneratedRegex(@"drawn_by_(?<artist>[^_].*?)__", RegexOptions.IgnoreCase)]
    private static partial Regex BooruRegex();

    // DeviantArt: <title>_by_<user>_d<6-7 base-36 chars>
    [GeneratedRegex(@"_by_(?<artist>[a-z0-9][a-z0-9_-]{1,40}?)_d[a-z0-9]{6,7}$", RegexOptions.IgnoreCase)]
    private static partial Regex DeviantArtRegex();
}
