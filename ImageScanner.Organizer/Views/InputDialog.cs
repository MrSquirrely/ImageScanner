using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImageScanner.Organizer.Views;

/// <summary>
/// Minimal single-line text prompt.
/// </summary>
public static class InputDialog {
    public static string? Show(Window? owner, string title, string prompt, string? initialValue = null) {
        TextBox input = new() { Text = initialValue ?? string.Empty, Margin = new Thickness(0, 8, 0, 0) };
        AutomationPropertiesHelper(input, prompt);
        Button ok = new() { Content = "OK", IsDefault = true, MinWidth = 80, Style = (Style)Application.Current.FindResource("ButtonPrimary") };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        StackPanel content = new() { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(input);
        content.Children.Add(buttons);

        HandyControl.Controls.Window window = new() {
            Title = title,
            Content = content,
            Owner = owner,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false
        };

        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => {
            input.Focus();
            input.SelectAll();
        };
        input.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) window.DialogResult = true;
        };

        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private static void AutomationPropertiesHelper(TextBox input, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(input, name);
}
