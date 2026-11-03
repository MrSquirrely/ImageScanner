using ImageScanner.Organizer.ViewModels;

namespace ImageScanner.Organizer.Views;

public partial class FirstRunWindow {
    public FirstRunWindow(FirstRunViewModel viewModel) {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += result => DialogResult = result;
    }
}
