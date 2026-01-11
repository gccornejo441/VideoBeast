using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

using Windows.Storage;

using VideoBeast.Playlists;

namespace VideoBeast.Pages;

public sealed partial class FolderVideosPage : Page
{
    private readonly ObservableCollection<StorageFile> _all = new();
    private readonly ObservableCollection<StorageFile> _filtered = new();

    private StorageFolder? _folder;
    private StorageFile? _contextMenuTargetFile;

    public FolderVideosPage()
    {
        InitializeComponent();
        FilesList.ItemsSource = _filtered;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _folder = e.Parameter as StorageFolder;
        await LoadFolderAsync(_folder);
    }

    public async Task LoadFolderAsync(StorageFolder? folder)
    {
        _folder = folder;

        _all.Clear();
        _filtered.Clear();

        if (_folder is null)
        {
            PlaylistTitle.Text = "Folder Videos";
            return;
        }

        var files = await _folder.GetFilesAsync();
        var mp4s = files
            .Where(f => string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name);

        foreach (var f in mp4s)
            _all.Add(f);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = (PlaylistFilterBox.Text ?? "").Trim();

        _filtered.Clear();
        foreach (var f in _all)
        {
            if (string.IsNullOrEmpty(q) ||
                f.Name.Contains(q,StringComparison.OrdinalIgnoreCase))
            {
                _filtered.Add(f);
            }
        }

        PlaylistTitle.Text = _folder is null
            ? "Folder Videos"
            : $"{_folder.Name} � {_filtered.Count}";
    }

    private async void FilesList_ItemClick(object sender,ItemClickEventArgs e)
    {
        if (e.ClickedItem is StorageFile file && MainWindow.Instance is not null)
            await MainWindow.Instance.PlayFromUiAsync(file);
    }

    private void FilesList_RightTapped(object sender,RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is StorageFile file)
            FilesList.SelectedItem = file;
    }

    private async void PlaylistRefresh_Click(object sender,RoutedEventArgs e)
        => await LoadFolderAsync(_folder);

    private void PlaylistFilterBox_TextChanged(object sender,TextChangedEventArgs e)
        => ApplyFilter();

    private StorageFile? SelectedFile => FilesList.SelectedItem as StorageFile;

    private async void PlaylistFileFlyout_Opening(object sender,object e)
    {
        bool hasSelection = SelectedFile is not null;

        FlyoutPlay.IsEnabled = hasSelection;
        FlyoutShowInFinder.IsEnabled = hasSelection;
        FlyoutCopyPathname.IsEnabled = hasSelection;
        FlyoutRename.IsEnabled = hasSelection;
        FlyoutMoveToTrash.IsEnabled = hasSelection;

        // Store the target file for context menu operations
        _contextMenuTargetFile = SelectedFile;

        // Clear existing playlist items (keep the "New Playlist" item)
        var submenu = AddToPlaylistSubmenu;
        while (submenu.Items.Count > 1)
        {
            submenu.Items.RemoveAt(0);
        }

        // Load playlists and add them to the submenu
        try
        {
            var store = new PlaylistStore();
            var playlists = await store.GetAllAsync();
            
            foreach (var playlist in playlists)
            {
                var item = new MenuFlyoutItem
                {
                    Text = playlist.Name,
                    Tag = playlist.Id
                };
                item.Click += AddToExistingPlaylist_Click;
                submenu.Items.Insert(submenu.Items.Count - 1, item); // Insert before "New Playlist"
            }

            // Add separator if there are playlists
            if (playlists.Count > 0)
            {
                submenu.Items.Insert(submenu.Items.Count - 1, new MenuFlyoutSeparator());
            }
        }
        catch
        {
            // If loading fails, just show the "New Playlist" option
        }
    }

    private async void Playlist_Play_Click(object sender,RoutedEventArgs e)
    {
        if (SelectedFile is null || MainWindow.Instance is null) return;
        await MainWindow.Instance.PlayFromUiAsync(SelectedFile);
    }

    private async void Playlist_ShowInFinder_Click(object sender,RoutedEventArgs e)
    {
        if (SelectedFile is null || MainWindow.Instance is null) return;
        await MainWindow.Instance.ShowInFolderFromUiAsync(SelectedFile);
    }

    private void Playlist_CopyPath_Click(object sender,RoutedEventArgs e)
    {
        if (SelectedFile is null || MainWindow.Instance is null) return;
        MainWindow.Instance.CopyPathFromUi(SelectedFile);
    }

    private async void Playlist_Rename_Click(object sender,RoutedEventArgs e)
    {
        if (SelectedFile is null || MainWindow.Instance is null) return;
        await MainWindow.Instance.RenameFromUiAsync(SelectedFile);
        await LoadFolderAsync(_folder);
    }

    private async void Playlist_MoveToTrash_Click(object sender,RoutedEventArgs e)
    {
        if (SelectedFile is null || MainWindow.Instance is null) return;
        await MainWindow.Instance.DeleteFromUiAsync(SelectedFile);
        await LoadFolderAsync(_folder);
    }

    private async void AddToExistingPlaylist_Click(object sender, RoutedEventArgs e)
    {
        // Support multi-select: use selected items if available, otherwise use context menu target
        var selectedFiles = FilesList.SelectedItems.Cast<StorageFile>().ToList();
        if (selectedFiles.Count == 0 && _contextMenuTargetFile != null)
        {
            selectedFiles.Add(_contextMenuTargetFile);
        }
        
        if (selectedFiles.Count == 0) return;
        
        var item = sender as MenuFlyoutItem;
        if (item?.Tag is Guid playlistId)
        {
            try
            {
                var store = new PlaylistStore();
                await store.AddItemsAsync(playlistId, selectedFiles);
                
                // Show success message (optional - using basic approach since StatusService may not be available)
                var dialog = new ContentDialog
                {
                    Title = "Success",
                    Content = selectedFiles.Count == 1
                        ? "Added video to playlist"
                        : $"Added {selectedFiles.Count} videos to playlist",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to add to playlist: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = dialog.ShowAsync();
            }
        }
    }

    private async void AddToNewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        // Support multi-select: use selected items if available, otherwise use context menu target
        var selectedFiles = FilesList.SelectedItems.Cast<StorageFile>().ToList();
        if (selectedFiles.Count == 0 && _contextMenuTargetFile != null)
        {
            selectedFiles.Add(_contextMenuTargetFile);
        }
        
        if (selectedFiles.Count == 0) return;

        // Show dialog to get playlist name
        var dialog = new ContentDialog
        {
            Title = "New Playlist",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var textBox = new TextBox
        {
            PlaceholderText = "Playlist name",
            Margin = new Thickness(0, 8, 0, 0)
        };
        dialog.Content = textBox;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            try
            {
                var store = new PlaylistStore();
                var playlist = await store.CreateAsync(textBox.Text.Trim());
                await store.AddItemsAsync(playlist.Id, selectedFiles);
                
                // Show success message
                var successDialog = new ContentDialog
                {
                    Title = "Success",
                    Content = selectedFiles.Count == 1
                        ? $"Created playlist '{playlist.Name}' and added video"
                        : $"Created playlist '{playlist.Name}' and added {selectedFiles.Count} videos",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to create playlist: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = errorDialog.ShowAsync();
            }
        }
    }
}
