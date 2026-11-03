using System.Windows;

using ImageScanner.Organizer.ViewModels;

namespace ImageScanner.Organizer.Views;

public partial class PluginSettingsWindow {
    private readonly PluginSettingsViewModel viewModel;

    public PluginSettingsWindow(PluginSettingsViewModel viewModel) {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        viewModel.Save();
        DialogResult = true;
    }
}
