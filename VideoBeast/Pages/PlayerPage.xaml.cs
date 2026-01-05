using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using VideoBeast.Converters;

using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.System;
using Windows.UI;

namespace VideoBeast.Pages;

public sealed partial class PlayerPage : Page
{
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    // Playlist filter debounce
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private readonly MediaPlayer _mp = new();

    private bool _isActive;

    private bool _isUserSeeking;
    private bool _internalSeekUpdate;
    private bool _wasPlayingBeforeSeek;

    private bool _isUserChangingVolume;
    private bool _internalVolumeUpdate;

    private bool _pointerOverControls;

    private TimeSpan _lastDuration = TimeSpan.Zero;
    private PlayerSettings _settings = new();

    private string _currentFolderPath = "";
    private string _nowPlayingName = "";
    private string _nowPlayingPath = "";

    private double DisplayVolume01 => _settings.IsMuted ? 0.0 : Clamp01(_settings.Volume);

    private readonly VolumeThumbToolTipConverter _volumeToolTipConverter = new();
    private readonly SeekThumbToolTipValueConverter _seekToolTipConverter = new();

    // Playlist state
    private StorageFolder? _playlistFolder;
    private string _playlistFilter = "";
    private Mp4FolderIncrementalSource? _playlistSource;
    private int _ensureNowPlayingRunId;

    // NEW: Allow MainWindow to request folder load before PlayerPage is Loaded
    private StorageFolder? _pendingFolderToLoad;
    private bool _pendingPreserveSelection;

    public PlayerPage()
    {
        InitializeComponent();

        ShowEmptyState(true);

        Player.SetMediaPlayer(_mp);

        _mp.MediaOpened += Mp_MediaOpened;
        _mp.MediaEnded += Mp_MediaEnded;
        _mp.CurrentStateChanged += Mp_CurrentStateChanged;

        _mp.PlaybackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;

        HookSliderPointerEvents();

        SeekSlider.IsThumbToolTipEnabled = true;
        SeekSlider.ThumbToolTipValueConverter = _seekToolTipConverter;

        VolumeSlider.IsThumbToolTipEnabled = true;
        VolumeSlider.ThumbToolTipValueConverter = _volumeToolTipConverter;

        _uiTimer.Tick += (_,__) => SyncUiFromPlayer();
        _hideTimer.Tick += (_,__) => HideControlsIfAllowed();

        _filterDebounce.Tick += async (_,__) =>
        {
            _filterDebounce.Stop();
            await RefreshPlaylistAsync(preserveSelection: true);
        };

        Loaded += async (_,__) =>
        {
            _isActive = true;

            IsTabStop = true;
            Focus(FocusState.Programmatic);

            _uiTimer.Start();
            SetControlsVisible(true);

            ApplyFullScreen(_settings.IsFullWindow,useTransitions: false);
            RestartHideTimer();

            // NEW: If MainWindow called LoadFolderAsync before we loaded, honor it now.
            if (_pendingFolderToLoad is not null)
            {
                var folder = _pendingFolderToLoad;
                var preserve = _pendingPreserveSelection;
                _pendingFolderToLoad = null;

                await LoadPlaylistFromFolderAsync(folder,preserveSelection: preserve);
                ShowEmptyState(_mp.Source is null);
                return;
            }

            // Existing behavior: if only a path was set before load, hydrate playlist from path.
            if (!string.IsNullOrWhiteSpace(_currentFolderPath))
                await LoadPlaylistFromPathAsync(_currentFolderPath,preserveSelection: false);
        };

        Unloaded += (_,__) =>
        {
            _isActive = false;

            _filterDebounce.Stop();
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
        SeekSlider.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(SeekSlider_PointerPressed),true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(SeekSlider_PointerReleased),true);
        SeekSlider.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(SeekSlider_PointerCanceled),true);

        VolumeSlider.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(VolumeSlider_PointerPressed),true);
        VolumeSlider.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(VolumeSlider_PointerReleased),true);
        VolumeSlider.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(VolumeSlider_PointerCanceled),true);
    }

    public void SetStretch(Stretch stretch) => Player.Stretch = stretch;

    /// <summary>
    /// Called by MainWindow when a folder is selected in the left nav.
    /// This also drives the playlist contents on the right.
    /// </summary>
    public void SetCurrentFolderText(string text)
    {
        _currentFolderPath = text ?? "";

        // Fire and forget (safe even if called frequently).
        _ = LoadPlaylistFromPathAsync(_currentFolderPath,preserveSelection: false);
    }

    // ----------------------------------------------------
    // NEW: MainWindow expects these methods to exist
    // ----------------------------------------------------
    public Task LoadFolderAsync(StorageFolder folder)
        => LoadFolderAsync(folder,preserveSelection: false);

    public async Task LoadFolderAsync(StorageFolder folder,bool preserveSelection)
    {
        if (folder is null) return;

        _currentFolderPath = folder.Path ?? _currentFolderPath;

        // If page isn't ready yet, remember the request for Loaded handler.
        if (!_isActive || !IsLoaded)
        {
            _pendingFolderToLoad = folder;
            _pendingPreserveSelection = preserveSelection;
            return;
        }

        await LoadPlaylistFromFolderAsync(folder,preserveSelection);
        ShowEmptyState(_mp.Source is null);
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
        _nowPlayingPath = "";
        _lastDuration = TimeSpan.Zero;
        _seekToolTipConverter.DurationSeconds = 0;

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
        _nowPlayingPath = file.Path ?? "";

        _lastDuration = TimeSpan.Zero;
        _seekToolTipConverter.DurationSeconds = 0;
        DurationText.Text = "0:00";

        _internalSeekUpdate = true;
        SeekSlider.Value = 0;
        _internalSeekUpdate = false;

        _mp.Source = MediaSource.CreateFromStorageFile(file);

        _ = TryApplyDurationFromFileAsync(file);

        SetControlsVisible(true);
        RestartHideTimer();

        // Now Playing highlight even when playlist hasn't loaded that far yet.
        _ = EnsureNowPlayingSelectedAsync(_nowPlayingPath);

        if (_settings.AutoPlay)
        {
            try { _mp.Play(); } catch { }
        }
    }

    // ---------------------------
    // Playlist
    // ---------------------------

    private async Task LoadPlaylistFromPathAsync(string folderPath,bool preserveSelection)
    {
        if (!_isActive && !IsLoaded)
            return;

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _playlistFolder = null;
            _playlistSource = null;
            RunOnUI(() =>
            {
                FilesList.ItemsSource = null;
                PlaylistTitle.Text = "Videos";
            });
            ShowEmptyState(true);
            return;
        }

        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            await LoadPlaylistFromFolderAsync(folder,preserveSelection);
            ShowEmptyState(_mp.Source is null); // still show empty overlay until something plays
        }
        catch
        {
            // Access/path failed
            _playlistFolder = null;
            _playlistSource = null;
            RunOnUI(() =>
            {
                FilesList.ItemsSource = null;
                PlaylistTitle.Text = "Videos";
            });
        }
    }

    private async Task LoadPlaylistFromFolderAsync(StorageFolder folder,bool preserveSelection)
    {
        _playlistFolder = folder;

        var selectedPath = preserveSelection
            ? (FilesList.SelectedItem as StorageFile)?.Path
            : null;

        RunOnUI(() =>
        {
            PlaylistTitle.Text = folder.Name;
        });

        _playlistSource = new Mp4FolderIncrementalSource(folder,() => _playlistFilter);

        RunOnUI(() =>
        {
            FilesList.ItemsSource = _playlistSource;
        });

        // Preload the first page so the list isn't blank until you scroll.
        try { await _playlistSource.LoadNextPageAsync(); } catch { }

        if (!string.IsNullOrWhiteSpace(_nowPlayingPath))
        {
            // Prefer now-playing selection
            await EnsureNowPlayingSelectedAsync(_nowPlayingPath);
        }
        else if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await EnsureNowPlayingSelectedAsync(selectedPath);
        }
    }

    private async Task RefreshPlaylistAsync(bool preserveSelection)
    {
        if (_playlistFolder is null) return;
        await LoadPlaylistFromFolderAsync(_playlistFolder,preserveSelection);
    }

    private async Task EnsureNowPlayingSelectedAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (_playlistSource is null) return;

        var runId = ++_ensureNowPlayingRunId;

        // If filter is active and excludes the now playing item, we won't find it.
        // We do NOT auto-clear your filter; we just try.
        for (int i = 0; i < 200; i++) // hard cap to avoid infinite loops
        {
            if (runId != _ensureNowPlayingRunId) return;

            var match = _playlistSource.FirstOrDefault(f =>
                string.Equals(f.Path,filePath,StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                RunOnUI(() =>
                {
                    FilesList.SelectedItem = match;
                    FilesList.ScrollIntoView(match);
                });
                return;
            }

            if (!_playlistSource.HasMoreItems) return;

            try { await _playlistSource.LoadNextPageAsync(); }
            catch { return; }
        }
    }

    private void PlaylistToggle_Click(object sender,RoutedEventArgs e)
    {
        var on = PlaylistToggle.IsChecked == true;

        PlaylistPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PlaylistSplitter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        SetControlsVisible(true);
        RestartHideTimer();
    }

    private async void PlaylistRefresh_Click(object sender,RoutedEventArgs e)
    {
        await RefreshPlaylistAsync(preserveSelection: true);
    }

    private void PlaylistFilterBox_TextChanged(object sender,TextChangedEventArgs e)
    {
        _playlistFilter = PlaylistFilterBox.Text ?? "";

        // debounce refresh
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void FilesList_ItemClick(object sender,ItemClickEventArgs e)
    {
        if (e.ClickedItem is not StorageFile file) return;

        var settings = MainWindow.Instance?.GetPlayerSettings() ?? _settings ?? new PlayerSettings();
        Play(file,settings);
    }

    // Context menu helpers
    private StorageFile? GetFileFromMenuSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is StorageFile f1) return f1;
        if (sender is MenuFlyoutItem mfi && mfi.DataContext is StorageFile f2) return f2;
        return null;
    }

    private void SelectPlaylistFile(StorageFile file)
    {
        FilesList.SelectedItem = file;
        FilesList.ScrollIntoView(file);
    }

    private void Playlist_Play_Click(object sender,RoutedEventArgs e)
    {
        var file = GetFileFromMenuSender(sender);
        if (file is null) return;

        SelectPlaylistFile(file);

        var settings = MainWindow.Instance?.GetPlayerSettings() ?? _settings ?? new PlayerSettings();
        Play(file,settings);
    }

    private async void Playlist_ShowInFolder_Click(object sender,RoutedEventArgs e)
    {
        var file = GetFileFromMenuSender(sender);
        if (file is null) return;

        SelectPlaylistFile(file);

        try
        {
            var parent = await file.GetParentAsync();
            if (parent is not null)
            {
                var opts = new FolderLauncherOptions();
                opts.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderAsync(parent,opts);
            }
        }
        catch { }
    }

    private void Playlist_CopyPath_Click(object sender,RoutedEventArgs e)
    {
        var file = GetFileFromMenuSender(sender);
        if (file is null) return;

        SelectPlaylistFile(file);

        try
        {
            var dp = new DataPackage();
            dp.SetText(file.Path ?? "");
            Clipboard.SetContent(dp);
        }
        catch { }
    }

    private async void Playlist_Rename_Click(object sender,RoutedEventArgs e)
    {
        var file = GetFileFromMenuSender(sender);
        if (file is null) return;

        SelectPlaylistFile(file);

        var tb = new TextBox
        {
            Text = file.DisplayName,
            PlaceholderText = "New name (no extension)",
            MinWidth = 320
        };

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Rename video",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = tb
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var newName = (tb.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            // Keep extension
            var ext = file.FileType ?? ".mp4";
            await file.RenameAsync(newName + ext,NameCollisionOption.FailIfExists);

            // If we were playing this file, update now-playing path/name
            if (!string.IsNullOrWhiteSpace(_nowPlayingPath) &&
                string.Equals(_nowPlayingPath,file.Path,StringComparison.OrdinalIgnoreCase))
            {
                _nowPlayingName = file.DisplayName ?? "";
                _nowPlayingPath = file.Path ?? "";
            }

            await RefreshPlaylistAsync(preserveSelection: true);
        }
        catch { }
    }

    private async void Playlist_Delete_Click(object sender,RoutedEventArgs e)
    {
        var file = GetFileFromMenuSender(sender);
        if (file is null) return;

        SelectPlaylistFile(file);

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete video?",
            Content = $"Delete \"{file.Name}\" from disk?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var deletingPath = file.Path ?? "";

            // Stop playback if deleting the currently playing file
            if (!string.IsNullOrWhiteSpace(deletingPath) &&
                string.Equals(deletingPath,_nowPlayingPath,StringComparison.OrdinalIgnoreCase))
            {
                StopAndShowEmpty();
            }

            await file.DeleteAsync(StorageDeleteOption.Default);

            await RefreshPlaylistAsync(preserveSelection: false);
        }
        catch { }
    }

    // ---------------------------
    // Media events / UI sync
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
                _seekToolTipConverter.DurationSeconds = _lastDuration.TotalSeconds;
                DurationText.Text = FormatTime(dur);

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
            _seekToolTipConverter.DurationSeconds = _lastDuration.TotalSeconds;
            DurationText.Text = FormatTime(dur);
        }
    }

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

    private void TogglePlayPause()
    {
        if (_mp.Source is null) return;

        try
        {
            var state = _mp.PlaybackSession?.PlaybackState;
            if (state == MediaPlaybackState.Playing) _mp.Pause();
            else _mp.Play();
        }
        catch { }

        SetControlsVisible(true);
        RestartHideTimer();
        UpdatePlayPauseUi();
    }

    private void Player_Tapped(object sender,TappedRoutedEventArgs e)
    {
        if (EmptyState.Visibility == Visibility.Visible) return;
        TogglePlayPause();
        e.Handled = true;
    }

    private void PlayPause_Click(object sender,RoutedEventArgs e) => TogglePlayPause();

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

    private void SyncUiFromPlayer()
    {
        var session = _mp.PlaybackSession;
        if (session is null) return;

        SyncDurationFromSession();

        var pos = session.Position;
        PositionText.Text = FormatTime(pos);

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

    // ---------------------------
    // Private helper types
    // ---------------------------

    private sealed class SeekThumbToolTipValueConverter : IValueConverter
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

    /// <summary>
    /// Incremental-loading source for MP4 files in a folder.
    /// ListView will call LoadMoreItemsAsync as you scroll.
    /// </summary>
    private sealed class Mp4FolderIncrementalSource : ObservableCollection<StorageFile>, ISupportIncrementalLoading
    {
        private readonly StorageFolder _folder;
        private readonly Func<string> _getFilter;
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        private uint _offset;
        private bool _hasMore = true;

        // Tune this based on your typical folder sizes.
        private const uint PageSize = 60;

        public Mp4FolderIncrementalSource(StorageFolder folder,Func<string> getFilter)
        {
            _folder = folder;
            _getFilter = getFilter ?? (() => "");
        }

        public bool HasMoreItems => _hasMore;

        public async Task LoadNextPageAsync()
        {
            if (!_hasMore) return;
            await LoadInternalAsync(PageSize);
        }

        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            var want = count > 0 ? Math.Max(PageSize,count) : PageSize;

            return AsyncInfo.Run(async ct =>
            {
                uint added = await LoadInternalAsync(want,ct);
                return new LoadMoreItemsResult { Count = added };
            });
        }

        private async Task<uint> LoadInternalAsync(uint want,System.Threading.CancellationToken ct = default)
        {
            if (!_hasMore) return 0;

            IReadOnlyList<StorageFile> files;

            try
            {
                // Pull a page of files (all types), then filter down to mp4 + search text.
                files = await _folder.GetFilesAsync(CommonFileQuery.OrderByName,_offset,want);
            }
            catch
            {
                _hasMore = false;
                return 0;
            }

            _offset += (uint)files.Count;

            if (files.Count < want)
                _hasMore = false;

            var filter = (_getFilter() ?? "").Trim();
            var hasFilter = !string.IsNullOrWhiteSpace(filter);
            var filterLower = hasFilter ? filter.ToLowerInvariant() : "";

            uint added = 0;

            foreach (var f in files)
            {
                if (ct.IsCancellationRequested) break;

                if (!string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = f.Path ?? "";
                if (path.Length == 0) continue;

                if (!_seen.Add(path))
                    continue;

                if (hasFilter)
                {
                    // match on Name or DisplayName
                    var name = (f.Name ?? "");
                    var disp = (f.DisplayName ?? "");
                    if (!name.ToLowerInvariant().Contains(filterLower) &&
                        !disp.ToLowerInvariant().Contains(filterLower))
                        continue;
                }

                Add(f);
                added++;
            }

            return added;
        }
    }
}
