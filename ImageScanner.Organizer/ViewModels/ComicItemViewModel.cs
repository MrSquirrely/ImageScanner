using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;

namespace ImageScanner.Organizer.ViewModels;

public partial class ComicItemViewModel(Comic model, ImageLibrary library, ImageLoader loader) : ObservableObject {
    private BitmapSource? cover;
    private bool coverRequested;

    public Comic Model { get; } = model;
    public string Title => Model.Title;
    public string PageSummary => Model.PageCount == 1 ? "1 page" : $"{Model.PageCount} pages";

    public BitmapSource? Cover {
        get {
            if (!coverRequested) {
                coverRequested = true;
                _ = LoadCoverAsync();
            }

            return cover;
        }
    }

    private async Task LoadCoverAsync() {
        if (library.GetImage(Model.CoverImageId) is LibraryImage coverImage) {
            cover = await loader.LoadThumbnailAsync(coverImage);
            OnPropertyChanged(nameof(Cover));
        }
    }
}

/// <summary>
/// Entry in the sidebar's Library section.
/// </summary>
public partial class QuickFilter(string name, string icon, string query) : ObservableObject {
    public string Name { get; } = name;
    public string Icon { get; } = icon;
    public string Query { get; } = query;
    public bool IsComicsView => Query == ComicsQuery;

    public const string ComicsQuery = "::comics";

    [ObservableProperty]
    public partial int Count { get; set; }
}
