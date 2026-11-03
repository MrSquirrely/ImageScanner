using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;

namespace ImageScanner.Organizer.ViewModels;

/// <summary>
/// Gallery tile. The thumbnail is loaded the first time the tile is realized on screen.
/// </summary>
public partial class ImageItemViewModel(LibraryImage model, ImageLoader loader) : ObservableObject {
    private BitmapSource? thumbnail;
    private bool thumbnailRequested;
    private string? loadedThumbnailId;

    public LibraryImage Model { get; private set; } = model;

    public int Id => Model.Id;
    public string FileName => Model.FileName;
    public bool IsFavorite => Model.IsFavorite;
    public bool HasPendingSuggestions => Model.HasPendingSuggestions;
    public bool IsComicPage => Model.ComicId != null;
    public int Rating => Model.Rating;
    public string TagSummary => Model.Tags.Count == 0 ? "No tags" : string.Join(", ", Model.Tags);

    public string ToolTipText => $"{Model.FileName}\n{Model.Dimensions}\n{TagSummary}";

    public BitmapSource? Thumbnail {
        get {
            if (!thumbnailRequested || loadedThumbnailId != Model.ThumbnailId) {
                thumbnailRequested = true;
                loadedThumbnailId = Model.ThumbnailId;
                _ = LoadThumbnailAsync();
            }

            return thumbnail;
        }
    }

    private async Task LoadThumbnailAsync() {
        BitmapSource? loaded = await loader.LoadThumbnailAsync(Model);
        if (loaded != null) {
            thumbnail = loaded;
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    /// <summary>
    /// Re-reads state after the library changed the underlying record.
    /// </summary>
    public void Refresh(LibraryImage updated) {
        Model = updated;
        OnPropertyChanged(string.Empty);
    }
}
