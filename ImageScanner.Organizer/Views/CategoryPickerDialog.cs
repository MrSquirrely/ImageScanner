using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ImageScanner.Organizer.Views;

public record CategoryChoice(string Category, bool KeepComic);

/// <summary>
/// Picks an existing category or names a new one, for turning a comic's pages into a category.
/// </summary>
public static class CategoryPickerDialog {
    public static CategoryChoice? Show(Window? owner, string comicTitle, IEnumerable<string> categories) {
        List<string> existing = [.. categories];
        HandyControl.Controls.ComboBox category = new() {
            IsEditable = true,
            ItemsSource = existing,
            Text = comicTitle,
            Margin = new Thickness(0, 8, 0, 0)
        };
        HandyControl.Controls.InfoElement.SetPlaceholder(category, "Choose a category or type a new name");
        AutomationProperties.SetName(category, "Category");

        TextBlock hint = new() { FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        void UpdateHint() => hint.Text = existing.Contains(category.Text.Trim(), StringComparer.OrdinalIgnoreCase)
                                             ? "The pages join this existing category."
                                             : "A new category with this name will be created.";
        category.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateHint()));
        category.SelectionChanged += (_, _) => category.Dispatcher.BeginInvoke(UpdateHint);
        UpdateHint();

        CheckBox keepComic = new() { Content = "Keep it as a comic too", Margin = new Thickness(0, 12, 0, 0) };

        Button ok = new() { Content = "Convert", IsDefault = true, MinWidth = 80, Style = (Style)Application.Current.FindResource("ButtonPrimary") };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        StackPanel content = new() { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock {
            Text = $"Put every page of \"{comicTitle}\" in a category. Unless you keep it, the comic grouping is removed; the images stay in the library.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(category);
        content.Children.Add(hint);
        content.Children.Add(keepComic);
        content.Children.Add(buttons);

        HandyControl.Controls.Window window = new() {
            Title = "Convert to category",
            Content = content,
            Owner = owner,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false
        };

        ok.Click += (_, _) => {
            if (category.Text.Trim().Length > 0) {
                window.DialogResult = true;
            }
        };
        window.Loaded += (_, _) => category.Focus();

        return window.ShowDialog() == true ? new CategoryChoice(category.Text.Trim(), keepComic.IsChecked == true) : null;
    }
}
