using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace VideoBeast.Services;

public sealed class LibraryFileActions
{
    private readonly Func<XamlRoot?> _getXamlRoot;
    private readonly Func<Task<bool>> _ensureLibraryFolderAsync;
    private readonly Func<Task> _rebuildNavMenuAsync;
    private readonly Func<string,Task> _stopPlaybackIfPlayingAsync;
    private readonly Action<string,InfoBarSeverity> _showStatus;
    private readonly Action<string>? _onDeletedPath;

    public LibraryFileActions(
        Func<XamlRoot?> getXamlRoot,
        Func<Task<bool>> ensureLibraryFolderAsync,
        Func<Task> rebuildNavMenuAsync,
        Func<string,Task> stopPlaybackIfPlayingAsync,
        Action<string,InfoBarSeverity> showStatus,
        Action<string>? onDeletedPath = null)
    {
        _getXamlRoot = getXamlRoot;
        _ensureLibraryFolderAsync = ensureLibraryFolderAsync;
        _rebuildNavMenuAsync = rebuildNavMenuAsync;
        _stopPlaybackIfPlayingAsync = stopPlaybackIfPlayingAsync;
        _showStatus = showStatus;
        _onDeletedPath = onDeletedPath;
    }

    public async Task ShowInExplorerAsync(StorageFile file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file.Path);
            if (string.IsNullOrWhiteSpace(dir))
            {
                _showStatus("Could not resolve the containing folder.",InfoBarSeverity.Warning);
                return;
            }

            var folder = await StorageFolder.GetFolderFromPathAsync(dir);

            // Best effort: open folder with the file selected; fallback to open folder
            try
            {
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(file);

                await Launcher.LaunchFolderAsync(folder,options);
            }
            catch
            {
                await Launcher.LaunchFolderAsync(folder);
            }
        }
        catch
        {
            _showStatus("Could not open in Explorer (access denied).",InfoBarSeverity.Error);
        }
    }

    public void CopyPathToClipboard(StorageFile file)
    {
        try
        {
            var dp = new DataPackage();
            dp.SetText(file.Path);

            Clipboard.SetContent(dp);
            Clipboard.Flush();

            _showStatus("Path copied to clipboard.",InfoBarSeverity.Informational);
        }
        catch
        {
            _showStatus("Failed to copy path to clipboard.",InfoBarSeverity.Error);
        }
    }

    public async Task RenameFileWithDialogAsync(StorageFile file)
    {
        if (!await _ensureLibraryFolderAsync().ConfigureAwait(true))
            return;

        // If playing, stop first to avoid file locks
        await _stopPlaybackIfPlayingAsync(file.Path).ConfigureAwait(true);
        await Task.Yield();

        var ext = file.FileType; // ".mp4"
        var baseName = await PromptRenameBaseNameAsync(file,ext).ConfigureAwait(true);
        if (baseName is null)
            return;

        baseName = baseName.Trim();

        // If user accidentally typed the extension, strip it
        if (!string.IsNullOrEmpty(ext) && baseName.EndsWith(ext,StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^ext.Length];

        if (string.IsNullOrWhiteSpace(baseName))
        {
            _showStatus("Rename canceled (empty name).",InfoBarSeverity.Warning);
            return;
        }

        if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _showStatus("Invalid file name characters.",InfoBarSeverity.Warning);
            return;
        }

        var newName = baseName + ext;

        try
        {
            await file.RenameAsync(newName,NameCollisionOption.FailIfExists);

            await _rebuildNavMenuAsync().ConfigureAwait(true);
            _showStatus("Renamed file.",InfoBarSeverity.Informational);
        }
        catch
        {
            _showStatus("Rename failed (name may already exist or file is locked).",InfoBarSeverity.Error);
        }
    }

    public async Task DeleteFileWithConfirmAsync(StorageFile file)
    {
        if (!await _ensureLibraryFolderAsync().ConfigureAwait(true))
            return;

        bool ok = await ConfirmDeleteAsync(file).ConfigureAwait(true);
        if (!ok)
        {
            _showStatus("Delete canceled.",InfoBarSeverity.Informational);
            return;
        }

        // If playing, stop first to avoid file locks
        await _stopPlaybackIfPlayingAsync(file.Path).ConfigureAwait(true);
        await Task.Yield();

        var targetPath = file.Path;

        try
        {
            await file.DeleteAsync(StorageDeleteOption.Default);

            _onDeletedPath?.Invoke(targetPath);

            await _rebuildNavMenuAsync().ConfigureAwait(true);
            _showStatus("Deleted file.",InfoBarSeverity.Informational);
        }
        catch
        {
            _showStatus("Delete failed (file may be locked).",InfoBarSeverity.Error);
        }
    }

    private async Task<string?> PromptRenameBaseNameAsync(StorageFile file,string ext)
    {
        var xamlRoot = _getXamlRoot();
        if (xamlRoot is null)
        {
            _showStatus("UI not ready yet. Try again.",InfoBarSeverity.Warning);
            return null;
        }

        var tb = new TextBox
        {
            Text = file.DisplayName, // name without extension
            PlaceholderText = "New name",
            MinWidth = 320
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Enter a new name:",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(tb);
        content.Children.Add(new TextBlock
        {
            Text = $"Extension: {ext}",
            Opacity = 0.7
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Rename file",
            Content = content,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        return tb.Text;
    }

    private async Task<bool> ConfirmDeleteAsync(StorageFile file)
    {
        var xamlRoot = _getXamlRoot();
        if (xamlRoot is null)
        {
            _showStatus("UI not ready yet. Try again.",InfoBarSeverity.Warning);
            return false;
        }

        var content = new StackPanel { Spacing = 8 };

        content.Children.Add(new TextBlock
        {
            Text = "Are you sure you want to delete this file?",
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = file.Name,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = file.Path,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Confirm delete",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
