using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Services;

namespace ImageScanner.Organizer.ViewModels;

/// <summary>
/// Page-by-page comic reader with single/two-page spreads and page reordering.
/// </summary>
public partial class ComicReaderViewModel : ObservableObject {
    private readonly ImageLibrary library;
    private readonly ImageLoader loader;
    private readonly int comicId;
    private int loadVersion;

    public ComicReaderViewModel(ImageLibrary library, int comicId, int startIndex) {
        this.library = library;
        this.comicId = comicId;
        loader = new ImageLoader(library);
        Title = library.GetComic(comicId)?.Title ?? "Comic";
        ReloadPages();
        CurrentIndex = Math.Clamp(startIndex, 0, Math.Max(0, Pages.Count - 1));
        _ = LoadCurrentAsync();
    }

    public ObservableCollection<ImageItemViewModel> Pages { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial int CurrentIndex { get; set; }

    [ObservableProperty]
    public partial BitmapSource? LeftPage { get; set; }

    [ObservableProperty]
    public partial BitmapSource? RightPage { get; set; }

    [ObservableProperty]
    public partial bool TwoPageMode { get; set; }

    [ObservableProperty]
    public partial bool RightToLeft { get; set; }

    [ObservableProperty]
    public partial bool FitWidth { get; set; }

    [ObservableProperty]
    public partial ImageItemViewModel? SelectedPage { get; set; }

    public string PageLabel => Pages.Count == 0 ? "No pages" :
                               TwoPageMode && CurrentIndex + 1 < Pages.Count ? $"Pages {CurrentIndex + 1}-{CurrentIndex + 2} of {Pages.Count}" :
                               $"Page {CurrentIndex + 1} of {Pages.Count}";

    private int Step => TwoPageMode ? 2 : 1;

    private void ReloadPages() {
        Pages.Clear();
        foreach (LibraryImage page in library.GetComicPages(comicId)) {
            Pages.Add(new ImageItemViewModel(page, loader));
        }
    }

    partial void OnCurrentIndexChanged(int value) {
        if (Pages.Count > 0 && value >= 0 && value < Pages.Count) {
            SelectedPage = Pages[value];
        }

        OnPropertyChanged(nameof(PageLabel));
        _ = LoadCurrentAsync();
    }

    partial void OnSelectedPageChanged(ImageItemViewModel? value) {
        if (value != null && Pages.IndexOf(value) is int index and >= 0 && index != CurrentIndex) {
            CurrentIndex = index;
        }
    }

    partial void OnTwoPageModeChanged(bool value) {
        OnPropertyChanged(nameof(PageLabel));
        _ = LoadCurrentAsync();
    }

    partial void OnRightToLeftChanged(bool value) => _ = LoadCurrentAsync();

    private async Task LoadCurrentAsync() {
        int version = ++loadVersion;
        if (Pages.Count == 0) {
            LeftPage = RightPage = null;
            return;
        }

        LibraryImage first = Pages[CurrentIndex].Model;
        LibraryImage? second = TwoPageMode && CurrentIndex + 1 < Pages.Count ? Pages[CurrentIndex + 1].Model : null;

        BitmapSource? firstImage = await ImageLoader.LoadFullAsync(first.FilePath);
        BitmapSource? secondImage = second == null ? null : await ImageLoader.LoadFullAsync(second.FilePath);
        if (version != loadVersion) {
            return;
        }

        // Manga reads right-to-left, so the first page of a spread sits on the right
        (LeftPage, RightPage) = RightToLeft && secondImage != null ? (secondImage, firstImage) : (firstImage, secondImage);

        // Warm the OS file cache for the next page so page turns feel instant
        int nextIndex = CurrentIndex + Step;
        if (nextIndex < Pages.Count) {
            string nextPath = Pages[nextIndex].Model.FilePath;
            _ = Task.Run(() => {
                try { File.ReadAllBytes(nextPath); } catch (IOException) { }
            });
        }
    }

    [RelayCommand]
    private void Next() {
        if (CurrentIndex + Step < Pages.Count) CurrentIndex += Step;
    }

    [RelayCommand]
    private void Previous() => CurrentIndex = Math.Max(0, CurrentIndex - Step);

    [RelayCommand]
    private void First() => CurrentIndex = 0;

    [RelayCommand]
    private void Last() => CurrentIndex = Math.Max(0, Pages.Count - 1);

    [RelayCommand]
    private void MovePageUp() => MoveSelected(-1);

    [RelayCommand]
    private void MovePageDown() => MoveSelected(1);

    [RelayCommand]
    private void MakeCover() {
        if (SelectedPage == null) return;
        MoveSelected(-Pages.IndexOf(SelectedPage));
    }

    [RelayCommand]
    private void RemovePage() {
        if (SelectedPage == null) return;
        library.RemoveFromComic([SelectedPage.Id]);
        int index = CurrentIndex;
        ReloadPages();
        CurrentIndex = Math.Clamp(index, 0, Math.Max(0, Pages.Count - 1));
        OnPropertyChanged(nameof(PageLabel));
        _ = LoadCurrentAsync();
    }

    [RelayCommand]
    private void SaveTitle() {
        if (library.GetComic(comicId) is Comic comic && !string.IsNullOrWhiteSpace(Title)) {
            comic.Title = Title.Trim();
            library.UpdateComic(comic);
        }
    }

    private void MoveSelected(int delta) {
        if (SelectedPage == null) return;
        int from = Pages.IndexOf(SelectedPage);
        int to = Math.Clamp(from + delta, 0, Pages.Count - 1);
        if (from < 0 || from == to) return;

        ImageItemViewModel moving = SelectedPage;
        Pages.Move(from, to);
        library.SetComicPages(comicId, [.. Pages.Select(page => page.Id)]);
        SelectedPage = moving;
        CurrentIndex = to;
    }
}
