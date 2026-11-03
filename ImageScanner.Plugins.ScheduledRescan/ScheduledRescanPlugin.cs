using System.Runtime.CompilerServices;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.ScheduledRescan;

/// <summary>
/// Rescans the imported folders on a schedule, so new, changed and deleted images show up without pressing Rescan.
/// </summary>
public sealed class ScheduledRescanPlugin : IPlugin, IConfigurablePlugin {
    internal const string IntervalSetting = "Interval";
    internal const string NotifySetting = "NotifyEveryTime";

    // How often the schedule is checked, and how long after PixShelf opens the first check waits
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    internal static readonly IReadOnlyList<(string Label, TimeSpan Interval)> Intervals = [
        ("Every 15 minutes", TimeSpan.FromMinutes(15)),
        ("Every 30 minutes", TimeSpan.FromMinutes(30)),
        ("Every hour", TimeSpan.FromHours(1)),
        ("Every 3 hours", TimeSpan.FromHours(3)),
        ("Every 6 hours", TimeSpan.FromHours(6)),
        ("Every 12 hours", TimeSpan.FromHours(12)),
        ("Once a day", TimeSpan.FromDays(1))
    ];

    private IPluginHost? host;
    private CancellationTokenSource? stopping;

    // When this plugin last rescanned. Folders that can't be scanned (an unplugged drive) keep an old
    // "last scanned" time, and without this the others would be rescanned every minute.
    private DateTime lastRescanUtc = DateTime.MinValue;

    public string Id => "pixshelf.scheduled-rescan";
    public string Name => "Scheduled rescan";
    public string Description => "Rescans your imported folders on a schedule, so new, changed and deleted images show up without pressing Rescan.";
    public string Author => "PixShelf";
    public Version Version => new(1, 0);
    public string Icon => "";

    public IReadOnlyList<PluginSetting> Settings { get; } = [
        new(IntervalSetting, "Rescan", PluginSettingKind.Choice, "Every hour",
            "How often the imported folders are rescanned. Counted from the last scan, including ones you start with the Rescan button. "
            + "A rescan only reads files that are new or changed.", Choices: [.. Intervals.Select(interval => interval.Label)]),
        new(NotifySetting, "Notify after every rescan", PluginSettingKind.Toggle, "false",
            "Off: a notification only appears when the rescan found new, changed or deleted images.")
    ];

    public void Initialize(IPluginHost pluginHost) {
        host = pluginHost;
        stopping = new CancellationTokenSource();
        _ = RunScheduleAsync(stopping.Token);
    }

    public void Shutdown() {
        stopping?.Cancel();
        stopping?.Dispose();
        stopping = null;
    }

    internal TimeSpan Interval {
        get {
            string? label = host?.GetSetting(this, IntervalSetting)?.Trim();
            return Intervals.FirstOrDefault(interval => interval.Label == label).Interval is { Ticks: > 0 } chosen ? chosen : TimeSpan.FromHours(1);
        }
    }

    private bool NotifyEveryTime => bool.TryParse(host?.GetSetting(this, NotifySetting), out bool notify) && notify;

    private async Task RunScheduleAsync(CancellationToken cancellationToken) {
        try {
            await Task.Delay(StartupDelay, cancellationToken);
            using PeriodicTimer timer = new(CheckEvery);
            do {
                if (IsDue(DateTime.UtcNow) && await TryRescanAsync()) {
                    lastRescanUtc = DateTime.UtcNow;
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) {
            // PixShelf is closing or the plugin was disabled
        }
    }

    /// <summary>
    /// Due once the interval has passed since the oldest folder's last scan and since this plugin's last rescan.
    /// </summary>
    internal bool IsDue(DateTime nowUtc) {
        if (host == null) {
            return false;
        }

        List<ScanSource> sources = host.Library.GetSources();
        if (sources.Count == 0) {
            return false;
        }

        // LiteDB returns dates in local time
        DateTime oldestScanUtc = sources.Min(source => source.LastScanned.ToUniversalTime());
        DateTime lastUtc = oldestScanUtc > lastRescanUtc ? oldestScanUtc : lastRescanUtc;
        return nowUtc - lastUtc >= Interval;
    }

    private async Task<bool> TryRescanAsync() {
        try {
            // False while PixShelf is busy with something else: the next check tries again
            return await Rescan(!NotifyEveryTime);
        }
        catch (MissingMethodException) {
            host?.Notify("Scheduled rescan needs a newer version of PixShelf. Update PixShelf, or disable this plugin.", NotificationKind.Warning);
            stopping?.Cancel();
            return false;
        }
    }

    // Kept separate so that on an older PixShelf without RescanLibraryAsync the MissingMethodException is thrown
    // when this method is compiled, inside TryRescanAsync's try block
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<bool> Rescan(bool quiet) => host!.RescanLibraryAsync(quiet);
}
