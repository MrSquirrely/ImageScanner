using System.Windows;
using System.Windows.Threading;

using HandyControl.Controls;

using ImageScanner.Core.Library;
using ImageScanner.Organizer.Plugins;
using ImageScanner.Organizer.Plugins.BuiltIn;
using ImageScanner.Organizer.Services;
using ImageScanner.Organizer.ViewModels;
using ImageScanner.Organizer.Views;

namespace ImageScanner.Organizer;

public partial class App : Application {
    private ImageLibrary? library;
    private PluginManager? plugins;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // HandyControl defaults to Chinese for built-in text (MessageBox buttons etc.)
        HandyControl.Tools.ConfigHelper.Instance.SetLang("en");

        AppSettings settings = AppSettings.Load();
        ApplyTheme(settings.DarkTheme);

        // Discover plugins before the wizard so it can offer to turn them on or off
        plugins = new PluginManager(settings);
        plugins.Discover([new BuiltInPlugin()]);

        bool importAfterSetup = false;
        if (!settings.FirstRunCompleted || e.Args.Contains("--first-run")) {
            // Keep the app alive while the wizard (the only window so far) closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            FirstRunViewModel wizard = new(settings, plugins);
            new FirstRunWindow(wizard).ShowDialog();
            importAfterSetup = wizard.StartImport && settings.FirstRunCompleted;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        // "--library <path>" opens a different catalog for this session without changing the saved setting
        int libraryArg = Array.IndexOf(e.Args, "--library");
        string libraryPath = libraryArg >= 0 && libraryArg + 1 < e.Args.Length ? e.Args[libraryArg + 1] : settings.LibraryPath;

        try {
            library = new ImageLibrary(libraryPath);
        }
        catch (Exception exception) {
            HandyControl.Controls.MessageBox.Show($"Could not open the library at {libraryPath}:\n{exception.Message}", "Image Organizer",
                                                  MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        plugins.Library = library;
        plugins.InitializeEnabled();

        MainViewModel viewModel = new(library, plugins, settings);
        MainWindow window = new(viewModel);
        MainWindow = window;
        window.Show();

        if (importAfterSetup) {
            viewModel.ImportCommand.Execute(null);
        }
    }

    protected override void OnExit(ExitEventArgs e) {
        plugins?.ShutdownAll();
        library?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Swaps HandyControl's skin dictionary (light/dark). Theme.xaml uses DynamicResource so the UI updates live.
    /// </summary>
    public static void ApplyTheme(bool dark) {
        ResourceDictionary skin = new() {
            Source = new Uri($"pack://application:,,,/HandyControl;component/Themes/{(dark ? "SkinDark" : "SkinDefault")}.xaml")
        };

        Current.Resources.MergedDictionaries[0] = skin;
        Current.Resources.MergedDictionaries[1] = new ResourceDictionary {
            Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml")
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        Growl.Error($"Unexpected error: {e.Exception.Message}");
        e.Handled = true;
    }
}
