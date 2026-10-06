using ImageScanner.Core;
using ImageScanner.DatabaseViewer.Models;
using ImageScanner.DatabaseViewer.ViewModels;
using ImageScanner.DatabaseViewer.Models;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ImageScanner.DatabaseViewer.ViewModels;

public class MainViewModel : INotifyPropertyChanged {
    private string scanDirectory = string.Empty;
    private string databaseName = "ImageDatabase";
    private string databasePassword = string.Empty;
    private string statusMessage = "Ready";
    private int scanProgress;
    private int totalFilesToScan;
    private bool isBusy;
    private string searchQuery = string.Empty;

    public ObservableCollection<ImageItemViewModel> Images { get; } = [];

    public string ScanDirectory {
        get => scanDirectory;
        set { scanDirectory = value; OnPropertyChanged(); }
    }

    public string DatabaseName {
        get => databaseName;
        set { databaseName = value; OnPropertyChanged(); }
    }

    public string DatabasePassword {
        get => databasePassword;
        set { databasePassword = value; OnPropertyChanged(); }
    }

    public string StatusMessage {
        get => statusMessage;
        set { statusMessage = value; OnPropertyChanged(); }
    }

    public int ScanProgress {
        get => scanProgress;
        set { scanProgress = value; OnPropertyChanged(); }
    }

    public int TotalFilesToScan {
        get => totalFilesToScan;
        set { totalFilesToScan = value; OnPropertyChanged(); }
    }

    public bool IsBusy {
        get => isBusy;
        set { isBusy = value; OnPropertyChanged(); }
    }

    public string SearchQuery {
        get => searchQuery;
        set {
            searchQuery = value;
            OnPropertyChanged();
            FilterImages();
        }
    }

    private bool isDisclaimerVisible = true;

    public bool IsDisclaimerVisible {
        get => isDisclaimerVisible;
        set {
            isDisclaimerVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAppEnabled));
        }
    }

    public bool IsAppEnabled => !isDisclaimerVisible;

    public ICommand AcknowledgeDisclaimerCommand { get; }

    public ICommand StartScanCommand { get; }
    public ICommand LoadDatabaseCommand { get; }

    public MainViewModel() {
        AcknowledgeDisclaimerCommand = new RelayCommand(() => IsDisclaimerVisible = false);
        StartScanCommand = new RelayCommand(async () => await ExecuteScanAsync(), () => !IsBusy && Directory.Exists(ScanDirectory));
        LoadDatabaseCommand = new RelayCommand(LoadDatabase, () => !IsBusy && !string.IsNullOrWhiteSpace(ScanDirectory));
    }

    private async Task ExecuteScanAsync() {
        IsBusy = true;
        StatusMessage = "Analyzing directory files...";
        ScanProgress = 0;

        string targetDir = ScanDirectory;
        string dbName = DatabaseName;
        string? pwd = string.IsNullOrWhiteSpace(DatabasePassword) ? null : DatabasePassword;

        DirectoryScanner scanner = new(dbName, targetDir, databasePassword: pwd);

        await Task.Run(() => {
            TotalFilesToScan = scanner.CountImageFiles(targetDir);
            if (TotalFilesToScan == 0) {
                StatusMessage = "No supported image assets found.";
                return;
            }

            Progress<int> progressHandler = new(count => {
                ScanProgress = count;
                StatusMessage = $"Scanned {count:N0} of {TotalFilesToScan:N0} assets...";
            });

            scanner.ScanAndSave(progressHandler, generateThumbnails: true);
        });

        StatusMessage = "Scan complete. Loading catalog...";
        LoadDatabase();
        IsBusy = false;
    }

    public void LoadDatabase() {
        string dbFile = Path.Combine(ScanDirectory, DatabaseName);
        string? pwd = string.IsNullOrWhiteSpace(DatabasePassword) ? null : DatabasePassword;

        if (!File.Exists(dbFile) && !File.Exists($"{dbFile}.db")) {
            StatusMessage = "Database file not found in selected directory.";
            return;
        }

        Images.Clear();
        using ImageDatabaseReader reader = new(dbFile, pwd);
        List<ImageRecord> records = reader.GetAllImages();

        foreach (ImageRecord record in records) {
            Images.Add(new ImageItemViewModel(record, reader));
        }

        StatusMessage = $"Loaded {Images.Count:N0} records from database.";
    }

    private void FilterImages() {
        string dbFile = Path.Combine(ScanDirectory, DatabaseName);
        string? pwd = string.IsNullOrWhiteSpace(DatabasePassword) ? null : DatabasePassword;

        if (string.IsNullOrWhiteSpace(SearchQuery)) {
            LoadDatabase();
            return;
        }

        Images.Clear();
        using ImageDatabaseReader reader = new(dbFile, pwd);
        List<ImageRecord> matchedRecords = reader.SearchImages(SearchQuery);

        foreach (ImageRecord record in matchedRecords) {
            Images.Add(new ImageItemViewModel(record, reader));
        }

        StatusMessage = $"Displaying {Images.Count:N0} search results.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}