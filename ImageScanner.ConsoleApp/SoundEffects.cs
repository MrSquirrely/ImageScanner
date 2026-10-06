
namespace ImageScanner.ConsoleApp;

/// <summary>
/// Provides asynchronous, non-blocking retro sound effects using the Windows Console beep API.
/// </summary>
public static class SoundEffects {

    /// <summary>
    /// Plays an ascending 4-note chord (C5-E5-G5-C6) to indicate the application has launched.
    /// </summary>
    public static void PlayStartup() => Task.Run(() => {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        Console.Beep(523, 100);
        Console.Beep(659, 100);
        Console.Beep(784, 100);
        Console.Beep(1046, 250);
    });

    /// <summary>
    /// Plays a short, high-pitched chime (C6) to confirm a menu selection or interaction.
    /// </summary>
    public static void PlaySelect() => Task.Run(() => {
        if (OperatingSystem.IsWindows()) Console.Beep(1046, 100);
    });

    /// <summary>
    /// Plays a very short, subtle tick (800Hz) to provide audio feedback when navigating UI elements.
    /// </summary>
    public static void PlayMove() => Task.Run(() => {
        if (OperatingSystem.IsWindows()) Console.Beep(800, 30);
    });

    /// <summary>
    /// Plays a 2-note ascending success chime (G5-C6) to indicate an operation has finished.
    /// </summary>
    public static void PlayComplete() => Task.Run(() => {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        Console.Beep(784, 150); Console.Beep(1046, 350);
    });

    /// <summary>
    /// Plays a low-pitched, long buzzer (300Hz) to indicate an error or cancellation.
    /// </summary>
    public static void PlayError() => Task.Run(() => {
        if (OperatingSystem.IsWindows()) Console.Beep(300, 400);
    });

    /// <summary>
    /// Plays a descending 3-note chord (C6-G5-C5) to indicate the application is shutting down.
    /// </summary>
    public static void PlayExit() => Task.Run(() => {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        Console.Beep(1046, 150);
        Console.Beep(784, 150);
        Console.Beep(523, 300);
    });
}