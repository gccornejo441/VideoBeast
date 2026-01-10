using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml.Controls;

using VideoBeast.Pages;

using Windows.Storage;

namespace VideoBeast.Services;

public sealed class LibraryGuardService
{
    private readonly LibraryFolderService _folders;
    private readonly Func<Frame> _getFrame;

    public LibraryGuardService(
        LibraryFolderService folders,
        Func<Frame> getFrame)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _getFrame = getFrame ?? throw new ArgumentNullException(nameof(getFrame));
    }

    /// <summary>
    /// Ensures a library folder exists. If missing, shows best-practice UX and returns null.
    /// </summary>
    public async Task<StorageFolder?> RequireLibraryAsync()
    {
        bool ok = await _folders.EnsureLibraryFolderAsync(() =>
        {
            ShowMissingLibrary(_getFrame());
        });

        return ok ? _folders.LibraryFolder : null;
    }

    /// <summary>
    /// Ensures a library folder exists and returns the best destination folder:
    /// SelectedFolder if set, else LibraryFolder.
    /// </summary>
    public async Task<StorageFolder?> RequireDestinationAsync()
    {
        var library = await RequireLibraryAsync();
        if (library is null) return null;

        return _folders.SelectedFolder ?? library;
    }

    private void ShowMissingLibrary(Frame frame)
    {
        frame?.DispatcherQueue.TryEnqueue(() =>
        {
            if (frame.Content is PlayerPage page)
            {
                page.SetCurrentFolderText("No folder selected");
                page.ShowEmptyState(true);
            }
        });
    }
}
