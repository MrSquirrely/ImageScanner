using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Viewer;

public partial class ViewerWindow : Window {
    private const double MinimumZoom = 0.02;
    private const double MaximumZoom = 32;
    private const double ZoomStep = 1.2;

    private readonly ViewerPlugin plugin;
    private readonly DispatcherTimer animationTimer = new();
    private readonly DispatcherTimer positionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer slideshowTimer = new();
    private readonly Stopwatch shownFor = new();

    private IReadOnlyList<LibraryImage> items = [];
    private int index;
    private int loadVersion;
    private CancellationTokenSource? loading;

    private Animation? animation;
    private int frameIndex;
    private bool isVideo;
    private bool playing;
    private bool updatingSeek;

    // Zoom is screen pixels per image pixel; the offset is where the image's top-left corner sits in the viewport
    private double zoom = 1;
    private Point offset;
    private bool fitToWindow = true;
    private Point? dragStart;
    private Point dragOffset;

    private WindowState stateBeforeFullScreen;
    private WindowStyle styleBeforeFullScreen;
    private bool fullScreen;

    public ViewerWindow(ViewerPlugin plugin) {
        InitializeComponent();
        this.plugin = plugin;

        MuteButton.IsChecked = plugin.Flag(ViewerPlugin.MutedSetting, false);
        LoopButton.IsChecked = plugin.Flag(ViewerPlugin.LoopSetting, true);
        VolumeSlider.Value = Math.Clamp(plugin.Number(ViewerPlugin.VolumeSetting, 0.8), 0, 1);
        Player.Volume = VolumeSlider.Value;
        Player.IsMuted = MuteButton.IsChecked == true;
        MuteButton.Content = Player.IsMuted ? "" : "";

        animationTimer.Tick += (_, _) => AdvanceFrame();
        positionTimer.Tick += (_, _) => UpdatePosition();
        slideshowTimer.Tick += (_, _) => SlideshowTick();
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;
    }

    private LibraryImage? Current => ((index >= 0) && (index < items.Count)) ? items[index] : null;

    private bool HasPicture => !isVideo && (Picture.Source is not null);

    /// <summary>Shows <paramref name="list"/>[<paramref name="startIndex"/>]; the list is what Next and Previous walk.</summary>
    public void Open(IReadOnlyList<LibraryImage> list, int startIndex) {
        items = list;
        _ = ShowItemAsync(Math.Clamp(startIndex, 0, Math.Max(0, list.Count - 1)));
    }

    #region Loading

    private async Task ShowItemAsync(int newIndex) {
        StopPlayback();
        index = newIndex;
        int version = ++loadVersion;
        loading?.Cancel();
        loading = new CancellationTokenSource();
        CancellationToken token = loading.Token;

        LibraryImage? item = Current;
        if (item is null) {
            return;
        }

        Title = $"{item.FileName} - Media viewer";
        CounterText.Text = $"{index + 1:N0} / {items.Count:N0}    {item.FileName}"
                           + ((item.Width > 0) ? $"    {item.Width} × {item.Height}" : string.Empty);
        HideMessage();
        shownFor.Restart();

        if (!File.Exists(item.FilePath)) {
            ShowMessage($"{item.FileName} is no longer at {item.Folder}.", offerExternal: false);
            return;
        }

        if (item.IsVideo) {
            ShowVideo(item);
            return;
        }

        try {
            string path = item.FilePath;
            bool animated = await Task.Run(() => AnimationLoader.IsAnimated(path), token);
            if (animated) {
                Animation loaded = await Task.Run(() => AnimationLoader.Load(path, token), token);
                if (version == loadVersion) {
                    ShowAnimation(loaded);
                }
            }
            else {
                BitmapSource bitmap = await Task.Run(() => StillLoader.Load(path), token);
                if (version == loadVersion) {
                    ShowStill(bitmap);
                }
            }
        }
        catch (OperationCanceledException) {
            // Moved on to another item before this one finished loading
        }
        catch (Exception exception) {
            if (version == loadVersion) {
                ShowMessage($"{item.FileName} couldn't be shown: {exception.Message}", offerExternal: true);
            }
        }
    }

    private void ShowStill(BitmapSource bitmap) {
        isVideo = false;
        animation = null;
        SetPicture(bitmap, resetView: true);
        PlaybackPanel.Visibility = Visibility.Collapsed;
        EnableImageTools(zoomTools: true, rotate: true);
        RestartSlideshowTimer();
    }

    private void ShowAnimation(Animation loaded) {
        isVideo = false;
        animation = loaded;
        frameIndex = 0;
        SetPicture(loaded.Frames[0], resetView: true);

        PlaybackPanel.Visibility = Visibility.Visible;
        SoundPanel.Visibility = Visibility.Collapsed;
        updatingSeek = true;
        SeekSlider.Maximum = Math.Max(1, loaded.Frames.Count - 1);
        SeekSlider.Value = 0;
        updatingSeek = false;
        EnableImageTools(zoomTools: true, rotate: false);
        SetPlaying(true);
        UpdatePosition();
        slideshowTimer.Stop();
    }

    private void ShowVideo(LibraryImage item) {
        isVideo = true;
        animation = null;
        Picture.Source = null;
        Picture.Visibility = Visibility.Collapsed;
        Player.Visibility = Visibility.Visible;
        PlaybackPanel.Visibility = Visibility.Visible;
        SoundPanel.Visibility = Visibility.Visible;
        ZoomText.Text = string.Empty;
        EnableImageTools(zoomTools: false, rotate: false);
        slideshowTimer.Stop();

        updatingSeek = true;
        SeekSlider.Maximum = Math.Max(1, item.DurationSeconds ?? 1);
        SeekSlider.Value = 0;
        updatingSeek = false;

        Player.Source = new Uri(item.FilePath);
        Player.Play();
        if (plugin.Flag(ViewerPlugin.AutoplaySetting, true) || (SlideshowButton.IsChecked == true)) {
            SetPlaying(true);
        }
        else {
            // Paused on the first frame
            Player.Pause();
            SetPlaying(false);
        }

        positionTimer.Start();
    }

    private void SetPicture(BitmapSource bitmap, bool resetView) {
        Player.Visibility = Visibility.Collapsed;
        Picture.Visibility = Visibility.Visible;
        Picture.Source = bitmap;
        // Sized in pixels whatever DPI the file claims, so 100% really is one screen pixel per image pixel
        Picture.Width = bitmap.PixelWidth;
        Picture.Height = bitmap.PixelHeight;
        if (resetView) {
            FitToWindow();
        }
        else {
            ApplyView();
        }
    }

    private void EnableImageTools(bool zoomTools, bool rotate) {
        FitButton.IsEnabled = zoomTools;
        ActualSizeButton.IsEnabled = zoomTools;
        RotateButton.IsEnabled = rotate;
    }

    private void StopPlayback() {
        animationTimer.Stop();
        positionTimer.Stop();
        slideshowTimer.Stop();
        animation = null;
        playing = false;
        if (Player.Source is not null) {
            Player.Stop();
            Player.Source = null;
        }
    }

    private void ShowMessage(string text, bool offerExternal) {
        Picture.Source = null;
        Picture.Visibility = Visibility.Collapsed;
        Player.Visibility = Visibility.Collapsed;
        PlaybackPanel.Visibility = Visibility.Collapsed;
        ZoomText.Text = string.Empty;
        EnableImageTools(zoomTools: false, rotate: false);
        MessageText.Text = text;
        OpenExternallyButton.Visibility = offerExternal ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
        RestartSlideshowTimer();
    }

    private void HideMessage() {
        MessagePanel.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Playback

    private void SetPlaying(bool play) {
        playing = play;
        PlayButton.Content = play ? "" : "";
        if (isVideo) {
            if (play) {
                Player.Play();
            }
            else {
                Player.Pause();
            }
        }
        else if (animation is not null) {
            if (play) {
                animationTimer.Interval = animation.Delays[frameIndex];
                animationTimer.Start();
            }
            else {
                animationTimer.Stop();
            }
        }
    }

    private void AdvanceFrame() {
        if (animation is null) {
            animationTimer.Stop();
            return;
        }

        int next = (frameIndex + 1) % animation.Frames.Count;
        // A slideshow lets the animation finish a loop, and stays at least the slideshow time, before moving on
        if ((next == 0) && (SlideshowButton.IsChecked == true) && (shownFor.Elapsed >= SlideshowInterval)) {
            Step(1);
            return;
        }

        ShowFrame(next);
        animationTimer.Interval = animation.Delays[frameIndex];
    }

    private void ShowFrame(int frame) {
        if (animation is null) {
            return;
        }

        frameIndex = Math.Clamp(frame, 0, animation.Frames.Count - 1);
        Picture.Source = animation.Frames[frameIndex];
        UpdatePosition();
    }

    private void UpdatePosition() {
        updatingSeek = true;
        if (isVideo) {
            SeekSlider.Value = Player.Position.TotalSeconds;
            TimeText.Text = Player.NaturalDuration.HasTimeSpan
                                ? $"{Format(Player.Position)} / {Format(Player.NaturalDuration.TimeSpan)}"
                                : Format(Player.Position);
        }
        else if (animation is not null) {
            SeekSlider.Value = frameIndex;
            TimeText.Text = $"{frameIndex + 1} / {animation.Frames.Count}";
        }

        updatingSeek = false;
    }

    private static string Format(TimeSpan time) {
        return (time.TotalHours >= 1) ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e) {
        if (Player.NaturalDuration.HasTimeSpan) {
            updatingSeek = true;
            SeekSlider.Maximum = Math.Max(0.1, Player.NaturalDuration.TimeSpan.TotalSeconds);
            updatingSeek = false;
        }

        UpdatePosition();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) {
        if (SlideshowButton.IsChecked == true) {
            Step(1);
        }
        else if (LoopButton.IsChecked == true) {
            Player.Position = TimeSpan.Zero;
            Player.Play();
        }
        else {
            Player.Pause();
            SetPlaying(false);
        }
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e) {
        StopPlayback();
        ShowMessage($"Windows can't play {Current?.FileName} here ({e.ErrorException.Message.Trim()}). It may need a codec that isn't installed.", offerExternal: true);
    }

    private void Play_Click(object sender, RoutedEventArgs e) {
        TogglePlaying();
    }

    private void TogglePlaying() {
        if (isVideo || (animation is not null)) {
            SetPlaying(!playing);
        }
    }

    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (updatingSeek) {
            return;
        }

        if (isVideo) {
            Player.Position = TimeSpan.FromSeconds(e.NewValue);
            UpdatePosition();
        }
        else if (animation is not null) {
            ShowFrame((int)Math.Round(e.NewValue));
        }
    }

    private void SeekBy(double seconds) {
        if (isVideo) {
            double length = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan.TotalSeconds : double.MaxValue;
            Player.Position = TimeSpan.FromSeconds(Math.Clamp(Player.Position.TotalSeconds + seconds, 0, length));
            UpdatePosition();
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e) {
        Player.IsMuted = MuteButton.IsChecked == true;
        MuteButton.Content = Player.IsMuted ? "" : "";
    }

    private void Loop_Click(object sender, RoutedEventArgs e) {
        plugin.Save(ViewerPlugin.LoopSetting, (LoopButton.IsChecked == true).ToString());
    }

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (Player is not null) {
            Player.Volume = e.NewValue;
        }
    }

    #endregion

    #region Zoom and pan

    private void FitToWindow() {
        fitToWindow = true;
        if (!HasPicture || (Viewport.ActualWidth <= 0) || (Viewport.ActualHeight <= 0)) {
            return;
        }

        // Shrinks big pictures to fit; small ones stay at their own size
        zoom = Math.Min(1, Math.Min(Viewport.ActualWidth / Picture.Width, Viewport.ActualHeight / Picture.Height));
        ApplyView();
    }

    private void ZoomTo(double newZoom, Point anchor) {
        if (!HasPicture) {
            return;
        }

        newZoom = Math.Clamp(newZoom, MinimumZoom, MaximumZoom);
        // Keep the image point under the anchor where it is
        offset = new Point(anchor.X - ((anchor.X - offset.X) * (newZoom / zoom)), anchor.Y - ((anchor.Y - offset.Y) * (newZoom / zoom)));
        zoom = newZoom;
        fitToWindow = false;
        ApplyView();
    }

    private void ApplyView() {
        if (!HasPicture) {
            return;
        }

        double width = Picture.Width * zoom;
        double height = Picture.Height * zoom;
        // Centered while it fits; otherwise it can't be dragged off past its own edges
        offset = new Point(
            (width <= Viewport.ActualWidth) ? ((Viewport.ActualWidth - width) / 2) : Math.Clamp(offset.X, Viewport.ActualWidth - width, 0),
            (height <= Viewport.ActualHeight) ? ((Viewport.ActualHeight - height) / 2) : Math.Clamp(offset.Y, Viewport.ActualHeight - height, 0));

        PictureScale.ScaleX = zoom;
        PictureScale.ScaleY = zoom;
        PictureOffset.X = Math.Round(offset.X);
        PictureOffset.Y = Math.Round(offset.Y);
        // Enlarged pixel art and screenshots stay crisp
        RenderOptions.SetBitmapScalingMode(Picture, (zoom >= 3) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        ZoomText.Text = $"{zoom * 100:0}%";
    }

    private Point ViewportCenter => new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) {
        if (fitToWindow) {
            FitToWindow();
        }
        else {
            ApplyView();
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) {
        ZoomTo(zoom * Math.Pow(ZoomStep, e.Delta / 120.0), e.GetPosition(Viewport));
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (isVideo) {
            if (e.ClickCount == 2) {
                ToggleFullScreen();
            }
            else {
                TogglePlaying();
            }

            return;
        }

        if (e.ClickCount == 2) {
            if (fitToWindow) {
                ZoomTo(1, e.GetPosition(Viewport));
            }
            else {
                FitToWindow();
            }

            return;
        }

        dragStart = e.GetPosition(Viewport);
        dragOffset = offset;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e) {
        if (dragStart is Point start) {
            Point now = e.GetPosition(Viewport);
            offset = new Point(dragOffset.X + (now.X - start.X), dragOffset.Y + (now.Y - start.Y));
            ApplyView();
        }
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        dragStart = null;
        Viewport.ReleaseMouseCapture();
    }

    private void Fit_Click(object sender, RoutedEventArgs e) {
        FitToWindow();
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e) {
        ZoomTo(1, ViewportCenter);
    }

    private void Rotate_Click(object sender, RoutedEventArgs e) {
        Rotate();
    }

    private void Rotate() {
        if (HasPicture && (animation is null) && (Picture.Source is BitmapSource source)) {
            TransformedBitmap turned = new(source, new RotateTransform(90));
            turned.Freeze();
            SetPicture(turned, resetView: true);
        }
    }

    #endregion

    #region Navigation, slideshow and full screen

    private void Step(int direction) {
        if (items.Count > 0) {
            // Wraps around at both ends
            _ = ShowItemAsync(((index + direction) % items.Count + items.Count) % items.Count);
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e) {
        Step(-1);
    }

    private void Next_Click(object sender, RoutedEventArgs e) {
        Step(1);
    }

    private TimeSpan SlideshowInterval => TimeSpan.FromSeconds(Math.Clamp(plugin.Number(ViewerPlugin.SlideshowSetting, 4), 1, 3600));

    private void Slideshow_Click(object sender, RoutedEventArgs e) {
        if (SlideshowButton.IsChecked == true) {
            shownFor.Restart();
            if ((isVideo || (animation is not null)) && !playing) {
                SetPlaying(true);
            }
        }

        RestartSlideshowTimer();
    }

    /// <summary>Still images (and error messages) move on by timer; videos and animations move on when they finish.</summary>
    private void RestartSlideshowTimer() {
        slideshowTimer.Stop();
        if ((SlideshowButton.IsChecked == true) && !isVideo && (animation is null)) {
            slideshowTimer.Interval = SlideshowInterval;
            slideshowTimer.Start();
        }
    }

    private void SlideshowTick() {
        slideshowTimer.Stop();
        if (SlideshowButton.IsChecked == true) {
            Step(1);
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) {
        ToggleFullScreen();
    }

    private void ToggleFullScreen() {
        if (fullScreen) {
            WindowStyle = styleBeforeFullScreen;
            WindowState = stateBeforeFullScreen;
            ResizeMode = ResizeMode.CanResize;
        }
        else {
            stateBeforeFullScreen = WindowState;
            styleBeforeFullScreen = WindowStyle;
            // Normal first: a maximized window that loses its border doesn't cover the taskbar
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }

        fullScreen = !fullScreen;
    }

    private void OpenExternally_Click(object sender, RoutedEventArgs e) {
        if (Current is LibraryImage item) {
            try {
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) {
                MessageText.Text = $"Windows couldn't open {item.FileName}: {exception.Message}";
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key) {
            case Key.Left when shift:
                SeekBy(-5);
                break;
            case Key.Right when shift:
                SeekBy(5);
                break;
            case Key.Left or Key.PageUp or Key.Back:
                Step(-1);
                break;
            case Key.Right or Key.PageDown:
                Step(1);
                break;
            case Key.Home:
                _ = ShowItemAsync(0);
                break;
            case Key.End:
                _ = ShowItemAsync(Math.Max(0, items.Count - 1));
                break;
            case Key.Space:
                TogglePlaying();
                break;
            case Key.OemPlus or Key.Add:
                ZoomTo(zoom * ZoomStep, ViewportCenter);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomTo(zoom / ZoomStep, ViewportCenter);
                break;
            case Key.D0 or Key.NumPad0:
                FitToWindow();
                break;
            case Key.D1 or Key.NumPad1:
                ZoomTo(1, ViewportCenter);
                break;
            case Key.R:
                Rotate();
                break;
            case Key.M:
                MuteButton.IsChecked = MuteButton.IsChecked != true;
                Mute_Click(this, new RoutedEventArgs());
                break;
            case Key.L:
                LoopButton.IsChecked = LoopButton.IsChecked != true;
                Loop_Click(this, new RoutedEventArgs());
                break;
            case Key.S:
                SlideshowButton.IsChecked = SlideshowButton.IsChecked != true;
                Slideshow_Click(this, new RoutedEventArgs());
                break;
            case Key.F or Key.F11:
                ToggleFullScreen();
                break;
            case Key.Escape when fullScreen:
                ToggleFullScreen();
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e) {
        loading?.Cancel();
        StopPlayback();
        Player.Close();
        plugin.Save(ViewerPlugin.MutedSetting, (MuteButton.IsChecked == true).ToString());
        plugin.Save(ViewerPlugin.VolumeSetting, VolumeSlider.Value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    #endregion
}