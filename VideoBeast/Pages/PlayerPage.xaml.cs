using System;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace VideoBeast.Pages
{
    public sealed partial class PlayerPage : Page
    {
        private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

        // Own the MediaPlayer (avoids Player.MediaPlayer COM/thread issues)
        private readonly MediaPlayer _mp = new();

        private bool _isActive;
        private bool _isUserSeeking;
        private bool _internalSeekUpdate;
        private bool _pointerOverControls;

        private TimeSpan _lastDuration = TimeSpan.Zero;
        private PlayerSettings _settings = new();

        // We no longer show these in the PlayerPage UI, but keep them if you want later
        private string _currentFolderPath = "";
        private string _nowPlayingName = "";

        public PlayerPage()
        {
            InitializeComponent();

            ShowEmptyState(true);

            // Attach owned player to the element
            Player.SetMediaPlayer(_mp);

            // MediaPlayer events can arrive off UI thread
            _mp.MediaOpened += Mp_MediaOpened;
            _mp.MediaEnded += Mp_MediaEnded;
            _mp.CurrentStateChanged += Mp_CurrentStateChanged;

            // UI timers run on UI thread
            _uiTimer.Tick += (_,__) => SyncUiFromPlayer();
            _hideTimer.Tick += (_,__) => HideControlsIfAllowed();

            Loaded += (_,__) =>
            {
                _isActive = true;

                // Ensure we can receive Esc
                IsTabStop = true;
                Focus(FocusState.Programmatic);

                _uiTimer.Start();
                SetControlsVisible(true);

                // Apply fullscreen state if setting says so
                ApplyFullScreen(_settings.IsFullWindow,useTransitions: false);

                RestartHideTimer();
            };

            Unloaded += (_,__) =>
            {
                _isActive = false;

                _hideTimer.Stop();
                _uiTimer.Stop();

                // Always leave fullscreen when leaving page
                try
                {
                    if (MainWindow.Instance is not null)
                        MainWindow.Instance.SetPlayerFullscreen(false);
                }
                catch { }

                try { _mp.Pause(); } catch { }
                _mp.Source = null;

                // Detach events (avoid leaks)
                _mp.MediaOpened -= Mp_MediaOpened;
                _mp.MediaEnded -= Mp_MediaEnded;
                _mp.CurrentStateChanged -= Mp_CurrentStateChanged;
            };
        }

        // Back-compat helper so older calls compile
        public void SetStretch(Stretch stretch) => Player.Stretch = stretch;

        // This used to update CurrentFolderText.Text; you removed it from XAML.
        // Keep method so MainWindow calls don’t break.
        public void SetCurrentFolderText(string text)
        {
            _currentFolderPath = text ?? "";
            // no UI element anymore (intentionally)
        }

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

        private void RunOnUI(Action action)
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                action();
                return;
            }

            _ = DispatcherQueue.TryEnqueue(() => action());
        }

        private void ApplyFullScreen(bool on,bool useTransitions)
        {
            // 1) Make the page layout fill the window (requires your XAML VisualStates)
            VisualStateManager.GoToState(this,on ? "PlayerFullWindow" : "PlayerNormal",useTransitions);

            // 2) YouTube-style fullscreen (hide app chrome) controlled by MainWindow
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

            PositionText.Text = "0:00";
            DurationText.Text = "0:00";
            SeekSlider.Minimum = 0;
            SeekSlider.Maximum = 1;
            SeekSlider.Value = 0;

            ShowEmptyState(true);
        }

        public void ApplySettings(PlayerSettings s)
        {
            _settings = s ?? new PlayerSettings();

            // Element-only settings
            Player.Stretch = _settings.Stretch;

            // Letterbox background
            PlayerHost.Background =
                new SolidColorBrush(ParseHexColor(_settings.LetterboxColorHex,Colors.Black));

            // MediaPlayer settings
            _mp.Volume = Clamp01(_settings.Volume);
            _mp.IsMuted = _settings.IsMuted;
            _mp.IsLoopingEnabled = _settings.IsLoopingEnabled;
            TrySetPlaybackRate(_settings.PlaybackRate);

            // UI reflect
            VolumeSlider.Value = Clamp01(_settings.Volume);
            MuteToggle.IsChecked = _settings.IsMuted;
            LoopToggle.IsChecked = _settings.IsLoopingEnabled;
            FullWindowToggle.IsChecked = _settings.IsFullWindow;

            UpdateMuteIcon();
            UpdatePlayPauseUi();
            SelectRateCombo(_settings.PlaybackRate);

            // Only apply fullscreen if page is active (prevents weirdness during early init)
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
            _mp.Source = MediaSource.CreateFromStorageFile(file);

            SetControlsVisible(true);
            RestartHideTimer();

            if (_settings.AutoPlay)
            {
                try { _mp.Play(); } catch { }
            }
        }

        // ---------------------------
        // Auto-hide behavior
        // ---------------------------
        private void RestartHideTimer()
        {
            _hideTimer.Stop();
            if (ShouldAutoHide())
                _hideTimer.Start();
        }

        private bool ShouldAutoHide()
        {
            if (!_isActive || !IsLoaded) return false;
            if (EmptyState is null || ControlBar is null) return false;
            if (EmptyState.Visibility == Visibility.Visible) return false;
            if (_isUserSeeking) return false;
            if (_pointerOverControls) return false;

            var state = _mp.PlaybackSession?.PlaybackState;
            return state == MediaPlaybackState.Playing;
        }

        private void HideControlsIfAllowed()
        {
            _hideTimer.Stop();
            if (ShouldAutoHide())
                SetControlsVisible(false);
        }

        private void SetControlsVisible(bool visible)
        {
            VisualStateManager.GoToState(this,visible ? "ControlsVisible" : "ControlsHidden",true);
            ControlBar.IsHitTestVisible = visible;
        }

        // ---------------------------
        // MediaPlayer events (dispatch)
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
        // UI -> Player actions
        // ---------------------------
        private void PlayPause_Click(object sender,RoutedEventArgs e)
        {
            try
            {
                if (_mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                    _mp.Pause();
                else
                    _mp.Play();
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
            var on = MuteToggle.IsChecked == true;

            _settings.IsMuted = on;
            _mp.IsMuted = on;

            UpdateMuteIcon();
            PersistSettings();
            SetControlsVisible(true);
            RestartHideTimer();
        }

        private void VolumeSlider_ValueChanged(object sender,RangeBaseValueChangedEventArgs e)
        {
            var v = Clamp01(e.NewValue);

            _settings.Volume = v;
            _mp.Volume = v;

            PersistSettings();
            SetControlsVisible(true);
            RestartHideTimer();
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

        // ---------------------------
        // Seek slider
        // ---------------------------
        private void SeekSlider_PointerPressed(object sender,PointerRoutedEventArgs e)
        {
            _isUserSeeking = true;
            SetControlsVisible(true);
            _hideTimer.Stop();
        }

        private void SeekSlider_PointerReleased(object sender,PointerRoutedEventArgs e)
        {
            _isUserSeeking = false;

            try
            {
                _mp.PlaybackSession.Position = TimeSpan.FromSeconds(SeekSlider.Value);
            }
            catch { }

            SyncUiFromPlayer();
            RestartHideTimer();
        }

        private void SeekSlider_ValueChanged(object sender,RangeBaseValueChangedEventArgs e)
        {
            if (_internalSeekUpdate) return;

            if (_isUserSeeking)
                PositionText.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
        }

        // ---------------------------
        // Sync UI from player state
        // ---------------------------
        private void SyncDurationFromSession()
        {
            var session = _mp.PlaybackSession;
            if (session is null) return;

            var dur = session.NaturalDuration;
            if (dur <= TimeSpan.Zero) dur = TimeSpan.FromSeconds(1);

            if (dur != _lastDuration)
            {
                _lastDuration = dur;
                DurationText.Text = FormatTime(dur);
                SeekSlider.Maximum = Math.Max(1,dur.TotalSeconds);
            }
        }

        private void SyncUiFromPlayer()
        {
            var session = _mp.PlaybackSession;
            if (session is null) return;

            SyncDurationFromSession();

            var pos = session.Position;
            PositionText.Text = FormatTime(pos);

            if (!_isUserSeeking)
            {
                _internalSeekUpdate = true;
                SeekSlider.Value = Math.Min(SeekSlider.Maximum,Math.Max(SeekSlider.Minimum,pos.TotalSeconds));
                _internalSeekUpdate = false;
            }

            // keep UI toggles in sync
            VolumeSlider.Value = Clamp01(_mp.Volume);
            MuteToggle.IsChecked = _mp.IsMuted;
            LoopToggle.IsChecked = _mp.IsLoopingEnabled;

            UpdateMuteIcon();
            UpdatePlayPauseUi();
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

        private void PersistSettings()
        {
            MainWindow.Instance?.SaveAndApplyPlayerSettings(_settings);
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

                // AARRGGBB
                if (hex.Length == 8)
                {
                    byte a = Convert.ToByte(hex.Substring(0,2),16);
                    byte r = Convert.ToByte(hex.Substring(2,2),16);
                    byte g = Convert.ToByte(hex.Substring(4,2),16);
                    byte b = Convert.ToByte(hex.Substring(6,2),16);
                    return Color.FromArgb(a,r,g,b);
                }

                // RRGGBB
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
        // Pointer handlers
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
    }
}
