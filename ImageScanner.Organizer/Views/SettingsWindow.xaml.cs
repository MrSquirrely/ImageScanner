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

    // The marketplace asks GitHub for listings only when its tab is first opened; selection changes inside
    // the tabs (e.g. the category chips) bubble up here too, so check the source
    private async void Tabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (ReferenceEquals(e.OriginalSource, Tabs) && MarketplaceTab.IsSelected && DataContext is SettingsViewModel viewModel) {
            await viewModel.Marketplace.EnsureLoadedAsync();
        }
    }
}
