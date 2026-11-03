using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using HandyControl.Data;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.ViewModels;
using ImageScanner.Plugins;

namespace ImageScanner.Organizer;

public partial class MainWindow {
    private readonly MainViewModel viewModel;
    private bool restoringSelection;

    public MainWindow(MainViewModel viewModel) {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.RestoreSelectionRequested += RestoreSelection;
        Closing += (_, _) => viewModel.Settings.Save();
    }

    private void RestoreSelection(IReadOnlyCollection<int> ids) {
        restoringSelection = true;
        try {
            Gallery.SelectedItems.Clear();
            foreach (ImageItemViewModel item in viewModel.Images.Where(item => ids.Contains(item.Id))) {
                Gallery.SelectedItems.Add(item);
            }
        }
        finally {
            restoringSelection = false;
        }

        viewModel.SetSelection([.. Gallery.SelectedItems.Cast<ImageItemViewModel>()]);
    }

    private void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!restoringSelection) {
            viewModel.SetSelection([.. Gallery.SelectedItems.Cast<ImageItemViewModel>()]);
        }
    }

    private void Gallery_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ImageItemViewModel item) {
            viewModel.OpenViewerCommand.Execute(item);
        }
    }

    private void Gallery_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter && viewModel.Current != null) {
            viewModel.OpenViewerCommand.Execute(viewModel.Current);
            e.Handled = true;
        }
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ClickCount == 2) {
            viewModel.OpenViewerCommand.Execute(viewModel.Current);
        }
    }

    private void Rating_ValueChanged(object? sender, FunctionEventArgs<double> e) {
        int rating = (int)Math.Round(e.Info);
        if (viewModel.Current != null && viewModel.Current.Rating != rating) {
            viewModel.SetRatingCommand.Execute(rating);
        }
    }

    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e) => SearchBox.Focus();

    #region Menus

    private void Gallery_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
        ContextMenu menu = Gallery.ContextMenu!;
        menu.Items.Clear();
        IReadOnlyList<ImageItemViewModel> selection = viewModel.Selection;
        if (selection.Count == 0) {
            e.Handled = true;
            return;
        }

        menu.Items.Add(Item("Open", () => viewModel.OpenViewerCommand.Execute(viewModel.Current ?? selection[0])));
        menu.Items.Add(Item(selection.All(item => item.IsFavorite) ? "Remove from favorites" : "Add to favorites", viewModel.ToggleFavoriteCommand.Execute));
        menu.Items.Add(new Separator());

        MenuItem category = new() { Header = "Set category" };
        foreach (CategoryCount existing in viewModel.Categories) {
            category.Items.Add(Item(existing.Name, () => viewModel.SetCategoryToCommand.Execute(existing.Name)));
        }
        if (category.Items.Count > 0) category.Items.Add(new Separator());
        category.Items.Add(Item("New category…", () => {
            if (Views.InputDialog.Show(this, "Set category", "Category name") is { Length: > 0 } name) {
                viewModel.SetCategoryToCommand.Execute(name);
            }
        }));
        category.Items.Add(Item("Clear category", () => viewModel.SetCategoryToCommand.Execute(null)));
        menu.Items.Add(category);

        menu.Items.Add(Item("Add tags…", () => {
            if (Views.InputDialog.Show(this, "Add tags", "Tags (comma separated)") is { Length: > 0 } tags) {
                viewModel.NewTagText = tags;
                viewModel.AddTagCommand.Execute(null);
            }
        }));

        menu.Items.Add(Item("Suggest tags with AI", () => viewModel.SuggestTagsCommand.Execute(null)));
        if (selection.Any(item => item.HasPendingSuggestions)) {
            menu.Items.Add(Item("Accept AI suggestions", viewModel.AcceptAllSuggestionsCommand.Execute));
            menu.Items.Add(Item("Dismiss AI suggestions", viewModel.RejectSuggestionsCommand.Execute));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Group as comic…", viewModel.GroupAsComicCommand.Execute));
        if (viewModel.Comics.Count > 0) {
            MenuItem addTo = new() { Header = "Add to comic" };
            foreach (ComicItemViewModel comic in viewModel.Comics.Take(40)) {
                addTo.Items.Add(Item(comic.Title, () => viewModel.AddToComicCommand.Execute(comic)));
            }
            menu.Items.Add(addTo);
        }
        if (selection.Any(item => item.IsComicPage)) {
            if (viewModel.Current?.IsComicPage == true) {
                menu.Items.Add(Item("Open in comic reader", viewModel.OpenCurrentInReaderCommand.Execute));
            }
            menu.Items.Add(Item("Remove from comic", viewModel.RemoveFromComicCommand.Execute));
        }

        List<LibraryImage> models = [.. selection.Select(item => item.Model)];
        List<IImageAction> actions = [.. viewModel.Plugins.ImageActions.Where(action => action.CanExecute(models))];
        if (actions.Count > 0) {
            menu.Items.Add(new Separator());
            foreach (IImageAction action in actions) {
                menu.Items.Add(Item(action.Name, () => viewModel.RunImageActionCommand.Execute(action)));
            }
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove from library", viewModel.RemoveFromLibraryCommand.Execute));
    }

    private void ComicList_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
        ContextMenu menu = ComicList.ContextMenu!;
        menu.Items.Clear();
        if (ComicList.SelectedItem is not ComicItemViewModel comic) {
            e.Handled = true;
            return;
        }

        menu.Items.Add(Item("Read", () => viewModel.OpenComicCommand.Execute(comic)));
        menu.Items.Add(Item("Show pages in gallery", () => viewModel.ShowComicPagesCommand.Execute(comic)));
        menu.Items.Add(Item("Rename…", () => viewModel.RenameComicCommand.Execute(comic)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Ungroup", () => viewModel.UngroupComicCommand.Execute(comic)));
    }

    private void ComicList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ComicItemViewModel comic) {
            viewModel.OpenComicCommand.Execute(comic);
        }
    }

    private void CategoryList_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
        ContextMenu menu = CategoryList.ContextMenu!;
        menu.Items.Clear();
        menu.Items.Add(Item("New category…", viewModel.AddCategoryCommand.Execute));
        if (CategoryList.SelectedItem is CategoryCount category) {
            menu.Items.Add(Item($"Rename \"{category.Name}\"…", () => viewModel.RenameCategoryCommand.Execute(category)));
            menu.Items.Add(Item($"Delete \"{category.Name}\"", () => viewModel.DeleteCategoryCommand.Execute(category)));
        }
    }

    private void TagList_ContextMenuOpening(object sender, ContextMenuEventArgs e) {
        ContextMenu menu = TagList.ContextMenu!;
        menu.Items.Clear();
        if (TagList.SelectedItem is not TagCount tag) {
            e.Handled = true;
            return;
        }

        menu.Items.Add(Item($"Rename or merge \"{tag.Name}\"…", () => viewModel.RenameTagCommand.Execute(tag)));
        menu.Items.Add(Item($"Delete \"{tag.Name}\"", () => viewModel.DeleteTagCommand.Execute(tag)));
        if (viewModel.HasSelection) {
            menu.Items.Add(Item($"Add \"{tag.Name}\" to selected images", () => {
                viewModel.NewTagText = tag.Name;
                viewModel.AddTagCommand.Execute(null);
            }));
        }
    }

    private void ToolsMenu_SubmenuOpened(object sender, RoutedEventArgs e) {
        if (!ReferenceEquals(e.OriginalSource, ToolsMenu)) {
            return;
        }

        ToolsMenu.Items.Clear();
        ToolsMenu.Items.Add(Item("Import folder or drive…", () => viewModel.ImportCommand.Execute(null)));
        ToolsMenu.Items.Add(Item("Rescan all folders", () => viewModel.RescanAllCommand.Execute(null)));
        ToolsMenu.Items.Add(new Separator());
        ToolsMenu.Items.Add(Item("AI: suggest tags for all untagged images", () => viewModel.SuggestTagsForUntaggedCommand.Execute(null)));
        ToolsMenu.Items.Add(Item("AI: accept every pending suggestion", viewModel.AcceptAllPendingCommand.Execute));
        ToolsMenu.Items.Add(Item("Detect comics in library", viewModel.DetectComicsCommand.Execute));

        if (viewModel.Plugins.Commands.Count > 0) {
            ToolsMenu.Items.Add(new Separator());
            foreach (IPluginCommand command in viewModel.Plugins.Commands) {
                ToolsMenu.Items.Add(Item(command.Name, () => viewModel.RunPluginCommandCommand.Execute(command)));
            }
        }

        ToolsMenu.Items.Add(new Separator());
        ToolsMenu.Items.Add(Item("Settings and plugins…", viewModel.OpenSettingsCommand.Execute));
    }

    private static MenuItem Item(string header, Action action) {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Item(string header, Action<object?> action) => Item(header, () => action(null));

    #endregion
}
