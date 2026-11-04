using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Services;
using ImageScanner.Organizer.Views;
using ImageScanner.Plugins;

using MessageBox = HandyControl.Controls.MessageBox;

namespace ImageScanner.Organizer.ViewModels;

public partial class MainViewModel : ObservableObject {
    public static readonly string[] SortOptions = ["Date added", "Name", "Folder", "Date modified", "File size", "Rating"];

    private readonly Dictionary<int, ImageItemViewModel> itemCache = [];
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer searchTimer;
    private IReadOnlyList<ImageItemViewModel> selection = [];
    private CancellationTokenSource? jobCancellation;
    private CancellationTokenSource? aiCancellation;
    private readonly Queue<(List<LibraryImage> Images, bool AutoApply)> aiQueue = new();
    private int aiDone;
    private int aiTotal;
    private bool suppressFilterChange;
    private int previewVersion;

    public MainViewModel(ImageLibrary library, PluginManager plugins, AppSettings settings) {
        Library = library;
        Plugins = plugins;
        Settings = settings;
        Loader = new ImageLoader(library);
        Tagging = new TaggingService(library, plugins, settings);
        TileSize = settings.TileSize;
        SortBy = settings.SortBy;

        QuickFilters = [
            new QuickFilter("All images", "", string.Empty),
            new QuickFilter("Favorites", "", "is:fav"),
            new QuickFilter("Untagged", "", "is:untagged"),
            new QuickFilter("Uncategorized", "", "is:uncategorized"),
            new QuickFilter("Needs AI review", "", "is:pending"),
            new QuickFilter("Comics", "", QuickFilter.ComicsQuery)
        ];
        suppressFilterChange = true;
        SelectedQuickFilter = QuickFilters[0];
        suppressFilterChange = false;

        plugins.SelectionProvider = () => [.. selection.Select(item => item.Model)];
        plugins.SearchHandler = query => {
            SelectedQuickFilter = QuickFilters[0];
            SearchText = query;
        };
        plugins.RescanHandler = RescanForPluginAsync;

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        refreshTimer.Tick += (_, _) => {
            refreshTimer.Stop();
            RefreshAll();
        };
        searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        searchTimer.Tick += (_, _) => {
            searchTimer.Stop();
            RefreshImages();
        };

        library.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(ScheduleRefresh);
        RefreshAll();
    }

    /// <summary>Raised when the gallery should re-select these image ids after a refresh.</summary>
    public event Action<IReadOnlyCollection<int>>? RestoreSelectionRequested;

    public ImageLibrary Library { get; }
    public PluginManager Plugins { get; }
    public AppSettings Settings { get; }
    public ImageLoader Loader { get; }
    public TaggingService Tagging { get; }

    public ObservableCollection<QuickFilter> QuickFilters { get; }
    public ObservableCollection<CategoryCount> Categories { get; } = [];
    public ObservableCollection<TagCount> Tags { get; } = [];
    public ObservableCollection<ComicItemViewModel> Comics { get; } = [];
    public ObservableCollection<string> AllTagNames { get; } = [];
    public ObservableCollection<string> AllCategoryNames { get; } = [];
    public ObservableCollection<string> DetailTags { get; } = [];
    public ObservableCollection<string> SuggestedTags { get; } = [];
    public ObservableCollection<ArtistCount> Artists { get; } = [];
    public ObservableCollection<string> AllArtistNames { get; } = [];
    public ObservableCollection<string> DetailArtists { get; } = [];
    public ObservableCollection<string> SuggestedArtists { get; } = [];

    public string[] Sorts => SortOptions;

    [ObservableProperty]
    public partial ObservableCollection<ImageItemViewModel> Images { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TagFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SortBy { get; set; }

    [ObservableProperty]
    public partial double TileSize { get; set; }

    [ObservableProperty]
    public partial QuickFilter? SelectedQuickFilter { get; set; }

    [ObservableProperty]
    public partial CategoryCount? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial TagCount? SelectedTag { get; set; }

    [ObservableProperty]
    public partial ArtistCount? SelectedArtist { get; set; }

    [ObservableProperty]
    public partial bool ShowComics { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    // Details panel
    [ObservableProperty]
    public partial ImageItemViewModel? Current { get; set; }

    [ObservableProperty]
    public partial int SelectionCount { get; set; }

    [ObservableProperty]
    public partial BitmapSource? Preview { get; set; }

    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewArtistText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CategoryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SuggestedCategory { get; set; }

    [ObservableProperty]
    public partial string? CurrentComicTitle { get; set; }

    // Background job
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string JobText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double JobProgress { get; set; }

    [ObservableProperty]
    public partial bool JobIndeterminate { get; set; }

    // AI tagging runs alongside imports, so a long batch never blocks the rest of the app
    [ObservableProperty]
    public partial bool IsAiBusy { get; set; }

    [ObservableProperty]
    public partial string AiJobText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double AiJobProgress { get; set; }

    [ObservableProperty]
    public partial bool AiJobIndeterminate { get; set; }

    public double TileHeight => TileSize + 30;

    public bool IsEmpty => ShowComics ? Comics.Count == 0 : Images.Count == 0;

    public string EmptyMessage => Library.Count == 0
                                      ? "Your library is empty.\nClick Import to scan a folder or drive."
                                      : ShowComics ? "No comics yet.\nSelect pages and choose \"Group as comic\", or import a folder of numbered pages."
                                      : "No images match this view.";

    public string CurrentFileSize => Current == null ? string.Empty : FormatSize(Current.Model.FileSize);

    private static string FormatSize(long bytes) => bytes switch {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):0.0} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):0.0} MB",
        >= 1 << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B"
    };

    public bool HasSelection => SelectionCount > 0;

    /// <summary>True when every selected image is a favorite; drives the Favorite button's on state.</summary>
    public bool IsSelectionFavorite => selection.Count > 0 && selection.All(item => item.IsFavorite);
    public bool IsMultiSelection => SelectionCount > 1;
    public bool HasSuggestions => SuggestedTags.Count > 0 || SuggestedArtists.Count > 0 || !string.IsNullOrEmpty(SuggestedCategory);
    public string SelectionHeader => SelectionCount switch {
        0 => "Nothing selected",
        1 => Current?.FileName ?? string.Empty,
        _ => $"{SelectionCount} images selected"
    };

    public IReadOnlyList<ImageItemViewModel> Selection => selection;

    private IEnumerable<int> SelectedIds => selection.Select(item => item.Id);

    private System.Windows.Window? Owner => Application.Current.MainWindow;

    #region Refresh & search

    private void ScheduleRefresh() {
        refreshTimer.Stop();
        refreshTimer.Start();
    }

    public void RefreshAll() {
        suppressFilterChange = true;
        try {
            string? selectedCategory = SelectedCategory?.Name;
            string? selectedTag = SelectedTag?.Name;
            string? selectedArtist = SelectedArtist?.Name;

            List<LibraryImage> all = Library.GetAllImages();
            foreach (QuickFilter filter in QuickFilters) {
                filter.Count = filter.IsComicsView ? Library.GetComics().Count : Library.Search(SearchQuery.Parse(filter.Query)).Count;
            }

            Replace(Categories, Library.GetCategoryCounts());
            Replace(AllCategoryNames, Categories.Select(category => category.Name));
            List<TagCount> tags = Library.GetTagCounts();
            Replace(AllTagNames, tags.Select(tag => tag.Name));
            Replace(Tags, tags.Where(tag => TagFilter.Length == 0 || tag.Name.Contains(TagFilter, StringComparison.OrdinalIgnoreCase)));
            List<ArtistCount> artists = Library.GetArtistCounts();
            Replace(Artists, artists);
            Replace(AllArtistNames, artists.Select(artist => artist.Name));
            Replace(Comics, Library.GetComics().Select(comic => new ComicItemViewModel(comic, Library, Loader)));

            SelectedCategory = Categories.FirstOrDefault(category => category.Name == selectedCategory);
            SelectedTag = Tags.FirstOrDefault(tag => tag.Name == selectedTag);
            SelectedArtist = Artists.FirstOrDefault(artist => artist.Name == selectedArtist);
            StatusText = $"{all.Count:N0} images · {Tags.Count:N0} tags · {Comics.Count:N0} comics";
        }
        finally {
            suppressFilterChange = false;
        }

        RefreshImages();
    }

    private string BuildQuery() {
        string baseQuery = SelectedCategory != null ? $"cat:\"{SelectedCategory.Name}\""
                           : SelectedTag != null ? $"tag:\"{SelectedTag.Name}\""
                           : SelectedArtist != null ? $"artist:\"{SelectedArtist.Name}\""
                           : SelectedQuickFilter is { IsComicsView: false } quick ? quick.Query
                           : string.Empty;
        return $"{baseQuery} {SearchText}".Trim();
    }

    public void RefreshImages() {
        if (ShowComics) {
            string text = SearchText.Trim();
            Replace(Comics, Library.GetComics()
                                   .Where(comic => text.Length == 0 || comic.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
                                   .Select(comic => new ComicItemViewModel(comic, Library, Loader)));
            RaiseEmptyState();
            return;
        }

        string query = BuildQuery();
        List<LibraryImage> results = Library.Search(SearchQuery.Parse(query));
        IEnumerable<LibraryImage> sorted = query.Contains("comicid:", StringComparison.OrdinalIgnoreCase)
                                               ? results.OrderBy(image => image.PageNumber)
                                               : Sort(results);

        List<ImageItemViewModel> items = [];
        foreach (LibraryImage image in sorted) {
            if (!itemCache.TryGetValue(image.Id, out ImageItemViewModel? item)) {
                item = new ImageItemViewModel(image, Loader);
                itemCache[image.Id] = item;
            }
            else {
                item.Refresh(image);
            }

            items.Add(item);
        }

        HashSet<int> selectedIds = [.. SelectedIds];
        if (!items.SequenceEqual(Images)) {
            Images = new ObservableCollection<ImageItemViewModel>(items);
            RestoreSelectionRequested?.Invoke(selectedIds);
        }

        UpdateDetails();
    }

    private IEnumerable<LibraryImage> Sort(IEnumerable<LibraryImage> images) => SortBy switch {
        "Name" => images.OrderBy(image => image.FileName, NaturalStringComparer.Instance),
        "Folder" => images.OrderBy(image => image.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(image => image.FileName, NaturalStringComparer.Instance),
        "Date modified" => images.OrderByDescending(image => image.LastModified),
        "File size" => images.OrderByDescending(image => image.FileSize),
        "Rating" => images.OrderByDescending(image => image.Rating).ThenByDescending(image => image.IsFavorite),
        _ => images.OrderByDescending(image => image.DateAdded).ThenBy(image => image.FilePath, NaturalStringComparer.Instance)
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items) {
        target.Clear();
        foreach (T item in items) {
            target.Add(item);
        }
    }

    partial void OnSearchTextChanged(string value) {
        searchTimer.Stop();
        searchTimer.Start();
        UpdateSearchSuggestions(value);
    }

    public ObservableCollection<string> SearchSuggestions { get; } = [];

    private void UpdateSearchSuggestions(string text) {
        // Arrow keys and picking a suggestion put that suggestion in the box; keep the list so navigation still works
        if (SearchSuggestions.Contains(text)) {
            return;
        }

        List<string> suggestions = SearchCompletion.Suggest(text, AllTagNames, AllArtistNames, AllCategoryNames);
        if (!suggestions.SequenceEqual(SearchSuggestions)) {
            Replace(SearchSuggestions, suggestions);
        }
    }

    partial void OnTagFilterChanged(string value) =>
        Replace(Tags, Library.GetTagCounts().Where(tag => value.Length == 0 || tag.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));

    partial void OnSortByChanged(string value) {
        Settings.SortBy = value;
        Settings.Save();
        RefreshImages();
    }

    partial void OnTileSizeChanged(double value) {
        Settings.TileSize = value;
        OnPropertyChanged(nameof(TileHeight));
    }

    partial void OnImagesChanged(ObservableCollection<ImageItemViewModel> value) => RaiseEmptyState();

    partial void OnShowComicsChanged(bool value) => RaiseEmptyState();

    private void RaiseEmptyState() {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnSelectedQuickFilterChanged(QuickFilter? value) {
        if (value == null || suppressFilterChange) return;
        suppressFilterChange = true;
        SelectedCategory = null;
        SelectedTag = null;
        SelectedArtist = null;
        suppressFilterChange = false;
        ShowComics = value.IsComicsView;
        RefreshImages();
    }

    partial void OnSelectedCategoryChanged(CategoryCount? value) {
        if (value == null || suppressFilterChange) return;
        suppressFilterChange = true;
        SelectedQuickFilter = null;
        SelectedTag = null;
        SelectedArtist = null;
        suppressFilterChange = false;
        ShowComics = false;
        RefreshImages();
    }

    partial void OnSelectedTagChanged(TagCount? value) {
        if (value == null || suppressFilterChange) return;
        suppressFilterChange = true;
        SelectedQuickFilter = null;
        SelectedCategory = null;
        SelectedArtist = null;
        suppressFilterChange = false;
        ShowComics = false;
        RefreshImages();
    }

    partial void OnSelectedArtistChanged(ArtistCount? value) {
        if (value == null || suppressFilterChange) return;
        suppressFilterChange = true;
        SelectedQuickFilter = null;
        SelectedCategory = null;
        SelectedTag = null;
        suppressFilterChange = false;
        ShowComics = false;
        RefreshImages();
    }

    #endregion

    #region Selection & details

    public void SetSelection(IReadOnlyList<ImageItemViewModel> items) {
        selection = items;
        UpdateDetails();
    }

    private void UpdateDetails() {
        // Drop selected items that are no longer in the results
        selection = [.. selection.Where(item => Library.GetImage(item.Id) != null)];
        SelectionCount = selection.Count;
        ImageItemViewModel? newCurrent = selection.Count == 1 ? selection[0] : null;
        bool currentChanged = !ReferenceEquals(newCurrent, Current);
        Current = newCurrent;

        IEnumerable<string> tags = selection.Count == 0
                                       ? []
                                       : selection.Skip(1).Aggregate((IEnumerable<string>)selection[0].Model.Tags, (common, item) => common.Intersect(item.Model.Tags));
        Replace(DetailTags, tags.OrderBy(tag => tag));
        Replace(SuggestedTags, selection.SelectMany(item => item.Model.SuggestedTags).Distinct().OrderBy(tag => tag));

        IEnumerable<string> artists = selection.Count == 0
                                          ? []
                                          : selection.Skip(1).Aggregate((IEnumerable<string>)selection[0].Model.Artists,
                                                                        (common, item) => common.Intersect(item.Model.Artists, StringComparer.OrdinalIgnoreCase));
        Replace(DetailArtists, artists.OrderBy(artist => artist, StringComparer.OrdinalIgnoreCase));
        Replace(SuggestedArtists, selection.SelectMany(item => item.Model.SuggestedArtists).Distinct(StringComparer.OrdinalIgnoreCase).Order());
        SuggestedCategory = selection.Select(item => item.Model.SuggestedCategory).FirstOrDefault(category => category != null);

        List<string?> categories = [.. selection.Select(item => item.Model.Category).Distinct()];
        CategoryText = categories.Count == 1 ? categories[0] ?? string.Empty : string.Empty;

        CurrentComicTitle = Current?.Model.ComicId is int comicId && Library.GetComic(comicId) is Comic comic
                                ? $"Page {Current.Model.PageNumber} of {comic.Title}"
                                : null;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(IsSelectionFavorite));
        OnPropertyChanged(nameof(SelectionHeader));
        OnPropertyChanged(nameof(CurrentFileSize));
        RaiseEmptyState();

        if (currentChanged) {
            _ = LoadPreviewAsync();
        }
    }

    private async Task LoadPreviewAsync() {
        int version = ++previewVersion;
        LibraryImage? image = Current?.Model;
        Preview = Current?.Thumbnail;
        if (image == null) {
            return;
        }

        BitmapSource? full = await ImageLoader.LoadFullAsync(image.FilePath, Math.Min(image.Width, 900));
        if (version == previewVersion && full != null) {
            Preview = full;
        }
    }

    #endregion

    #region Tagging & categories

    [RelayCommand]
    private void AddTag() {
        List<string> tags = [.. NewTagText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        if (tags.Count == 0 || selection.Count == 0) return;
        Library.AddTags(SelectedIds, tags);
        NewTagText = string.Empty;
    }

    [RelayCommand]
    private void RemoveTag(string tag) => Library.RemoveTags(SelectedIds, [tag]);

    [RelayCommand]
    private void AddArtist() {
        List<string> artists = [.. NewArtistText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        if (artists.Count == 0 || selection.Count == 0) return;
        Library.AddArtists(SelectedIds, artists);
        NewArtistText = string.Empty;
    }

    [RelayCommand]
    private void RemoveArtist(string artist) => Library.RemoveArtists(SelectedIds, [artist]);

    [RelayCommand]
    private void AcceptSuggestedArtist(string artist) => Library.AcceptSuggestedArtist(SelectedIds, artist);

    [RelayCommand]
    private void RenameArtist(ArtistCount? artist) {
        if (artist != null && InputDialog.Show(Owner, "Rename artist", "New name (an existing artist merges the two)", artist.Name) is { Length: > 0 } name) {
            Library.RenameArtist(artist.Name, name);
        }
    }

    [RelayCommand]
    private void DeleteArtist(ArtistCount? artist) {
        if (artist != null && Confirm($"Remove artist \"{artist.Name}\" from {artist.Count:N0} images?")) {
            Library.DeleteArtist(artist.Name);
        }
    }

    [RelayCommand]
    private void ApplyCategory() {
        if (selection.Count == 0) return;
        Library.SetCategory(SelectedIds, CategoryText);
    }

    [RelayCommand]
    private void SetCategoryTo(string? category) {
        if (selection.Count == 0) return;
        Library.SetCategory(SelectedIds, category);
    }

    [RelayCommand]
    private void ToggleFavorite() {
        if (selection.Count == 0) return;
        Library.SetFavorite(SelectedIds, !selection.All(item => item.IsFavorite));
    }

    [RelayCommand]
    private void SetRating(int rating) {
        if (selection.Count == 0) return;
        Library.SetRating(SelectedIds, rating);
    }

    [RelayCommand]
    private void AcceptSuggestion(string tag) => Library.AcceptSuggestions(SelectedIds, [tag], acceptCategory: false);

    [RelayCommand]
    private void AcceptAllSuggestions() => Library.AcceptSuggestions(SelectedIds);

    [RelayCommand]
    private void RejectSuggestions() => Library.RejectSuggestions(SelectedIds);

    [RelayCommand]
    private void AcceptAllPending() {
        List<int> pending = [.. Library.Search(SearchQuery.Parse("is:pending")).Select(image => image.Id)];
        if (pending.Count > 0 && Confirm($"Apply AI suggestions to {pending.Count:N0} images?")) {
            Library.AcceptSuggestions(pending);
        }
    }

    [RelayCommand]
    private void SuggestTags() {
        List<LibraryImage> images = [.. selection.Select(item => item.Model)];
        if (images.Count == 0) {
            Growl.Info("Select one or more images first.");
            return;
        }

        if (images.Count > 25 && !Confirm($"Ask AI to tag {images.Count:N0} images? Each image is one AI request (paid with an online service).")) {
            return;
        }

        _ = QueueAiTaggingAsync(images, Settings.AutoApplySuggestions);
    }

    [RelayCommand]
    private void SuggestTagsForUntagged() {
        List<LibraryImage> images = Library.Search(SearchQuery.Parse("is:untagged -is:pending"));
        if (images.Count == 0) {
            Growl.Info("Every image already has tags or pending suggestions.");
            return;
        }

        if (Confirm($"Ask AI to suggest tags for {images.Count:N0} untagged images? Each image is one AI request (paid with an online service).")) {
            _ = QueueAiTaggingAsync(images, Settings.AutoApplySuggestions);
        }
    }

    /// <summary>
    /// Tags images in the background. Images queued while a batch is running join it, so importing
    /// several folders produces one progress bar rather than a refused second job.
    /// </summary>
    private async Task QueueAiTaggingAsync(IReadOnlyList<LibraryImage> images, bool autoApply) {
        aiQueue.Enqueue(([.. images], autoApply));
        if (IsAiBusy) {
            aiTotal += images.Count;
            ReportAi();
            return;
        }

        IsAiBusy = true;
        aiDone = 0;
        aiTotal = images.Count;
        aiCancellation = new CancellationTokenSource();
        ReportAi();
        try {
            while (aiQueue.TryDequeue(out (List<LibraryImage> Images, bool AutoApply) batch)) {
                int offset = aiDone;
                Progress<(int Done, int Total)> progress = new(value => {
                    aiDone = Math.Max(aiDone, offset + value.Done);
                    ReportAi();
                });

                TaggingResult result = await Tagging.SuggestAsync(batch.Images, batch.AutoApply, progress, aiCancellation.Token);
                aiDone = offset + batch.Images.Count;
                ShowTaggingResult(result, batch.AutoApply);

                // The batch stopped early on a configuration problem; the queued ones would fail the same way
                if (result.Succeeded + result.Failed < batch.Images.Count) {
                    break;
                }
            }
        }
        catch (OperationCanceledException) {
            Growl.Info("AI tagging cancelled.");
        }
        catch (Exception exception) {
            Growl.Error($"AI tagging failed: {exception.Message}");
        }
        finally {
            aiQueue.Clear();
            IsAiBusy = false;
            aiCancellation.Dispose();
            aiCancellation = null;
            RefreshAll();
        }
    }

    private void ReportAi() {
        // Until the first image finishes, the AI may still be loading its model, which can take a minute or more
        AiJobText = aiDone == 0 ? $"AI tagging 0 / {aiTotal:N0} · starting the AI…" : $"AI tagging {aiDone:N0} / {aiTotal:N0}";
        AiJobIndeterminate = aiDone == 0;
        AiJobProgress = aiTotal > 0 ? aiDone * 100.0 / aiTotal : 0;
    }

    [RelayCommand]
    private void CancelAiJob() => aiCancellation?.Cancel();

    private static void ShowTaggingResult(TaggingResult result, bool autoApply) {
        if (result.Failed > 0) {
            Growl.Warning($"AI tagged {result.Succeeded:N0} images, {result.Failed:N0} failed: {result.FirstError}");
        }
        else if (result.Succeeded > 0) {
            Growl.Success(autoApply
                              ? $"AI tagged {result.Succeeded:N0} images."
                              : $"AI suggested tags for {result.Succeeded:N0} images. Review them under \"Needs AI review\".");
        }
    }

    [RelayCommand]
    private void AddCategory() {
        if (InputDialog.Show(Owner, "New category", "Category name") is { Length: > 0 } name) {
            Library.AddCategory(name);
        }
    }

    [RelayCommand]
    private void RenameCategory(CategoryCount? category) {
        if (category != null && InputDialog.Show(Owner, "Rename category", "New name", category.Name) is { Length: > 0 } name) {
            Library.RenameCategory(category.Name, name);
        }
    }

    [RelayCommand]
    private void DeleteCategory(CategoryCount? category) {
        if (category != null && Confirm($"Delete category \"{category.Name}\"? {category.Count:N0} images will become uncategorized.")) {
            Library.DeleteCategory(category.Name);
        }
    }

    [RelayCommand]
    private void RenameTag(TagCount? tag) {
        if (tag != null && InputDialog.Show(Owner, "Rename tag", "New name (an existing tag name merges the two)", tag.Name) is { Length: > 0 } name) {
            Library.RenameTag(tag.Name, name);
        }
    }

    [RelayCommand]
    private void DeleteTag(TagCount? tag) {
        if (tag != null && Confirm($"Remove tag \"{tag.Name}\" from {tag.Count:N0} images?")) {
            Library.DeleteTag(tag.Name);
        }
    }

    [RelayCommand]
    private void ClearSearch() {
        SearchText = string.Empty;
        SelectedQuickFilter = QuickFilters[0];
    }

    #endregion

    #region Comics

    [RelayCommand]
    private void GroupAsComic() {
        if (selection.Count == 0) return;
        List<LibraryImage> pages = [.. selection.Select(item => item.Model).OrderBy(image => image.Folder).ThenBy(image => image.FileName, NaturalStringComparer.Instance)];
        string suggestedTitle = Path.GetFileName(pages[0].Folder);
        if (InputDialog.Show(Owner, "Group as comic", "Comic title (pages are ordered by file name; reorder them in the reader)", suggestedTitle) is { Length: > 0 } title) {
            Comic comic = Library.CreateComic(title, [.. pages.Select(page => page.Id)]);
            Growl.Success($"Created \"{comic.Title}\" with {comic.PageCount} pages.");
        }
    }

    [RelayCommand]
    private void AddToComic(ComicItemViewModel? comic) {
        if (comic != null && selection.Count > 0) {
            Library.AddToComic(comic.Model.Id, [.. SelectedIds]);
        }
    }

    [RelayCommand]
    private void RemoveFromComic() => Library.RemoveFromComic([.. SelectedIds]);

    [RelayCommand]
    private void DetectComics() {
        List<ComicCandidate> candidates = ComicDetector.FindCandidates(Library.GetAllImages());
        foreach (ComicCandidate candidate in candidates) {
            Library.CreateComic(candidate.Title, [.. candidate.Pages.Select(page => page.Id)], isAutoDetected: true, candidate.FolderPath);
        }

        Growl.Info(candidates.Count == 0 ? "No new comics found." : $"Grouped {candidates.Count} comics.");
    }

    [RelayCommand]
    private void OpenComic(ComicItemViewModel? comic) {
        if (comic != null) {
            new ComicReaderWindow(new ComicReaderViewModel(Library, comic.Model.Id, 0)) { Owner = Owner }.Show();
        }
    }

    [RelayCommand]
    private void OpenCurrentInReader() {
        if (Current?.Model.ComicId is int comicId) {
            new ComicReaderWindow(new ComicReaderViewModel(Library, comicId, Current.Model.PageNumber - 1)) { Owner = Owner }.Show();
        }
    }

    [RelayCommand]
    private void ShowComicPages(ComicItemViewModel? comic) {
        if (comic == null) return;
        SelectedQuickFilter = QuickFilters[0];
        SearchText = $"comicid:{comic.Model.Id}";
    }

    [RelayCommand]
    private void RenameComic(ComicItemViewModel? comic) {
        if (comic != null && InputDialog.Show(Owner, "Rename comic", "Title", comic.Title) is { Length: > 0 } title) {
            comic.Model.Title = title;
            Library.UpdateComic(comic.Model);
        }
    }

    [RelayCommand]
    private void UngroupComic(ComicItemViewModel? comic) {
        if (comic != null && Confirm($"Ungroup \"{comic.Title}\"? The page images stay in your library.")) {
            Library.DeleteComic(comic.Model.Id);
        }
    }

    /// <summary>
    /// Puts every page of a comic in a new or existing category and, unless asked to keep it, removes the comic grouping.
    /// </summary>
    [RelayCommand]
    private void ConvertComicToCategory(ComicItemViewModel? comic) {
        if (comic == null || CategoryPickerDialog.Show(Owner, comic.Title, AllCategoryNames) is not CategoryChoice choice) {
            return;
        }

        List<int> pages = [.. Library.GetComicPages(comic.Model.Id).Select(page => page.Id)];
        Library.SetCategory(pages, choice.Category);
        if (choice.KeepComic) {
            comic.Model.Category = choice.Category;
            Library.UpdateComic(comic.Model);
        }
        else {
            Library.DeleteComic(comic.Model.Id);
        }

        Growl.Success($"{pages.Count:N0} pages of \"{comic.Title}\" are now in the \"{choice.Category}\" category.");
    }

    [RelayCommand]
    private void ConvertCurrentComicToCategory() {
        if (Current?.Model.ComicId is int comicId && Comics.FirstOrDefault(comic => comic.Model.Id == comicId) is { } comic) {
            ConvertComicToCategory(comic);
        }
    }

    #endregion

    #region Import, viewer, settings

    [RelayCommand]
    private async Task ImportAsync() {
        ImportWindow dialog = new(Settings, Plugins, Library) { Owner = Owner };
        if (dialog.ShowDialog() == true && dialog.Result is ImportRequest request) {
            await RunImportAsync(request);
        }
    }

    [RelayCommand]
    private async Task RescanAllAsync() {
        List<ScanSource> sources = Library.GetSources();
        if (sources.Count == 0) {
            await ImportAsync();
            return;
        }

        foreach (ScanSource source in sources) {
            await RunImportAsync(new ImportRequest(source.RootPath, source.IgnoredFolders, Settings.DetectComics, Settings.SuggestTagsOnImport, Settings.AutoApplySuggestions));
        }
    }

    /// <summary>
    /// <see cref="IPluginHost.RescanLibraryAsync"/>: the Rescan button without the prompts, skipped while busy.
    /// </summary>
    private async Task<bool> RescanForPluginAsync(bool quiet) {
        if (IsBusy) {
            return false;
        }

        foreach (ScanSource source in Library.GetSources()) {
            if (IsBusy) {
                // Something else started between two folders; the rest wait for the next rescan
                return false;
            }

            if (quiet && !Directory.Exists(source.RootPath)) {
                // An unplugged drive or a disconnected share: don't report the same error on every scheduled rescan
                continue;
            }

            await RunImportAsync(new ImportRequest(source.RootPath, source.IgnoredFolders, Settings.DetectComics, Settings.SuggestTagsOnImport, Settings.AutoApplySuggestions) {
                Quiet = quiet
            });
        }

        return true;
    }

    private async Task RunImportAsync(ImportRequest request) {
        List<LibraryImage> newImages = [];
        await RunJobAsync($"Importing {request.RootPath}", async token => newImages = await ScanSourceAsync(request, token));

        // The import is finished at this point; tagging carries on in the background
        if (request.SuggestTags && newImages.Count > 0) {
            _ = QueueAiTaggingAsync(newImages, request.AutoApply);
        }
    }

    private async Task<List<LibraryImage>> ScanSourceAsync(ImportRequest request, CancellationToken token) {
        Progress<ScanProgress> scanProgress = new(value => ReportJob(
            value.Total > 0 ? $"{value.Phase} {value.Processed:N0} / {value.Total:N0}" : $"{value.Phase}…", value.Processed, value.Total));

        ScanResult result = await new LibraryScanner(Library).ScanAsync(new ScanOptions {
            RootPath = request.RootPath,
            IgnoredFolders = request.IgnoredFolders,
            DetectComics = request.DetectComics
        }, scanProgress, token);
        RefreshAll();

        if (Plugins.ImportProcessors.Count > 0 && result.NewImages.Count > 0) {
            int processed = 0;
            foreach (LibraryImage image in result.NewImages) {
                foreach (IImportProcessor processor in Plugins.ImportProcessors) {
                    try {
                        await processor.ProcessAsync(image, token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException) {
                        Debug.WriteLine($"{processor.Name} failed on {image.FilePath}: {exception.Message}");
                    }
                }

                ReportJob($"Running plugins {++processed:N0} / {result.NewImages.Count:N0}", processed, result.NewImages.Count);
            }
        }

        if (request.Quiet && result.Added + result.Updated + result.Removed + result.ComicsCreated.Count + result.Failed == 0) {
            return result.NewImages;
        }

        Growl.Success($"Import finished: {result.Added:N0} new, {result.Updated:N0} updated, {result.Removed:N0} removed"
                      + (result.ComicsCreated.Count > 0 ? $", {result.ComicsCreated.Count} comics detected" : string.Empty)
                      + (result.Failed > 0 ? $", {result.Failed:N0} unreadable" : string.Empty) + ".");
        return result.NewImages;
    }

    [RelayCommand]
    private void OpenViewer(ImageItemViewModel? item) {
        item ??= Current;
        if (item == null) return;

        if (Settings.UseSystemViewer) {
            if (ShellLauncher.TryOpenWithDefaultApp(item.Model.FilePath, out string? error)) {
                return;
            }

            // Don't leave the user with nothing: fall back to the built-in viewer and explain why
            Growl.Warning($"{error} Showing it in the built-in viewer instead.");
        }

        List<LibraryImage> images = [.. Images.Select(image => image.Model)];
        new ImageViewerWindow(images, Math.Max(0, images.IndexOf(item.Model))) { Owner = Owner }.Show();
    }

    [RelayCommand]
    private void RemoveFromLibrary() {
        if (selection.Count > 0 && Confirm($"Remove {selection.Count:N0} images from the library? Files on disk are not deleted.")) {
            Library.DeleteImages([.. SelectedIds]);
        }
    }

    [RelayCommand]
    private void OpenSettings() {
        SettingsWindow window = new(new SettingsViewModel(Settings, Plugins, Library)) { Owner = Owner };
        window.ShowDialog();
        RefreshAll();
    }

    [RelayCommand]
    private async Task RunImageActionAsync(IImageAction action) {
        List<LibraryImage> images = [.. selection.Select(item => item.Model)];
        try {
            await action.ExecuteAsync(images, CancellationToken.None);
        }
        catch (Exception exception) {
            Growl.Error($"{action.Name} failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private Task RunPluginCommandAsync(IPluginCommand command) => RunJobAsync(command.Name, command.ExecuteAsync);

    [RelayCommand]
    private void CancelJob() => jobCancellation?.Cancel();

    #endregion

    #region Helpers

    private async Task RunJobAsync(string title, Func<CancellationToken, Task> work) {
        if (IsBusy) {
            Growl.Warning("Please wait for the current task to finish (or cancel it).");
            return;
        }

        IsBusy = true;
        JobText = title;
        JobIndeterminate = true;
        JobProgress = 0;
        jobCancellation = new CancellationTokenSource();
        try {
            await work(jobCancellation.Token);
        }
        catch (OperationCanceledException) {
            Growl.Info($"{title} cancelled.");
        }
        catch (Exception exception) {
            Growl.Error($"{title} failed: {exception.Message}");
        }
        finally {
            IsBusy = false;
            jobCancellation.Dispose();
            jobCancellation = null;
            RefreshAll();
        }
    }

    private void ReportJob(string text, int done, int total) {
        JobText = text;
        JobIndeterminate = total <= 0;
        JobProgress = total > 0 ? done * 100.0 / total : 0;
    }

    private bool Confirm(string message) =>
        MessageBox.Show(message, "PixShelf", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    #endregion
}

public record ImportRequest(string RootPath, IReadOnlyCollection<string> IgnoredFolders, bool DetectComics, bool SuggestTags, bool AutoApply) {
    /// <summary>Only report the result when something changed (scheduled rescans).</summary>
    public bool Quiet { get; init; }
}
