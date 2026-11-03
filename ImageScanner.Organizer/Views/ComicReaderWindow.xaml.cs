using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ImageScanner.Organizer.ViewModels;

namespace ImageScanner.Organizer.Views;

public partial class ComicReaderWindow {
    private readonly ComicReaderViewModel viewModel;

    public ComicReaderWindow(ComicReaderViewModel viewModel) {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// Click the right half of the page to go forward, the left half to go back.
    /// </summary>
    private void Page_Click(object sender, MouseButtonEventArgs e) {
        bool rightHalf = e.GetPosition(PageScroller).X > PageScroller.ActualWidth / 2;
        if (rightHalf ^ viewModel.RightToLeft) {
            viewModel.NextCommand.Execute(null);
        }
        else {
            viewModel.PreviousCommand.Execute(null);
        }

        PageScroller.ScrollToTop();
    }

    private void FitChanged(object sender, RoutedEventArgs e) {
        // Fit width lets tall pages scroll vertically instead of shrinking to the window height
        PageScroller.VerticalScrollBarVisibility = viewModel.FitWidth ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e) => PageScroller.ScrollToTop();
}
