using System.Collections.ObjectModel;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;

using ImageScanner.Core.Library;

namespace ImageScanner.Organizer.ViewModels;

/// <summary>
/// The folders under an import's root, each with a checkbox. Everything starts ticked; unticking a folder skips it
/// and everything inside it. Subfolders are read when a folder is expanded, so a whole drive opens instantly.
/// </summary>
public sealed partial class FolderTreeViewModel : ObservableObject {
    // The same folders the scanner skips (LibraryScanner.EnumerateImages)
    internal static readonly EnumerationOptions FolderOptions = new() {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden
    };

    private HashSet<string> skippedNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private int loadVersion;

    public ObservableCollection<FolderNode> Roots { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = "Choose a folder or drive to see what's inside it.";

    public string? RootPath => Roots.FirstOrDefault()?.Path;

    /// <summary>True when the root itself is unticked, so there's nothing to import.</summary>
    public bool NothingSelected => Roots.FirstOrDefault()?.IsChecked == false;

    internal bool IsSkippedByName(string name) => skippedNames.Contains(name);

    /// <summary>
    /// Shows <paramref name="root"/>'s folders. <paramref name="excluded"/> are full paths to start unticked
    /// (from an earlier import of the same folder); <paramref name="skipNames"/> are names that are always skipped.
    /// </summary>
    public async Task LoadAsync(string root, IEnumerable<string> skipNames, IEnumerable<string> excluded) {
        int version = ++loadVersion;
        skippedNames = new HashSet<string>(skipNames, StringComparer.OrdinalIgnoreCase);
        excludedPaths = new HashSet<string>(excluded.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        Roots.Clear();

        string path = Normalize(root);
        FolderNode node = new(this, null, path, DisplayName(path), hasSubfolders: true, InitialState(path, inherited: true), skippedByName: false);
        Roots.Add(node);
        await node.ExpandAsync();
        if (version == loadVersion) {
            UpdateSummary();
        }
    }

    public void Clear() {
        loadVersion++;
        Roots.Clear();
        Summary = "Choose a folder or drive to see what's inside it.";
    }

    /// <summary>
    /// The unticked folders as full paths, outermost only (skipping a folder already skips everything inside it).
    /// Includes ones from an earlier import that sit in folders not opened here.
    /// </summary>
    public List<string> ExcludedPaths() {
        List<string> paths = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void Walk(FolderNode node) {
            seen.Add(node.Path);
            if (node is { IsPlaceholder: false, SkippedByName: false, IsChecked: false }) {
                paths.Add(node.Path);
                return;
            }

            foreach (FolderNode child in node.Children) {
                Walk(child);
            }
        }

        foreach (FolderNode root in Roots) {
            Walk(root);
        }

        // Earlier exclusions deeper than anything opened this time still apply, unless an ancestor is now unticked
        foreach (string path in excludedPaths) {
            if (!seen.Contains(path) && RootPath is { } root && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                && !paths.Any(outer => path.StartsWith(outer + "\\", StringComparison.OrdinalIgnoreCase))) {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Ticked unless it was excluded before; partly ticked when something inside it was. <paramref name="inherited"/>
    /// is the parent's state for a parent that's fully ticked or unticked.
    /// </summary>
    internal bool? InitialState(string path, bool? inherited) {
        if (excludedPaths.Contains(path)) {
            return false;
        }

        string prefix = path.TrimEnd('\\') + "\\";
        if (excludedPaths.Any(excluded => excluded.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) {
            return null;
        }

        return inherited ?? true;
    }

    internal void UpdateSummary() {
        if (Roots.FirstOrDefault() is not { } root) {
            return;
        }

        if (root.IsChecked == false) {
            Summary = "Nothing will be imported: tick the folder or some of its subfolders.";
            return;
        }

        int skipped = ExcludedPaths().Count;
        Summary = skipped == 0
                      ? "Every folder will be scanned. Untick the ones you don't want; that skips everything inside them too."
                      : $"{skipped:N0} folder{(skipped == 1 ? " is" : "s are")} skipped, with everything inside {(skipped == 1 ? "it" : "them")}. "
                        + "Images already imported from a skipped folder are removed from the library.";
    }

    private static string Normalize(string path) {
        string full = System.IO.Path.GetFullPath(path);
        // "D:\" stays as it is; other folders lose a trailing slash so they match the scanner's paths
        return full.Length > 3 ? full.TrimEnd('\\', '/') : full;
    }

    private static string DisplayName(string path) =>
        System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
}

/// <summary>A folder in <see cref="FolderTreeViewModel"/>.</summary>
public sealed partial class FolderNode : ObservableObject {
    private readonly FolderTreeViewModel tree;
    private bool? isChecked;
    private bool loaded;
    private Task? loading;

    internal FolderNode(FolderTreeViewModel tree, FolderNode? parent, string path, string name, bool hasSubfolders, bool? isChecked, bool skippedByName) {
        this.tree = tree;
        Parent = parent;
        Path = path;
        Name = name;
        SkippedByName = skippedByName;
        this.isChecked = skippedByName ? false : isChecked;
        if (hasSubfolders) {
            // Gives the folder an expander until its real subfolders are read
            Children.Add(Placeholder(tree, this));
        }
    }

    private FolderNode(FolderTreeViewModel tree, FolderNode parent) {
        this.tree = tree;
        Parent = parent;
        Path = string.Empty;
        Name = "Loading…";
        IsPlaceholder = true;
    }

    private static FolderNode Placeholder(FolderTreeViewModel tree, FolderNode parent) => new(tree, parent);

    public FolderNode? Parent { get; }
    public string Path { get; }
    public string Name { get; }
    public bool IsPlaceholder { get; }

    /// <summary>Matches a name in "Skip folders named", so it's always skipped and can't be ticked here.</summary>
    public bool SkippedByName { get; }

    public bool CanToggle => !SkippedByName && !IsPlaceholder;

    public string? ToolTip => SkippedByName ? "Skipped because of its name. Remove it from \"Skip folders named\" to include it." : null;

    public ObservableCollection<FolderNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value) {
        if (value) {
            _ = ExpandAsync();
        }
    }

    /// <summary>
    /// Ticked (scanned with everything inside), unticked (skipped with everything inside) or partly ticked (scanned,
    /// but some folders inside are skipped). Clicking a partly ticked box ticks it fully.
    /// </summary>
    public bool? IsChecked {
        get => isChecked;
        set {
            if (!CanToggle) {
                OnPropertyChanged();
                return;
            }

            SetState(value ?? true);
            Parent?.UpdateFromChildren();
            tree.UpdateSummary();
        }
    }

    private void SetState(bool value) {
        if (SkippedByName) {
            return;
        }

        isChecked = value;
        OnPropertyChanged(nameof(IsChecked));
        foreach (FolderNode child in Children.Where(child => !child.IsPlaceholder)) {
            child.SetState(value);
        }
    }

    /// <summary>
    /// After a subfolder changed: fully ticked when every subfolder is, otherwise partly ticked. A folder is still
    /// scanned while something inside it is ticked, so it never becomes unticked this way.
    /// </summary>
    private void UpdateFromChildren() {
        List<FolderNode> children = [.. Children.Where(child => child.CanToggle)];
        if (children.Count == 0) {
            return;
        }

        bool? state = children.All(child => child.isChecked == true)
                          ? isChecked == false ? null : true
                          : children.All(child => child.isChecked == false) && isChecked == false ? false : null;
        if (state != isChecked) {
            isChecked = state;
            OnPropertyChanged(nameof(IsChecked));
        }

        Parent?.UpdateFromChildren();
    }

    internal Task ExpandAsync() {
        IsExpanded = true;
        return loading ??= LoadChildrenAsync();
    }

    private async Task LoadChildrenAsync() {
        if (loaded) {
            return;
        }

        loaded = true;
        List<(string Path, string Name, bool HasSubfolders)> folders = await Task.Run(() => ReadFolders(Path));

        Children.Clear();
        bool? inherited = isChecked == null ? null : isChecked;
        foreach ((string path, string name, bool hasSubfolders) in folders) {
            bool skipped = tree.IsSkippedByName(name);
            Children.Add(new FolderNode(tree, this, path, name, hasSubfolders && !skipped, tree.InitialState(path, inherited), skipped));
        }
    }

    private static List<(string Path, string Name, bool HasSubfolders)> ReadFolders(string path) {
        try {
            return [.. Directory.EnumerateDirectories(path, "*", FolderTreeViewModel.FolderOptions)
                                .Select(folder => (folder, System.IO.Path.GetFileName(folder), HasSubfolders(folder)))
                                .OrderBy(folder => folder.Item2, NaturalStringComparer.Instance)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return [];
        }
    }

    private static bool HasSubfolders(string path) {
        try {
            return Directory.EnumerateDirectories(path, "*", FolderTreeViewModel.FolderOptions).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return false;
        }
    }
}
