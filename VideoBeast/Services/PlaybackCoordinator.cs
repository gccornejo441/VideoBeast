using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using Windows.Storage;

using VideoBeast.Pages;

namespace VideoBeast.Services;

public sealed class PlaybackCoordinator
{
    private readonly Func<Frame> _getFrame;
    private StorageFile? _pendingPlayFile;

    public string? PlayingFilePath { get; private set; }

    public PlaybackCoordinator(Func<Frame> getFrame)
    {
        _getFrame = getFrame ?? throw new ArgumentNullException(nameof(getFrame));
    }

    /// <summary>
    /// Request playback of a file. If not currently on PlayerPage, navigates there and plays after navigation.
    /// If already on PlayerPage, plays immediately.
    /// </summary>
    public Task RequestPlayAsync(StorageFile file,PlayerSettings settings)
    {
        if (file is null) return Task.CompletedTask;

        _pendingPlayFile = file;

        var frame = _getFrame();

        // Ensure we're on PlayerPage
        if (frame.CurrentSourcePageType != typeof(PlayerPage))
            frame.Navigate(typeof(PlayerPage));

        // If already there, play immediately
        if (frame.Content is PlayerPage page)
        {
            page.ApplySettings(settings);
            page.Play(file,settings);

            PlayingFilePath = file.Path;
            _pendingPlayFile = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Call from Frame.Navigated. Ensures PlayerPage always reflects latest settings + folder text,
    /// and plays any pending file request.
    /// </summary>
    public void HandleFrameNavigated(
        NavigationEventArgs e,
        PlayerSettings settings,
        string currentFolderText,
        bool isLibraryMissing)
    {
        var frame = _getFrame();

        if (frame.Content is not PlayerPage page)
            return;

        page.ApplySettings(settings);
        page.SetCurrentFolderText(currentFolderText);

        if (_pendingPlayFile is not null)
        {
            page.Play(_pendingPlayFile,settings);
            PlayingFilePath = _pendingPlayFile.Path;
            _pendingPlayFile = null;
        }
        else
        {
            page.ShowEmptyState(isLibraryMissing);
        }
    }

    /// <summary>
    /// Update the PlayerPage UI if it is currently displayed (no navigation required).
    /// </summary>
    public void UpdatePlayerPageUi(
        PlayerSettings settings,
        string currentFolderText,
        bool isLibraryMissing)
    {
        var frame = _getFrame();
        if (frame.Content is not PlayerPage page)
            return;

        page.ApplySettings(settings);
        page.SetCurrentFolderText(currentFolderText);

        // Don't override playback if there's a pending play request.
        if (_pendingPlayFile is null)
            page.ShowEmptyState(isLibraryMissing);
    }

    /// <summary>
    /// If the file at targetPath is currently playing, stop playback to avoid file locks.
    /// </summary>
    public Task StopIfPlayingAsync(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return Task.CompletedTask;

        if (!string.Equals(PlayingFilePath,targetPath,StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var frame = _getFrame();
        if (frame.Content is PlayerPage page)
            page.StopAndShowEmpty();

        PlayingFilePath = null;
        return Task.CompletedTask;
    }

    public void ClearPending() => _pendingPlayFile = null;
}

