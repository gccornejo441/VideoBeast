using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

using Windows.Storage;

namespace VideoBeast.Pages;

public sealed partial class PlaylistPage : Page
{
    private readonly ObservableCollection<StorageFile> _all = new();
    private readonly ObservableCollection<StorageFile> _filtered = new();

    private StorageFolder? _folder;

    public PlaylistPage()
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
            PlaylistTitle.Text = "Up Next";
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
            ? "Up Next"
            : $"{_folder.Name} • {_filtered.Count}";
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

    private void PlaylistFileFlyout_Opening(object sender,object e)
    {
        bool hasSelection = SelectedFile is not null;

        FlyoutPlay.IsEnabled = hasSelection;
        FlyoutShowInFinder.IsEnabled = hasSelection;
        FlyoutCopyPathname.IsEnabled = hasSelection;
        FlyoutRename.IsEnabled = hasSelection;
        FlyoutMoveToTrash.IsEnabled = hasSelection;
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
}
