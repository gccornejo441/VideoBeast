using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using Windows.Storage.Pickers;

using VideoBeast.Playlists;
using VideoBeast.Services;

namespace VideoBeast.Pages;

public sealed partial class PlaylistsPage : Page
{
    private readonly ObservableCollection<PlaylistModel> _playlists = new();
    private readonly PlaylistStore _store = new();

    public PlaylistsPage()
    {
        InitializeComponent();
        PlaylistsList.ItemsSource = _playlists;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Always reload playlists when navigating to this page to ensure counts are up-to-date
        await LoadPlaylistsAsync();
    }

    private async Task LoadPlaylistsAsync()
    {
        _playlists.Clear();

        var playlists = await _store.GetAllAsync();
        foreach (var playlist in playlists.OrderByDescending(p => p.UpdatedUtc))
        {
            _playlists.Add(playlist);
            
            // Load thumbnail asynchronously
            _ = LoadPlaylistThumbnailAsync(playlist);
        }

        UpdateEmptyState();
    }

    private async Task LoadPlaylistThumbnailAsync(PlaylistModel playlist)
    {
        if (string.IsNullOrEmpty(playlist.CoverImageKey)) return;
        
        try
        {
            var bitmap = await ThumbnailCache.Instance.LoadThumbnailAsync(playlist.CoverImageKey);
            if (bitmap != null)
            {
                // Find the ListView item and update its image
                var container = PlaylistsList.ContainerFromItem(playlist) as ListViewItem;
                if (container != null)
                {
                    var image = FindChildByName(container, "CoverImage") as Image;
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

    private void UpdateEmptyState()
    {
        bool isEmpty = _playlists.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        PlaylistsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var name = await PromptPlaylistNameAsync("New Playlist", "");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var playlist = await _store.CreateAsync(name.Trim());
            _playlists.Insert(0, playlist);
            UpdateEmptyState();
        }
        catch
        {
            // Handle error silently or show message
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadPlaylistsAsync();
    }

    private void PlaylistsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistModel playlist)
        {
            this.Frame.Navigate(typeof(PlaylistDetailsPage), playlist.Id);
        }
    }

    private void PlaylistsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is PlaylistModel playlist)
        {
            PlaylistsList.SelectedItem = playlist;
        }
    }

    private PlaylistModel? SelectedPlaylist => PlaylistsList.SelectedItem as PlaylistModel;

    private void PlaylistFlyout_Opening(object sender, object e)
    {
        bool hasSelection = SelectedPlaylist is not null;

        FlyoutOpen.IsEnabled = hasSelection;
        FlyoutChangeCover.IsEnabled = hasSelection;
        FlyoutRename.IsEnabled = hasSelection;
        FlyoutDelete.IsEnabled = hasSelection;
    }

    private void Playlist_Open_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlaylist is null)
            return;

        this.Frame.Navigate(typeof(PlaylistDetailsPage), SelectedPlaylist.Id);
    }

    private async void Playlist_ChangeCover_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlaylist is null)
            return;

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
            await _store.UpdateCoverImageAsync(SelectedPlaylist.Id, key);
            
            // Refresh the list
            await LoadPlaylistsAsync();
            
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

    private async void Playlist_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlaylist is null)
            return;

        var newName = await PromptPlaylistNameAsync("Rename Playlist", SelectedPlaylist.Name);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        try
        {
            await _store.RenameAsync(SelectedPlaylist.Id, newName.Trim());
            await LoadPlaylistsAsync();
        }
        catch
        {
            // Handle error silently or show message
        }
    }

    private async void Playlist_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlaylist is null)
            return;

        bool confirmed = await ConfirmDeleteAsync(SelectedPlaylist);
        if (!confirmed)
            return;

        try
        {
            await _store.DeleteAsync(SelectedPlaylist.Id);
            _playlists.Remove(SelectedPlaylist);
            UpdateEmptyState();
        }
        catch
        {
            // Handle error silently or show message
        }
    }

    private async Task<string?> PromptPlaylistNameAsync(string title, string currentName)
    {
        var tb = new TextBox
        {
            Text = currentName,
            PlaceholderText = "Playlist name",
            MinWidth = 320
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Enter a name for the playlist:",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(tb);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        return tb.Text;
    }

    private async Task<bool> ConfirmDeleteAsync(PlaylistModel playlist)
    {
        var content = new StackPanel { Spacing = 8 };

        content.Children.Add(new TextBlock
        {
            Text = "Are you sure you want to delete this playlist?",
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = playlist.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = $"{playlist.Items.Count} videos",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
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
