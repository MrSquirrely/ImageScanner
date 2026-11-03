using System.Windows;

using ImageScanner.Organizer.ViewModels;

namespace ImageScanner.Organizer.Views;

public partial class SettingsWindow {
    public SettingsWindow(SettingsViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
