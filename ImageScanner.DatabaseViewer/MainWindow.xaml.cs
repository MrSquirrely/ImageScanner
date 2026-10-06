using System.Windows;

using ImageScanner.DatabaseViewer.ViewModels;

using Microsoft.Win32;

namespace ImageScanner.DatabaseViewer;

public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();
	}

	private void BrowseFolder_Click(object sender, RoutedEventArgs e) {
		OpenFolderDialog dialog = new() {
			Title = "Select Image Directory to Scan"
		};

		if (dialog.ShowDialog() == true && DataContext is MainViewModel viewModel) {
			viewModel.ScanDirectory = dialog.FolderName;
		}
	}

	private void DbPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) {
		if (DataContext is MainViewModel viewModel) {
			viewModel.DatabasePassword = DbPasswordBox.Password;
		}
	}
}