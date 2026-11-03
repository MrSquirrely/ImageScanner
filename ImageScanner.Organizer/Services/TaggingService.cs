using ImageScanner.Core.Library;
using ImageScanner.Organizer.Plugins;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer.Services;

public record TaggingResult(int Succeeded, int Failed, string? FirstError);

/// <summary>
/// Runs the active <see cref="ITagSuggestionProvider"/> over a set of images, either storing the
/// results as pending suggestions for review or applying them straight away.
/// </summary>
public sealed class TaggingService(ImageLibrary library, PluginManager plugins, AppSettings settings) {
    private const int AiImageEdge = 1024;

    public async Task<TaggingResult> SuggestAsync(IReadOnlyList<LibraryImage> images, bool autoApply, IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken) {
        ITagSuggestionProvider provider = plugins.ActiveTagProvider
                                          ?? throw new InvalidOperationException("No AI tagging provider is available. Enable one in Settings.");
        if (!provider.IsConfigured) {
            throw new InvalidOperationException($"{provider.DisplayName} isn't set up yet. Configure it under Settings → Plugins.");
        }

        List<string> vocabulary = library.GetAllTagNames();
        List<string> categories = [.. library.GetCategoryCounts().Select(category => category.Name)];
        List<string> artists = [.. library.GetArtistCounts().Select(artist => artist.Name)];

        int done = 0;
        int succeeded = 0;
        int failed = 0;
        string? firstError = null;
        using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try {
            await Parallel.ForEachAsync(images, new ParallelOptions {
                MaxDegreeOfParallelism = Math.Clamp(settings.AiConcurrency, 1, 16),
                CancellationToken = abort.Token
            }, async (image, token) => {
                try {
                    byte[] jpeg = await Task.Run(() => ImageProcessing.EncodeJpeg(image.FilePath, AiImageEdge, 85), token);
                    TagSuggestion suggestion = await provider.SuggestAsync(
                        new TagSuggestionRequest(image, jpeg, vocabulary, categories, settings.MaxSuggestedTags) {
                            KnownArtist = ArtistDetector.FromFileName(image.FileName),
                            ExistingArtists = artists
                        }, token);
                    Apply(image, suggestion, provider, autoApply);
                    Interlocked.Increment(ref succeeded);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) {
                    throw;
                }
                catch (Exception exception) {
                    Interlocked.Increment(ref failed);
                    Interlocked.CompareExchange(ref firstError, exception.Message, null);

                    // A bad key or missing configuration will fail every image; stop instead of burning through the batch
                    if (exception is TagProviderException { IsConfigurationError: true } or InvalidOperationException) {
                        await abort.CancelAsync();
                    }
                }

                progress?.Report((Interlocked.Increment(ref done), images.Count));
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // Aborted because of a configuration error; firstError explains why
        }

        return new TaggingResult(succeeded, failed, firstError);
    }

    private void Apply(LibraryImage image, TagSuggestion suggestion, ITagSuggestionProvider provider, bool autoApply) {
        if (!string.IsNullOrWhiteSpace(suggestion.Description) && string.IsNullOrWhiteSpace(image.Description)) {
            image.Description = suggestion.Description;
            library.Save([image], raiseChanged: false);
        }

        // An artist named in the file name beats whatever the model read off the image
        IReadOnlyList<string> artists = ArtistDetector.FromFileName(image.FileName) is { } known ? [known] : suggestion.Artists;

        if (autoApply) {
            library.AddTags([image.Id], suggestion.Tags);
            if (artists.Count > 0) {
                library.AddArtists([image.Id], artists);
            }

            if (string.IsNullOrEmpty(image.Category) && !string.IsNullOrWhiteSpace(suggestion.Category)) {
                library.SetCategory([image.Id], suggestion.Category);
            }
        }
        else {
            library.SetSuggestions(image.Id, suggestion.Tags, suggestion.Category, provider.DisplayName, artists);
        }
    }
}
