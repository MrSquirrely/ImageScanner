using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ImageScanner.Organizer.Services;

/// <summary>
/// Keeps PixShelf to one running copy per Windows session. A second launch asks the running copy to come to the
/// front and exits: two copies would write the same library and settings files at the same time.
/// </summary>
internal sealed class SingleInstance : IDisposable {
    private const string MutexName = @"Local\PixShelf.SingleInstance";
    private const string ActivateEventName = @"Local\PixShelf.Activate";
    private const int AsfwAny = -1;

    private readonly Mutex mutex;
    private readonly EventWaitHandle activateEvent;
    private RegisteredWaitHandle? registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activateEvent) {
        this.mutex = mutex;
        this.activateEvent = activateEvent;
    }

    /// <summary>
    /// Returns the lock when this is the only copy, or null after telling the running copy to show itself.
    /// </summary>
    public static SingleInstance? TryAcquire() {
        Mutex mutex = new(true, MutexName, out bool createdNew);
        EventWaitHandle activateEvent = new(false, EventResetMode.AutoReset, ActivateEventName);
        if (!createdNew) {
            bool owned = false;
            try {
                // The previous copy crashed or was killed while holding it
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException) {
                owned = true;
            }

            if (!owned) {
                // This process was just started by the user, so it may hand the foreground to the running copy
                AllowSetForegroundWindow(AsfwAny);
                activateEvent.Set();
                activateEvent.Dispose();
                mutex.Dispose();
                return null;
            }
        }

        return new SingleInstance(mutex, activateEvent);
    }

    /// <summary>
    /// Brings this copy's window to the front whenever another launch is attempted.
    /// </summary>
    public void ListenForActivation(Dispatcher dispatcher) {
        registration = ThreadPool.RegisterWaitForSingleObject(activateEvent, (_, _) => dispatcher.BeginInvoke(BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static void BringToFront() {
        Application application = Application.Current;
        // Before the main window exists the setup wizard is the only window
        Window? window = application.MainWindow is { IsVisible: true } main
                             ? main
                             : application.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible);
        if (window == null) {
            return;
        }

        if (window.WindowState == WindowState.Minimized) {
            window.WindowState = WindowState.Normal;
        }

        // If a dialog is open over the window, that's what should come forward
        IntPtr handle = new WindowInteropHelper(window).Handle;
        SetForegroundWindow(GetLastActivePopup(handle));
    }

    public void Dispose() {
        registration?.Unregister(null);
        activateEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr window);
}
