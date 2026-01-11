using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using VideoBeast.Playlists;
using VideoBeast.Services;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace VideoBeast.Pages;

public sealed partial class PlaylistDetailsPage : Page
{
    private readonly ObservableCollection<PlaylistItemModel> _items = new();
    private readonly PlaylistStore _store = new();

    private Guid _currentPlaylistId;
    private PlaylistModel? _playlist;
    private PlaylistItemModel? _contextMenuTargetItem;

    public PlaylistDetailsPage()
    {
        InitializeComponent();
        ItemsList.ItemsSource = _items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Guid playlistId)
        {
            _currentPlaylistId = playlistId;
            await LoadPlaylistAsync();
        }
    }

    private async Task LoadPlaylistAsync()
    {
        _items.Clear();

        var playlists = await _store.GetAllAsync();
        _playlist = playlists.FirstOrDefault(p => p.Id == _currentPlaylistId);

        if (_playlist is null)
        {
            PlaylistTitle.Text = "Playlist not found";
            ItemCount.Text = "";
            UpdateEmptyState();
            return;
        }

        PlaylistTitle.Text = _playlist.Name;

        // Load items WITHOUT resolving files immediately (lazy resolution)
        var sortedItems = _playlist.Items.OrderBy(i => i.SortIndex).ToList();

        foreach (var item in sortedItems)
        {
            _items.Add(item);

            // Load thumbnail asynchronously
            _ = LoadItemThumbnailAsync(item);
        }

        UpdateItemCount();
        UpdateEmptyState();
    }

    private async Task LoadItemThumbnailAsync(PlaylistItemModel item)
    {
        if (string.IsNullOrEmpty(item.ThumbnailKey)) return;

        try
        {
            var bitmap = await ThumbnailCache.Instance.LoadThumbnailAsync(item.ThumbnailKey);
            if (bitmap != null)
            {
                // Find the ListView item and update its image
                var container = ItemsList.ContainerFromItem(item) as ListViewItem;
                if (container != null)
                {
                    var image = FindChildByName(container, "ItemThumbnail") as Image;
                    if (image != null)
                    {
                        image.Source = bitmap;
                    }
                }
            }
        }
        catch
        {
            // Ignore thumbnail loading errors
        }
    }

    private FrameworkElement? FindChildByName(DependencyObject parent, string name)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name)
                return element;

            var result = FindChildByName(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void UpdateItemCount()
    {
        if (_playlist is null)
        {
            ItemCount.Text = "";
            return;
        }

        int count = _items.Count;
        ItemCount.Text = count == 1 ? "1 video" : $"{count} videos";
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _items.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        ItemsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack)
        {
            this.Frame.GoBack();
        }
        else
        {
            this.Frame.Navigate(typeof(PlaylistsPage));
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadPlaylistAsync();
    }

    private async void AddVideos_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            FileTypeFilter = { ".mp4" }
        };
        
        // Initialize picker with window handle
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        
        // Allow multiple file selection
        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;
        
        try
        {
            var store = new PlaylistStore();
            await store.AddItemsAsync(_currentPlaylistId, files);
            
            // Show success message
            var dialog = new ContentDialog
            {
                Title = "Videos Added",
                Content = $"Successfully added {files.Count} video{(files.Count > 1 ? "s" : "")} to the playlist.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
            
            // Refresh the playlist view
            await LoadPlaylistAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = $"Failed to add videos: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private async void ChangeCover_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            FileTypeFilter = { ".jpg", ".jpeg", ".png", ".bmp" }
        };
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        
        try
        {
            // Save the custom image
            var key = await ThumbnailCache.Instance.SaveCustomImageAsync(file);
            if (key == null)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = "Failed to save cover image.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
                return;
            }
            
            // Update the playlist
            var store = new PlaylistStore();
            await store.UpdateCoverImageAsync(_currentPlaylistId, key);
            
            // Refresh the playlist
            await LoadPlaylistAsync();
            
            var successDialog = new ContentDialog
            {
                Title = "Cover Updated",
                Content = "Playlist cover image has been updated.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await successDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                Title = "Error",
                Content = $"Failed to update cover: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private void ItemsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is PlaylistItemModel item)
        {
            ItemsList.SelectedItem = item;
        }
    }

    private PlaylistItemModel? SelectedItem => ItemsList.SelectedItem as PlaylistItemModel;

    private async void ItemFlyout_Opening(object sender, object e)
    {
        bool hasSelection = SelectedItem is not null;
        bool isAvailable = hasSelection && !(SelectedItem?.IsMissing ?? true);

        FlyoutPlay.IsEnabled = isAvailable;
        FlyoutShowInExplorer.IsEnabled = isAvailable;
        FlyoutCopyPath.IsEnabled = isAvailable;
        FlyoutRemove.IsEnabled = hasSelection;

        // Store the target item for context menu operations
        _contextMenuTargetItem = SelectedItem;

        // Clear existing playlist items (keep the "New Playlist" item)
        var submenu = AddToPlaylistSubmenu;
        while (submenu.Items.Count > 1)
        {
            submenu.Items.RemoveAt(0);
        }

        // Load playlists and add them to the submenu
        var store = new PlaylistStore();
        var playlists = await store.GetAllAsync();
        
        foreach (var playlist in playlists)
        {
            // Skip the current playlist
            if (playlist.Id == _currentPlaylistId) continue;
            
            var item = new MenuFlyoutItem
            {
                Text = playlist.Name,
                Tag = playlist.Id
            };
            item.Click += Item_AddToExistingPlaylist_Click;
            submenu.Items.Insert(submenu.Items.Count - 1, item); // Insert before "New Playlist"
        }

        // Add separator if there are other playlists
        if (playlists.Count > 1) // More than just the current playlist
        {
            submenu.Items.Insert(submenu.Items.Count - 1, new MenuFlyoutSeparator());
        }
    }

    private async void Item_Play_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
            return;

        var (file, isMissing) = await _store.TryResolveAsync(SelectedItem);

        if (isMissing || file == null)
        {
            SelectedItem.IsMissing = true;

            var dialog = new ContentDialog
            {
                Title = "File Not Found",
                Content = $"The video '{SelectedItem.DisplayName}' could not be found. It may have been moved or deleted.",
                PrimaryButtonText = "Re-link",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await RelinkItemAsync(SelectedItem);
            }
            return;
        }

        if (MainWindow.Instance is not null)
        {
            await MainWindow.Instance.PlayFromUiAsync(file);
        }
    }

    private async void Item_Relink_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
            return;

        await RelinkItemAsync(SelectedItem);
    }

    private async Task RelinkItemAsync(PlaylistItemModel item)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            FileTypeFilter = { ".mp4" }
        };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            var folder = await file.GetParentAsync();
            var folderToken = await _store.GetOrCreateFolderTokenAsync(folder);
            var properties = await file.GetBasicPropertiesAsync();

            // Update item with new reference
            item.FolderToken = folderToken;
            item.FileName = file.Name;
            item.LastKnownFullPath = file.Path;
            item.SizeBytesHint = properties.Size;
            item.LastWriteTimeUtcHint = properties.DateModified.ToString("o");
            item.DisplayName = file.DisplayName;
            item.IsMissing = false;

            // Regenerate thumbnail
            item.ThumbnailKey = await ThumbnailCache.Instance.GetOrCreateThumbnailKeyAsync(file, folderToken);

            // Save changes
            await _store.UpdateItemAsync(_currentPlaylistId, item);

            // Refresh UI
            await LoadPlaylistAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Re-link Failed",
                Content = $"Failed to re-link file: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private async void Item_ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
            return;

        var (file, isMissing) = await _store.TryResolveAsync(SelectedItem);
        if (file is null || isMissing)
            return;

        try
        {
            var dir = Path.GetDirectoryName(file.Path);
            if (string.IsNullOrWhiteSpace(dir))
                return;

            var folder = await StorageFolder.GetFolderFromPathAsync(dir);

            // Best effort: open folder with the file selected; fallback to open folder
            try
            {
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderAsync(folder, options);
            }
            catch
            {
                await Launcher.LaunchFolderAsync(folder);
            }
        }
        catch
        {
            // Handle error silently
        }
    }

    private async void Item_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
            return;

        var (file, isMissing) = await _store.TryResolveAsync(SelectedItem);
        if (file is null || isMissing)
            return;

        try
        {
            var dp = new DataPackage();
            dp.SetText(file.Path);
            Clipboard.SetContent(dp);
            Clipboard.Flush();
        }
        catch
        {
            // Handle error silently
        }
    }

    private async void Item_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null || _playlist is null)
            return;

        try
        {
            await _store.RemoveItemAsync(_playlist.Id, SelectedItem.Id);
            _items.Remove(SelectedItem);
            UpdateItemCount();
            UpdateEmptyState();
        }
        catch
        {
            // Handle error silently
        }
    }

    private async void ItemsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_playlist is null)
            return;

        try
        {
            // Extract the new order of item IDs
            var newOrder = _items.Select(item => item.Id).ToList();

            // Update the store with the new order
            await _store.ReorderAsync(_playlist.Id, newOrder);
        }
        catch
        {
            // Handle error silently - could reload to restore original order
        }
    }

    private async void Item_AddToExistingPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetItem == null) return;
        
        var menuItem = sender as MenuFlyoutItem;
        if (menuItem?.Tag is Guid targetPlaylistId)
        {
            try
            {
                var store = new PlaylistStore();
                
                // Resolve the file first
                var (file, isMissing) = await store.TryResolveAsync(_contextMenuTargetItem);
                if (isMissing || file == null)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "File Not Found",
                        Content = "Cannot add a missing file to another playlist. Please re-link the file first.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }
                
                // Add to target playlist
                await store.AddItemsAsync(targetPlaylistId, new[] { file });
                
                // Show success message
                var targetPlaylist = (await store.GetAllAsync()).FirstOrDefault(p => p.Id == targetPlaylistId);
                var dialog = new ContentDialog
                {
                    Title = "Video Added",
                    Content = $"Added '{_contextMenuTargetItem.DisplayName}' to playlist '{targetPlaylist?.Name ?? "Unknown"}'.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to add video to playlist: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
    }

    private async void Item_AddToNewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetItem == null) return;

        // Show dialog to get playlist name
        var nameDialog = new ContentDialog
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
        nameDialog.Content = textBox;

        var result = await nameDialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            try
            {
                var store = new PlaylistStore();
                
                // Resolve the file first
                var (file, isMissing) = await store.TryResolveAsync(_contextMenuTargetItem);
                if (isMissing || file == null)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "File Not Found",
                        Content = "Cannot add a missing file to a new playlist. Please re-link the file first.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }
                
                // Create new playlist and add video
                var playlist = await store.CreateAsync(textBox.Text.Trim());
                await store.AddItemsAsync(playlist.Id, new[] { file });
                
                // Show success message
                var dialog = new ContentDialog
                {
                    Title = "Playlist Created",
                    Content = $"Created playlist '{playlist.Name}' and added '{_contextMenuTargetItem.DisplayName}'.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to create playlist: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
    }
}
