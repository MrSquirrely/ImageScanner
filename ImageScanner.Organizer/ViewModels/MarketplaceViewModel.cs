using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using HandyControl.Controls;

using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Services;

using MessageBox = HandyControl.Controls.MessageBox;

namespace ImageScanner.Organizer.ViewModels;

/// <summary>A category chip; <see cref="Category"/> is null for "All".</summary>
public record MarketplaceFilter(MarketplaceCategory? Category, int Count) {
    public string Label => Category == null ? $"All ({Count})" : $"{Category.Emoji} {Category.ShortName} ({Count})";
}

public enum MarketplaceInstallState {
    NotInstalled,
    Installed,
    UpdateAvailable,
    RestartPending,
    UninstallPending,
    NotInstallable
}

/// <param name="installed">The loaded plugin this listing matches, if it's installed.</param>
public partial class MarketplaceListingViewModel(MarketplaceListing listing, MarketplaceViewModel owner, PluginInfo? installed) : ObservableObject {
    public MarketplaceListing Listing { get; } = listing;
    public PluginInfo? Installed { get; } = installed;
    public string Name => Listing.Name;
    public string Byline => $"by {Listing.Author}" + (Listing.Version != null ? $" · v{Listing.Version}" : string.Empty);
    public string CategoryLabel => $"{Listing.Category.Emoji} {Listing.Category.ShortName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText), nameof(ActionName), nameof(CanInstall), nameof(CanUninstall), nameof(StateText))]
    public partial MarketplaceInstallState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanUninstall), nameof(StateText))]
    public partial string? Progress { get; set; }

    public bool CanInstall => Progress == null && State is MarketplaceInstallState.NotInstalled or MarketplaceInstallState.UpdateAvailable;

    /// <summary>Downloaded plugins can be removed here too; the tagging plugins ship with PixShelf.</summary>
    public bool CanUninstall => Progress == null && Installed is { IsDownloaded: true }
                                && State is MarketplaceInstallState.Installed or MarketplaceInstallState.UpdateAvailable;

    public string ActionText => State == MarketplaceInstallState.UpdateAvailable ? "Update" : "Install";

    public string ActionName => $"{ActionText} {Name}";

    public string? StateText => Progress ?? State switch {
        MarketplaceInstallState.Installed => Installed is { IsIncluded: true } ? "Included with PixShelf" : "Installed",
        MarketplaceInstallState.RestartPending => "Restart PixShelf to finish",
        MarketplaceInstallState.UninstallPending => "Removed when PixShelf restarts",
        MarketplaceInstallState.NotInstallable => "No valid download in the post",
        _ => null
    };

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => owner.InstallAsync(this);

    [RelayCommand]
    private void Uninstall() {
        if (Installed == null) return;
        owner.Uninstall(Installed);
        if (Installed.IsUninstallPending) {
            State = MarketplaceInstallState.UninstallPending;
        }
    }

    [RelayCommand]
    private void Open() => MarketplaceViewModel.OpenUrl(Listing.DiscussionUrl);

    [RelayCommand]
    private void OpenSource() {
        if (Listing.SourceUrl != null) MarketplaceViewModel.OpenUrl(Listing.SourceUrl);
    }

    partial void OnStateChanged(MarketplaceInstallState value) => InstallCommand.NotifyCanExecuteChanged();

    partial void OnProgressChanged(string? value) => InstallCommand.NotifyCanExecuteChanged();
}

/// <summary>
/// The Marketplace tab: plugin listings from the repository's GitHub Discussions, filtered by category.
/// </summary>
public partial class MarketplaceViewModel(PluginManager plugins, AppSettings settings) : ObservableObject {
    private readonly MarketplaceClient client = new();
    private readonly PluginInstaller installer = new(settings);
    private List<MarketplaceListingViewModel> all = [];
    private bool loaded;

    public ObservableCollection<MarketplaceFilter> Filters { get; } = [];
    public ObservableCollection<MarketplaceListingViewModel> Listings { get; } = [];

    [ObservableProperty]
    public partial MarketplaceFilter? SelectedFilter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Warning { get; set; }

    [ObservableProperty]
    public partial string? LastUpdated { get; set; }

    public bool ShowEmpty => !IsLoading && Listings.Count == 0;

    public string EmptyText => all.Count == 0
                                   ? "No plugins have been shared yet. Be the first: click \"Share a plugin\"."
                                   : "No plugins match this filter.";

    /// <summary>Loads once, when the tab is first shown, so opening Settings doesn't spend an API call.</summary>
    public async Task EnsureLoadedAsync() {
        if (!loaded) {
            loaded = true;
            await LoadAsync(force: false);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(force: true);

    [RelayCommand]
    private static void OpenDiscussions() => OpenUrl(MarketplaceClient.DiscussionsUrl);

    [RelayCommand]
    private static void Share() => OpenUrl(MarketplaceClient.SubmitUrl);

    private async Task LoadAsync(bool force) {
        IsLoading = true;
        try {
            MarketplaceClient.Result result = await client.GetListingsAsync(force, CancellationToken.None);
            Warning = result.Warning;
            LastUpdated = result.FetchedAt == DateTime.MinValue ? null : $"Updated {result.FetchedAt.ToLocalTime():g}";
            all = [.. result.Listings.Select(listing => new MarketplaceListingViewModel(listing, this, InstalledPlugin(listing)) { State = StateOf(listing) })];

            string? selectedSlug = SelectedFilter?.Category?.Slug;
            Filters.Clear();
            Filters.Add(new MarketplaceFilter(null, all.Count));
            foreach (IGrouping<string, MarketplaceListingViewModel> group in all.GroupBy(item => item.Listing.Category.Slug).OrderBy(group => group.First().Listing.Category.Name)) {
                Filters.Add(new MarketplaceFilter(group.First().Listing.Category, group.Count()));
            }

            SelectedFilter = Filters.FirstOrDefault(filter => filter.Category?.Slug == selectedSlug) ?? Filters[0];
            ApplyFilter();
        }
        finally {
            IsLoading = false;
        }
    }

    partial void OnSelectedFilterChanged(MarketplaceFilter? value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter() {
        string text = SearchText.Trim();
        Listings.Clear();
        foreach (MarketplaceListingViewModel item in all.Where(item =>
                     (SelectedFilter?.Category == null || item.Listing.Category.Slug == SelectedFilter.Category.Slug)
                     && (text.Length == 0
                         || item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                         || item.Listing.Summary.Contains(text, StringComparison.OrdinalIgnoreCase)
                         || item.Listing.Author.Contains(text, StringComparison.OrdinalIgnoreCase)))) {
            Listings.Add(item);
        }

        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private PluginInfo? InstalledPlugin(MarketplaceListing listing) =>
        listing.PluginId == null ? null : plugins.Plugins.FirstOrDefault(info => string.Equals(info.Plugin.Id, listing.PluginId, StringComparison.OrdinalIgnoreCase));

    internal void Uninstall(PluginInfo info) => PluginEntryViewModel.UninstallWithConfirmation(info, plugins);

    private MarketplaceInstallState StateOf(MarketplaceListing listing) {
        if (!listing.IsInstallable) {
            return MarketplaceInstallState.NotInstallable;
        }

        if (PluginInstaller.IsPending(listing.PluginId!)) {
            return MarketplaceInstallState.RestartPending;
        }

        PluginInfo? installed = InstalledPlugin(listing);
        if (installed == null) {
            return MarketplaceInstallState.NotInstalled;
        }

        if (installed.IsUninstallPending) {
            return MarketplaceInstallState.UninstallPending;
        }

        return ParseVersion(listing.Version) is { } offered && offered > installed.Plugin.Version
                   ? MarketplaceInstallState.UpdateAvailable
                   : MarketplaceInstallState.Installed;
    }

    internal async Task InstallAsync(MarketplaceListingViewModel item) {
        MarketplaceListing listing = item.Listing;
        string trust = listing.IsTeamPost
                           ? "It was posted by the PixShelf team."
                           : $"It was posted by {listing.Author}, not by the PixShelf team. Plugins run with full access to your PC, so only install ones you trust"
                             + (listing.SourceUrl != null ? $" (source: {listing.SourceUrl})." : ".");
        if (MessageBox.Show($"Install {listing.Name}{(listing.Version != null ? $" {listing.Version}" : string.Empty)}?\n\n{trust}\n\nThe download is checked against the SHA-256 in the post before anything is installed.",
                            item.ActionText + " plugin", System.Windows.MessageBoxButton.OKCancel,
                            listing.IsTeamPost ? System.Windows.MessageBoxImage.Question : System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK) {
            return;
        }

        Progress<string> progress = new(text => item.Progress = text);
        try {
            await installer.InstallAsync(listing, progress, CancellationToken.None);
            item.Progress = null;
            item.State = MarketplaceInstallState.RestartPending;
            Growl.Success($"{listing.Name} is ready. Restart PixShelf to finish installing it.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException) {
            item.Progress = null;
            Growl.Error($"{listing.Name} wasn't installed: {exception.Message}");
        }
    }

    private static Version? ParseVersion(string? text) {
        if (text == null) return null;
        string[] parts = text.Split('-', '+')[0].Split('.');
        return Version.TryParse(parts.Length == 1 ? parts[0] + ".0" : string.Join('.', parts.Take(4)), out Version? version) ? version : null;
    }

    internal static void OpenUrl(string url) {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps) {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
