using System.Windows;

using ImageScanner.Organizer.ViewModels;

namespace ImageScanner.Organizer.Views;

public partial class SettingsWindow {
    public SettingsWindow(SettingsViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;

        // Save sets DialogResult before closing; any other way out (Cancel, Esc, X) discards
        Closed += (_, _) => {
            if (DialogResult != true) {
                viewModel.DiscardChanges();
            }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
