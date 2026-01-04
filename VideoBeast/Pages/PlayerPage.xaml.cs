using System;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using VideoBeast.Converters;

using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace VideoBeast.Pages;

public sealed partial class PlayerPage : Page
{
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    private readonly MediaPlayer _mp = new();

    private bool _isActive;

    // Seek guards
    private bool _isUserSeeking;
    private bool _internalSeekUpdate;
    private bool _wasPlayingBeforeSeek;

    // Volume guards
    private bool _isUserChangingVolume;
    private bool _internalVolumeUpdate;

    private bool _pointerOverControls;

    private TimeSpan _lastDuration = TimeSpan.Zero;
    private PlayerSettings _settings = new();

    private string _currentFolderPath = "";
    private string _nowPlayingName = "";

    // YouTube-style: slider displays 0 when muted, otherwise remembered volume
    private double DisplayVolume01 => _settings.IsMuted ? 0.0 : Clamp01(_settings.Volume);

    private readonly VolumeThumbToolTipConverter _volumeToolTipConverter = new();

    private readonly SeekThumbToolTipConverter _seekToolTipConverter = new();

    public PlayerPage()
    {
        InitializeComponent();

        ShowEmptyState(true);

        Player.SetMediaPlayer(_mp);

        _mp.MediaOpened += Mp_MediaOpened;
        _mp.MediaEnded += Mp_MediaEnded;
        _mp.CurrentStateChanged += Mp_CurrentStateChanged;

        // Duration can change after MediaOpened
        _mp.PlaybackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;

        HookSliderPointerEvents();

        SeekSlider.IsThumbToolTipEnabled = true;
        SeekSlider.ThumbToolTipValueConverter = _seekToolTipConverter;
   
        VolumeSlider.IsThumbToolTipEnabled = true;
        VolumeSlider.ThumbToolTipValueConverter = _volumeToolTipConverter;

        _uiTimer.Tick += (_,__) => SyncUiFromPlayer();
        _hideTimer.Tick += (_,__) => HideControlsIfAllowed();

        Loaded += (_,__) =>
        {
            _isActive = true;

            IsTabStop = true;
            Focus(FocusState.Programmatic);

            _uiTimer.Start();
            SetControlsVisible(true);

            ApplyFullScreen(_settings.IsFullWindow,useTransitions: false);
            RestartHideTimer();
        };

        Unloaded += (_,__) =>
        {
            _isActive = false;

            _hideTimer.Stop();
            _uiTimer.Stop();

            try { MainWindow.Instance?.SetPlayerFullscreen(false); } catch { }

            try { _mp.Pause(); } catch { }
            _mp.Source = null;

            _mp.MediaOpened -= Mp_MediaOpened;
            _mp.MediaEnded -= Mp_MediaEnded;
            _mp.CurrentStateChanged -= Mp_CurrentStateChanged;

            try { _mp.PlaybackSession.NaturalDurationChanged -= PlaybackSession_NaturalDurationChanged; } catch { }
        };
    }

    private void HookSliderPointerEvents()
    {
        // Seek
        SeekSlider.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(SeekSlider_PointerPressed),true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(SeekSlider_PointerReleased),true);
        SeekSlider.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(SeekSlider_PointerCanceled),true);

        // Volume
        VolumeSlider.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(VolumeSlider_PointerPressed),true);
        VolumeSlider.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(VolumeSlider_PointerReleased),true);
        VolumeSlider.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(VolumeSlider_PointerCanceled),true);
    }

    public void SetStretch(Stretch stretch) => Player.Stretch = stretch;

    public void SetCurrentFolderText(string text) => _currentFolderPath = text ?? "";

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _settings.IsFullWindow)
        {
            _settings.IsFullWindow = false;
            FullWindowToggle.IsChecked = false;

            ApplyFullScreen(false,useTransitions: true);
            PersistSettings();

            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    // FIX: avoid Action -> DispatcherQueueHandler conversion + avoid ambiguous delegate types
    private void RunOnUI(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(
            (Microsoft.UI.Dispatching.DispatcherQueueHandler)(() => action())
        );
    }

    private void ApplyFullScreen(bool on,bool useTransitions)
    {
        VisualStateManager.GoToState(this,on ? "PlayerFullWindow" : "PlayerNormal",useTransitions);
        try { MainWindow.Instance?.SetPlayerFullscreen(on); } catch { }
    }

    public void ShowEmptyState(bool show)
    {
        EmptyState.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (show)
        {
            SetControlsVisible(false);
            _hideTimer.Stop();
        }
        else
        {
            SetControlsVisible(true);
            RestartHideTimer();
        }
    }

    public void StopAndShowEmpty()
    {
        try { _mp.Pause(); } catch { }
        _mp.Source = null;

        _nowPlayingName = "";
        _lastDuration = TimeSpan.Zero;
        _seekToolTipConverter.DurationSeconds = 0; // NEW

        PositionText.Text = "0:00";
        DurationText.Text = "0:00";

        _internalSeekUpdate = true;
        SeekSlider.Value = 0;
        _internalSeekUpdate = false;

        ShowEmptyState(true);
    }

    public void ApplySettings(PlayerSettings s)
    {
        _settings = s ?? new PlayerSettings();

        Player.Stretch = _settings.Stretch;

        PlayerHost.Background =
            new SolidColorBrush(ParseHexColor(_settings.LetterboxColorHex,Colors.Black));

        _settings.Volume = Clamp01(_settings.Volume);

        _mp.Volume = _settings.Volume;
        _mp.IsMuted = _settings.IsMuted;
        _mp.IsLoopingEnabled = _settings.IsLoopingEnabled;

        TrySetPlaybackRate(_settings.PlaybackRate);

        SetVolumeSliderUIFromSettings();
        MuteToggle.IsChecked = _settings.IsMuted;
        LoopToggle.IsChecked = _settings.IsLoopingEnabled;
        FullWindowToggle.IsChecked = _settings.IsFullWindow;

        UpdateMuteIcon();
        UpdatePlayPauseUi();
        SelectRateCombo(_settings.PlaybackRate);

        if (_isActive)
            ApplyFullScreen(_settings.IsFullWindow,useTransitions: false);
        else
            VisualStateManager.GoToState(this,_settings.IsFullWindow ? "PlayerFullWindow" : "PlayerNormal",false);

        RestartHideTimer();
    }

    public void Play(StorageFile file,PlayerSettings settings)
    {
        ApplySettings(settings);
        ShowEmptyState(false);

        if (file is null) return;

        _nowPlayingName = file.DisplayName ?? "";

        // reset duration
        _lastDuration = TimeSpan.Zero;
        _seekToolTipConverter.DurationSeconds = 0; // NEW
        DurationText.Text = "0:00";

        _internalSeekUpdate = true;
        SeekSlider.Value = 0;
        _internalSeekUpdate = false;

        _mp.Source = MediaSource.CreateFromStorageFile(file);

        // fallback metadata duration (helps when NaturalDuration is late)
        _ = TryApplyDurationFromFileAsync(file);

        SetControlsVisible(true);
        RestartHideTimer();

        if (_settings.AutoPlay)
        {
            try { _mp.Play(); } catch { }
        }
    }

    // ---------------------------
    // Duration
    // ---------------------------
    private void PlaybackSession_NaturalDurationChanged(MediaPlaybackSession sender,object args)
    {
        RunOnUI(() =>
        {
            if (!_isActive) return;
            SyncDurationFromSession();
        });
    }

    private async Task TryApplyDurationFromFileAsync(StorageFile file)
    {
        try
        {
            var props = await file.Properties.GetVideoPropertiesAsync();
            var dur = props.Duration;
            if (dur <= TimeSpan.Zero) return;

            RunOnUI(() =>
            {
                if (_lastDuration > TimeSpan.FromSeconds(0.5)) return;

                _lastDuration = dur;
                _seekToolTipConverter.DurationSeconds = _lastDuration.TotalSeconds; // NEW
                DurationText.Text = FormatTime(dur);

                // keep current UI position consistent
                SyncUiFromPlayer();
            });
        }
        catch { }
    }

    private void SyncDurationFromSession()
    {
        var session = _mp.PlaybackSession;
        if (session is null) return;

        var dur = session.NaturalDuration;
        if (dur <= TimeSpan.Zero) return;

        if (dur != _lastDuration)
        {
            _lastDuration = dur;
            _seekToolTipConverter.DurationSeconds = _lastDuration.TotalSeconds; // NEW
            DurationText.Text = FormatTime(dur);
        }
    }

    // ---------------------------
    // Auto-hide
    // ---------------------------
    private void RestartHideTimer()
    {
        _hideTimer.Stop();
        if (ShouldAutoHide()) _hideTimer.Start();
    }

    private bool ShouldAutoHide()
    {
        if (!_isActive || !IsLoaded) return false;
        if (EmptyState is null || ControlBar is null) return false;
        if (EmptyState.Visibility == Visibility.Visible) return false;
        if (_isUserSeeking) return false;
        if (_isUserChangingVolume) return false;
        if (_pointerOverControls) return false;

        return _mp.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
    }

    private void HideControlsIfAllowed()
    {
        _hideTimer.Stop();
        if (ShouldAutoHide()) SetControlsVisible(false);
    }

    private void SetControlsVisible(bool visible)
    {
        VisualStateManager.GoToState(this,visible ? "ControlsVisible" : "ControlsHidden",true);
        ControlBar.IsHitTestVisible = visible;
    }

    // ---------------------------
    // MediaPlayer events
    // ---------------------------
    private void Mp_MediaOpened(MediaPlayer sender,object args)
    {
        RunOnUI(() =>
        {
            if (!_isActive) return;
            SyncDurationFromSession();
            SyncUiFromPlayer();
            SetControlsVisible(true);
            RestartHideTimer();
        });
    }

    private void Mp_MediaEnded(MediaPlayer sender,object args)
    {
        RunOnUI(() =>
        {
            if (!_isActive) return;
            SetControlsVisible(true);
            _hideTimer.Stop();
            SyncUiFromPlayer();
        });
    }

    private void Mp_CurrentStateChanged(MediaPlayer sender,object args)
    {
        RunOnUI(() =>
        {
            if (!_isActive) return;
            UpdatePlayPauseUi();

            if (!ShouldAutoHide())
            {
                SetControlsVisible(true);
                _hideTimer.Stop();
            }
            else
            {
                RestartHideTimer();
            }
        });
    }

    // ---------------------------
    // Buttons
    // ---------------------------
    private void PlayPause_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if (_mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _mp.Pause();
            else _mp.Play();
        }
        catch { }

        SetControlsVisible(true);
        RestartHideTimer();
        UpdatePlayPauseUi();
    }

    private void Stop_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            _mp.Pause();
            _mp.PlaybackSession.Position = TimeSpan.Zero;
        }
        catch { }

        SetControlsVisible(true);
        _hideTimer.Stop();
        SyncUiFromPlayer();
    }

    private void LoopToggle_Click(object sender,RoutedEventArgs e)
    {
        var on = LoopToggle.IsChecked == true;
        _settings.IsLoopingEnabled = on;
        _mp.IsLoopingEnabled = on;

        PersistSettings();
        SetControlsVisible(true);
        RestartHideTimer();
    }

    private void MuteToggle_Click(object sender,RoutedEventArgs e)
    {
        var wantMuted = MuteToggle.IsChecked == true;

        _settings.IsMuted = wantMuted;
        _mp.IsMuted = wantMuted;

        // optional: unmute restores to 100% if remembered volume is zero
        if (!wantMuted && _settings.Volume <= 0.0001)
        {
            _settings.Volume = 1.0;
            _mp.Volume = _settings.Volume;
        }

        SetVolumeSliderUIFromSettings();
        UpdateMuteIcon();

        PersistSettings();
        SetControlsVisible(true);
        RestartHideTimer();
    }

    // ---------------------------
    // Volume (smooth, no timer fighting)
    // ---------------------------
    private void VolumeSlider_PointerPressed(object sender,PointerRoutedEventArgs e)
    {
        _isUserChangingVolume = true;
        SetControlsVisible(true);
        _hideTimer.Stop();
    }

    private void VolumeSlider_PointerReleased(object sender,PointerRoutedEventArgs e)
    {
        if (!_isUserChangingVolume) return;
        _isUserChangingVolume = false;

        PersistSettings();
        RestartHideTimer();
    }

    private void VolumeSlider_PointerCanceled(object sender,PointerRoutedEventArgs e)
    {
        if (!_isUserChangingVolume) return;
        _isUserChangingVolume = false;

        PersistSettings();
        RestartHideTimer();
    }

    private void VolumeSlider_ValueChanged(object sender,RangeBaseValueChangedEventArgs e)
    {
        if (_internalVolumeUpdate) return;

        var v01 = Clamp01(e.NewValue / 100.0);

        if (v01 <= 0.0001)
        {
            _settings.IsMuted = true;
            _mp.IsMuted = true;
            // keep remembered volume unchanged
        }
        else
        {
            _settings.Volume = v01;
            _settings.IsMuted = false;

            _mp.Volume = v01;
            _mp.IsMuted = false;
        }

        if (MuteToggle.IsChecked != _settings.IsMuted)
            MuteToggle.IsChecked = _settings.IsMuted;

        UpdateMuteIcon();
        SetControlsVisible(true);
        RestartHideTimer();
    }

    // ---------------------------
    // Seek (normalized 0..1)
    // ---------------------------
    private void SeekSlider_PointerPressed(object sender,PointerRoutedEventArgs e)
    {
        _isUserSeeking = true;
        SetControlsVisible(true);
        _hideTimer.Stop();

        _wasPlayingBeforeSeek = _mp.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
        if (_wasPlayingBeforeSeek)
        {
            try { _mp.Pause(); } catch { }
        }
    }

    private void SeekSlider_PointerReleased(object sender,PointerRoutedEventArgs e) => EndSeekFromSlider();

    private void SeekSlider_PointerCanceled(object sender,PointerRoutedEventArgs e) => EndSeekFromSlider();

    private void SeekSlider_ValueChanged(object sender,RangeBaseValueChangedEventArgs e)
    {
        if (_internalSeekUpdate) return;
        if (!_isUserSeeking) return;

        var dur = _lastDuration;
        if (dur <= TimeSpan.Zero) return;

        var seconds = Clamp(e.NewValue,0,1) * dur.TotalSeconds;
        PositionText.Text = FormatTime(TimeSpan.FromSeconds(seconds));
    }

    private void EndSeekFromSlider()
    {
        if (!_isUserSeeking) return;
        _isUserSeeking = false;

        var dur = _lastDuration;
        if (dur > TimeSpan.Zero)
        {
            try
            {
                var seconds = Clamp(SeekSlider.Value,0,1) * dur.TotalSeconds;
                _mp.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
            }
            catch { }
        }

        if (_wasPlayingBeforeSeek)
        {
            try { _mp.Play(); } catch { }
        }

        _wasPlayingBeforeSeek = false;
        SyncUiFromPlayer();
        RestartHideTimer();
    }

    // ---------------------------
    // Sync UI
    // ---------------------------
    private void SyncUiFromPlayer()
    {
        var session = _mp.PlaybackSession;
        if (session is null) return;

        SyncDurationFromSession();

        var pos = session.Position;
        PositionText.Text = FormatTime(pos);

        // SEEK: update slider as fraction 0..1
        if (!_isUserSeeking)
        {
            var dur = _lastDuration;
            var frac = 0.0;

            if (dur > TimeSpan.Zero && dur.TotalSeconds > 0.1)
                frac = Clamp01(pos.TotalSeconds / dur.TotalSeconds);

            _internalSeekUpdate = true;
            SeekSlider.Value = frac;
            _internalSeekUpdate = false;
        }

        if (!_isUserChangingVolume)
            SetVolumeSliderUIFromSettings();

        LoopToggle.IsChecked = _mp.IsLoopingEnabled;

        if (MuteToggle.IsChecked != _settings.IsMuted)
            MuteToggle.IsChecked = _settings.IsMuted;

        UpdateMuteIcon();
        UpdatePlayPauseUi();
    }

    private void SetVolumeSliderUIFromSettings()
    {
        _internalVolumeUpdate = true;
        VolumeSlider.Value = DisplayVolume01 * 100.0;
        _internalVolumeUpdate = false;
    }

    private void UpdatePlayPauseUi()
    {
        var isPlaying = _mp.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
        PlayPauseIcon.Symbol = isPlaying ? Symbol.Pause : Symbol.Play;
        PlayPauseText.Text = isPlaying ? "Pause" : "Play";
    }

    private void UpdateMuteIcon()
    {
        var muted = MuteToggle.IsChecked == true;
        MuteIcon.Symbol = muted ? Symbol.Mute : Symbol.Volume;
    }

    private void TrySetPlaybackRate(double rate)
    {
        var session = _mp.PlaybackSession;
        if (session is null) return;

        try { session.PlaybackRate = Clamp(rate,0.25,4.0); }
        catch { }
    }

    private void SelectRateCombo(double rate)
    {
        double[] options = { 0.5,1.0,1.25,1.5,2.0 };

        double closest = 1.0;
        double best = double.MaxValue;

        foreach (var o in options)
        {
            var d = Math.Abs(o - rate);
            if (d < best) { best = d; closest = o; }
        }

        foreach (var obj in RateCombo.Items)
        {
            if (obj is ComboBoxItem item
                && item.Tag is string tag
                && double.TryParse(tag,out var v)
                && Math.Abs(v - closest) < 0.0001)
            {
                RateCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void RateCombo_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (RateCombo.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string tag) return;
        if (!double.TryParse(tag,out var rate)) return;

        _settings.PlaybackRate = rate;
        TrySetPlaybackRate(rate);

        PersistSettings();
        SetControlsVisible(true);
        RestartHideTimer();
    }

    private void FullWindowToggle_Click(object sender,RoutedEventArgs e)
    {
        var on = FullWindowToggle.IsChecked == true;

        _settings.IsFullWindow = on;
        ApplyFullScreen(on,useTransitions: true);

        PersistSettings();
        SetControlsVisible(true);
        RestartHideTimer();
    }

    private void PersistSettings()
    {
        MainWindow.Instance?.SavePlayerSettings(_settings);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    private static double Clamp(double v,double min,double max) => v < min ? min : (v > max ? max : v);

    private static string FormatTime(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{t.Minutes}:{t.Seconds:00}";
    }

    private static Color ParseHexColor(string hex,Color fallback)
    {
        try
        {
            hex = (hex ?? "").Trim();
            if (hex.StartsWith("#")) hex = hex[1..];

            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0,2),16);
                byte r = Convert.ToByte(hex.Substring(2,2),16);
                byte g = Convert.ToByte(hex.Substring(4,2),16);
                byte b = Convert.ToByte(hex.Substring(6,2),16);
                return Color.FromArgb(a,r,g,b);
            }

            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0,2),16);
                byte g = Convert.ToByte(hex.Substring(2,2),16);
                byte b = Convert.ToByte(hex.Substring(4,2),16);
                return Color.FromArgb(255,r,g,b);
            }
        }
        catch { }

        return fallback;
    }

    // ---------------------------
    // Pointer handlers (auto-show controls)
    // ---------------------------
    private void PlayerArea_PointerMoved(object sender,PointerRoutedEventArgs e)
    {
        SetControlsVisible(true);
        RestartHideTimer();
    }

    private void PlayerArea_PointerPressed(object sender,PointerRoutedEventArgs e)
    {
        SetControlsVisible(true);
        RestartHideTimer();
    }

    private void PlayerArea_Tapped(object sender,TappedRoutedEventArgs e)
    {
        SetControlsVisible(true);
        RestartHideTimer();
    }

    private void ControlBar_PointerEntered(object sender,PointerRoutedEventArgs e)
    {
        _pointerOverControls = true;
        SetControlsVisible(true);
        _hideTimer.Stop();
    }

    private void ControlBar_PointerExited(object sender,PointerRoutedEventArgs e)
    {
        _pointerOverControls = false;
        RestartHideTimer();
    }

    // ============================================================
    // NEW: Seek tooltip converter (formats 0..1 fraction as mm:ss)
    // ============================================================
    private sealed class SeekThumbToolTipConverter : IValueConverter
    {
        public double DurationSeconds { get; set; }

        public object Convert(object value,Type targetType,object parameter,string language)
        {
            if (value is not double frac)
                return "0:00";

            var dur = DurationSeconds;
            if (dur <= 0.1)
                return "0:00";

            var seconds = Math.Clamp(frac,0,1) * dur;
            var t = TimeSpan.FromSeconds(seconds);

            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
        }

        public object ConvertBack(object value,Type targetType,object parameter,string language)
            => throw new NotSupportedException();
    }
}
