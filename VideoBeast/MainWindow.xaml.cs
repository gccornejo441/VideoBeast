using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using VideoBeast.Navigation;
using VideoBeast.Pages;
using VideoBeast.Playlists;
using VideoBeast.Services;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

using WinRT.Interop;

namespace VideoBeast;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly LibraryFolderService _folderService = new();
    private readonly LibraryImportService _importService = new();
    private readonly PlaybackCoordinator _playback;
    private readonly LibraryGuardService _guard;
    private readonly SearchCoordinator _search;
    private readonly ActionRouter _actions;

    private StorageFile? _selectedFile;
    private PlayerSettings _playerSettings = new();
    private StorageFile? _contextMenuTargetFile;

    private readonly AppWindow _appWindow;
    private readonly LibraryTreeBuilder _navTreeBuilder;
    private readonly LibraryFileActions _fileActions;
    private readonly WindowChromeService _chrome;
    private readonly WindowPlacementService _placementService;

    private const string PlaylistTag = "view:playlist";
    private const string PlaylistsTag = "page:playlists";
    private bool _isNavigatingProgrammatically = false;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        _playback = new PlaybackCoordinator(() => Shell.Frame);
        _guard = new LibraryGuardService(_folderService,() => Shell.Frame);

        _actions = new ActionRouter(onError: (_,__) => { });
        _actions.Register("action:chooseFolder",ChooseFolderAsync);
        _actions.Register("action:import",ImportAsync);
        _actions.Register("action:refresh",RefreshAsync);
        _actions.Register("action:delete",DeleteSelectedAsync);
        _actions.Register("action:openFolder",OpenFolderAsync);

        var searchService = new LibrarySearchService();
        _search = new SearchCoordinator(
            getLibraryFolder: () => _folderService.LibraryFolder,
            getSelectedFolder: () => _folderService.SelectedFolder,
            getPlayerSettings: () => _playerSettings,
            onFileChosen: file => _selectedFile = file,
            playback: _playback,
            searchService: searchService,
            actionRouter: _actions,
            getXamlRoot: () => RootGrid.XamlRoot,
            navigateToPage: (type, param) => Shell.Frame.Navigate(type, param),
            showStatus: (message, severity) => Shell.ShowStatus(message, severity));

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Set window icon for taskbar
        SetWindowIcon(hwnd);

        var sizing = new WindowSizingService(_appWindow);
        sizing.ApplyMinimumSize(960,600);

        _placementService = new WindowPlacementService();
        var placement = _placementService.LoadPlacement();
        if (placement is not null)
            _placementService.ApplyPlacement(_appWindow,placement);
        else
            sizing.ApplyDefaultSizeIfFirstLaunch(1280,720,1024,768);

        _appWindow.Closing += AppWindow_Closing;

        _chrome = new WindowChromeService(
            appWindow: _appWindow,
            dispatcherQueue: DispatcherQueue,
            getExtendsContentIntoTitleBar: () => ExtendsContentIntoTitleBar,
            setExtendsContentIntoTitleBar: v => ExtendsContentIntoTitleBar = v,
            setTitleBar: ui => SetTitleBar(ui),
            getTitleBarElement: () => Shell.TitleBar.TitleBarControl,
            getNavView: () => Shell.Navigation
        );

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Shell.TitleBar.TitleBarControl);

        _fileActions = new LibraryFileActions(
            getXamlRoot: () => RootGrid.XamlRoot,
            ensureLibraryFolderAsync: async () => (await _guard.RequireLibraryAsync()) is not null,
            rebuildNavMenuAsync: RebuildNavMenuAsync,
            stopPlaybackIfPlayingAsync: _playback.StopIfPlayingAsync,
            onDeletedPath: deletedPath =>
            {
                if (_selectedFile?.Path == deletedPath)
                    _selectedFile = null;
            });

        _navTreeBuilder = new LibraryTreeBuilder(
            fileFlyoutFactory: (file,item) => CreateFileContextFlyout(file,item),
            onFileRightTapped: (file,item) =>
            {
                Shell.Navigation.SelectedItem = item;
                _selectedFile = file;
                _search.Reset();
            });

        Shell.Navigation.Expanding += NavView_Expanding;
        Shell.Navigation.ItemInvoked += NavView_ItemInvoked;
        Shell.Navigation.SelectionChanged += NavView_SelectionChanged;

        Shell.Frame.Navigated += RootFrame_Navigated;

        Shell.TitleBar.PaneToggleRequested += TitleBar_PaneToggleRequested;
        Shell.TitleBar.BackRequested += TitleBar_BackRequested;

        Shell.TitleBar.TextChanged += (s,e) => _search.HandleTextChanged(s,e);
        Shell.TitleBar.SuggestionChosen += (s,e) => _search.HandleSuggestionChosen(s,e);
        Shell.TitleBar.QuerySubmitted += (s,e) => _search.HandleQuerySubmitted(s,e);

        _playerSettings = PlayerSettingsStore.Load();

        _ = InitializeLibraryAsync();
        Shell.Frame.Navigate(typeof(VideoBeast.Pages.PlayerPage));

        UpdateBackButtonVisibility();
    }

    private void AppWindow_Closing(AppWindow sender,AppWindowClosingEventArgs args)
        => _placementService.SavePlacement(sender);

    private void RootGrid_Loaded(object sender,RoutedEventArgs e) { }

    public static Stretch DefaultPlayerStretch => Stretch.Uniform;
    public static string PlayerStretchSettingKey => "PlayerStretch";

    public void SavePlayerStretch(Stretch stretch)
    {
        _playerSettings.Stretch = stretch;
        PlayerSettingsStore.Save(_playerSettings);

        if (Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
            page.ApplySettings(_playerSettings);
    }

    public bool IsPlayerFullscreen => _chrome.IsPlayerFullscreen;

    public void SetPlayerFullscreen(bool on)
    {
        _chrome.SetPlayerFullscreen(on);
        UpdateBackButtonVisibility();
    }

    public void TogglePlayerFullscreen()
    {
        _chrome.TogglePlayerFullscreen();
        UpdateBackButtonVisibility();
    }

    public PlayerSettings GetPlayerSettings() => _playerSettings;

    public void SavePlayerSettings(PlayerSettings settings)
    {
        _playerSettings = (settings ?? new PlayerSettings()).Clone();
        PlayerSettingsStore.Save(_playerSettings);
    }

    public void SaveAndApplyPlayerSettings(PlayerSettings settings)
    {
        SavePlayerSettings(settings);

        _playback.UpdatePlayerPageUi(
            settings: _playerSettings,
            currentFolderText: _folderService.LibraryFolder?.Path ?? "No folder selected",
            isLibraryMissing: _folderService.LibraryFolder is null);
    }

    public void RefreshSearchCoordinatorAiSettings()
    {
        _search.RefreshAiSettings();
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        Shell.ShowStatus(message, severity);
    }

    public async Task PlayFromUiAsync(StorageFile file)
    {
        _selectedFile = file;
        _search.Reset();
        await _playback.RequestPlayAsync(file,_playerSettings);
    }

    public Task ShowInFolderFromUiAsync(StorageFile file)
        => _fileActions.ShowInExplorerAsync(file);

    public void CopyPathFromUi(StorageFile file)
        => _fileActions.CopyPathToClipboard(file);

    public Task RenameFromUiAsync(StorageFile file)
        => _fileActions.RenameFileWithDialogAsync(file);

    public Task DeleteFromUiAsync(StorageFile file)
        => _fileActions.DeleteFileWithConfirmAsync(file);

    private void TitleBar_PaneToggleRequested(TitleBar sender,object args)
    {
        Shell.Navigation.IsPaneOpen = !Shell.Navigation.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender,object args)
    {
        if (Shell.Frame.CanGoBack)
            Shell.Frame.GoBack();

        UpdateBackButtonVisibility();
    }

    private void UpdateBackButtonVisibility()
    {
        Shell.TitleBar.TitleBarControl.IsBackButtonVisible = Shell.Frame.CanGoBack;
    }

    private async Task InitializeLibraryAsync()
    {
        await _folderService.RestoreAsync();
        await RebuildNavMenuAsync();

        _playback.UpdatePlayerPageUi(
            settings: _playerSettings,
            currentFolderText: _folderService.LibraryFolder?.Path ?? "No folder selected",
            isLibraryMissing: _folderService.LibraryFolder is null);

        _search.Reset();
    }

    private void EnsurePlaylistNavItem()
    {
        bool exists = Shell.Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .Any(i => i.Tag as string == PlaylistTag);

        if (exists) return;

        var playlistItem = new NavigationViewItem
        {
            Content = "Library",
            Icon = new SymbolIcon(Symbol.Bullets),
            Tag = PlaylistTag
        };

        int commandsIndex = -1;
        for (int i = 0; i < Shell.Navigation.MenuItems.Count; i++)
        {
            if (Shell.Navigation.MenuItems[i] is NavigationViewItem nvi
                && nvi.Content as string == "Commands")
            {
                commandsIndex = i;
                break;
            }
        }

        if (commandsIndex >= 0)
            Shell.Navigation.MenuItems.Insert(commandsIndex + 1, playlistItem);
        else
            Shell.Navigation.MenuItems.Add(playlistItem);
    }

    private void EnsurePlaylistsNavItem()
    {
        var existing = Shell.Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == PlaylistsTag);
        
        if (existing != null) return;

        var playlistsItem = new NavigationViewItem
        {
            Content = "Playlists",
            Tag = PlaylistsTag,
            Icon = new SymbolIcon(Symbol.MusicInfo)
        };

        var folderVideosItem = Shell.Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == PlaylistTag);
        
        if (folderVideosItem != null)
        {
            var index = Shell.Navigation.MenuItems.IndexOf(folderVideosItem);
            Shell.Navigation.MenuItems.Insert(index + 1, playlistsItem);
        }
        else
        {
            Shell.Navigation.MenuItems.Add(playlistsItem);
        }
    }

    private async Task RebuildNavMenuAsync()
    {
        await _navTreeBuilder.RebuildAsync(Shell.Navigation,_folderService.LibraryFolder);
        EnsurePlaylistNavItem();
        EnsurePlaylistsNavItem();
    }

    private async void NavView_Expanding(NavigationView sender,NavigationViewItemExpandingEventArgs args)
    {
        await _navTreeBuilder.HandleExpandingAsync(args);
    }

    private void NavView_SelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        // Ignore selection changes triggered by programmatic navigation (e.g., back button)
        if (_isNavigatingProgrammatically)
            return;
        
        if (args.IsSettingsSelected)
        {
            // Check if we're already on the Settings page to avoid duplicate navigation
            if (Shell.Frame.Content is VideoBeast.Pages.SettingsPage)
                return;
            
            Shell.Frame.Navigate(typeof(VideoBeast.Pages.SettingsPage));
        }
    }

    private async void NavView_ItemInvoked(NavigationView sender,NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            // Check if we're already on the Settings page to avoid duplicate navigation
            if (Shell.Frame.Content is VideoBeast.Pages.SettingsPage)
                return;
            
            Shell.Frame.Navigate(typeof(VideoBeast.Pages.SettingsPage));
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem nvi)
            return;

        if (nvi.Tag is string viewTag && viewTag == PlaylistTag)
        {
            var folder = _folderService.SelectedFolder ?? _folderService.LibraryFolder;
            if (folder is null)
                return;

            // Check if we're already on the Folder Videos page to avoid duplicate navigation
            if (Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage)
                return;

            Shell.Frame.Navigate(typeof(VideoBeast.Pages.FolderVideosPage),folder);
            return;
        }

        if (nvi.Tag is string playlistsTag && playlistsTag == PlaylistsTag)
        {
            // Check if we're already on the Playlists page to avoid duplicate navigation
            if (Shell.Frame.Content is VideoBeast.Pages.PlaylistsPage)
                return;

            Shell.Frame.Navigate(typeof(VideoBeast.Pages.PlaylistsPage));
            return;
        }

        if (nvi.Tag is string tagStr && tagStr.StartsWith("action:",StringComparison.Ordinal))
        {
            await _actions.TryInvokeAsync(tagStr);
            return;
        }

        if (nvi.Tag is NodeContent c)
        {
            if (c.File is not null)
            {
                _selectedFile = c.File;
                _search.Reset();
                await _playback.RequestPlayAsync(c.File,_playerSettings);
                return;
            }

            if (c.Folder is not null)
            {
                _folderService.SetSelectedFolder(c.Folder);

                if (Shell.Frame.Content is VideoBeast.Pages.PlayerPage player)
                    player.SetCurrentFolderText(c.Folder.Path);

                if (Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage playlist)
                    await playlist.LoadFolderAsync(c.Folder);

                _search.Reset();
            }
        }
    }

    private void RootFrame_Navigated(object sender,Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        UpdateBackButtonVisibility();
        
        // Sync NavigationView selection with current page to prevent unwanted re-navigation
        _isNavigatingProgrammatically = true;
        
        if (e.SourcePageType == typeof(VideoBeast.Pages.SettingsPage))
        {
            // Navigated to Settings - selection is already correct
        }
        else if (e.SourcePageType == typeof(VideoBeast.Pages.FolderVideosPage))
        {
            // Navigated to Folder Videos - find and select the playlist item
            var playlistItem = Shell.Navigation.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => i.Tag as string == PlaylistTag);
            if (playlistItem != null)
                Shell.Navigation.SelectedItem = playlistItem;
        }
        else if (e.SourcePageType == typeof(VideoBeast.Pages.PlaylistsPage))
        {
            // Navigated to Playlists - find and select the playlists item
            var playlistsItem = Shell.Navigation.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => i.Tag as string == PlaylistsTag);
            if (playlistsItem != null)
                Shell.Navigation.SelectedItem = playlistsItem;
        }
        else if (e.SourcePageType == typeof(VideoBeast.Pages.PlaylistDetailsPage))
        {
            // Navigated to Playlist Details - keep playlists item selected
            var playlistsItem = Shell.Navigation.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => i.Tag as string == PlaylistsTag);
            if (playlistsItem != null)
                Shell.Navigation.SelectedItem = playlistsItem;
        }
        else
        {
            // Navigated to PlayerPage or other - clear settings selection
            Shell.Navigation.SelectedItem = null;
        }
        
        // Use DispatcherQueue to reset the flag after all SelectionChanged events have processed
        DispatcherQueue.TryEnqueue(() => _isNavigatingProgrammatically = false);

        _playback.HandleFrameNavigated(
            e: e,
            settings: _playerSettings,
            currentFolderText: _folderService.LibraryFolder?.Path ?? "No folder selected",
            isLibraryMissing: _folderService.LibraryFolder is null);
    }

    private async Task ChooseFolderAsync()
    {
        var folder = await _folderService.PickAndStoreLibraryFolderAsync(this);
        if (folder is null) return;

        _selectedFile = null;

        await RebuildNavMenuAsync();

        _playback.UpdatePlayerPageUi(
            settings: _playerSettings,
            currentFolderText: folder.Path,
            isLibraryMissing: false);

        if (Shell.Frame.Content is VideoBeast.Pages.PlayerPage player)
            player.SetCurrentFolderText(folder.Path);

        if (Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage playlist)
            await playlist.LoadFolderAsync(folder);

        _search.Reset();
    }

    private async Task RefreshAsync()
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        await RebuildNavMenuAsync();

        if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage playlist)
            await playlist.LoadFolderAsync(_folderService.SelectedFolder);

        _search.Reset();
    }

    private async Task ImportAsync()
    {
        var destination = await _guard.RequireDestinationAsync();
        if (destination is null) return;

        var result = await _importService.ImportWithPickerAsync(this,destination);

        if (result.ImportedCount > 0)
        {
            await RebuildNavMenuAsync();

            _playback.UpdatePlayerPageUi(
                settings: _playerSettings,
                currentFolderText: _folderService.LibraryFolder?.Path ?? "No folder selected",
                isLibraryMissing: false);

            if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage playlist)
                await playlist.LoadFolderAsync(_folderService.SelectedFolder);
        }

        _search.Reset();
    }

    private async Task DeleteSelectedAsync()
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        if (_selectedFile is null)
            return;

        await _fileActions.DeleteFileWithConfirmAsync(_selectedFile);
        _search.Reset();

        if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage playlist)
            await playlist.LoadFolderAsync(_folderService.SelectedFolder);
    }

    private async Task OpenFolderAsync()
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        await Launcher.LaunchFolderAsync(library);
    }

    private void RootGrid_DragOver(object sender,DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Import MP4 videos";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void RootGrid_Drop(object sender,DragEventArgs e)
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        var result = await _importService.ImportFromDropAsync(e.DataView,library);

        if (result.ImportedCount > 0)
        {
            await RebuildNavMenuAsync();

            _playback.UpdatePlayerPageUi(
                settings: _playerSettings,
                currentFolderText: _folderService.LibraryFolder?.Path ?? "No folder selected",
                isLibraryMissing: false);

            if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.FolderVideosPage playlist)
                await playlist.LoadFolderAsync(_folderService.SelectedFolder);
        }

        _search.Reset();
    }

    private MenuFlyout CreateFileContextFlyout(StorageFile file,NavigationViewItem owningItem)
    {
        var flyout = new MenuFlyout();

        var play = new MenuFlyoutItem { Text = "Play",Icon = new SymbolIcon(Symbol.Play) };
        play.Click += async (_,__) =>
        {
            SelectFileFromContext(file,owningItem);
            _search.Reset();
            await _playback.RequestPlayAsync(file,_playerSettings);
        };

        // Add to Playlist menu item (opens dialog)
        var addToPlaylist = new MenuFlyoutItem
        {
            Text = "Add to Playlist",
            Icon = new SymbolIcon(Symbol.Add)
        };
        addToPlaylist.Click += async (_, __) =>
        {
            _contextMenuTargetFile = file;
            await ShowAddToPlaylistDialogAsync();
        };

        var showInFolder = new MenuFlyoutItem { Text = "Show in folder",Icon = new SymbolIcon(Symbol.Find) };
        showInFolder.Click += async (_,__) =>
        {
            SelectFileFromContext(file,owningItem);
            _search.Reset();
            await _fileActions.ShowInExplorerAsync(file);
        };

        var copyPath = new MenuFlyoutItem { Text = "Copy path",Icon = new SymbolIcon(Symbol.Copy) };
        copyPath.Click += (_,__) =>
        {
            SelectFileFromContext(file,owningItem);
            _search.Reset();
            _fileActions.CopyPathToClipboard(file);
        };

        var rename = new MenuFlyoutItem { Text = "Rename",Icon = new SymbolIcon(Symbol.Edit) };
        rename.Click += async (_,__) =>
        {
            SelectFileFromContext(file,owningItem);
            _search.Reset();
            await _fileActions.RenameFileWithDialogAsync(file);
        };

        var delete = new MenuFlyoutItem { Text = "Delete",Icon = new SymbolIcon(Symbol.Delete) };
        delete.Click += async (_,__) =>
        {
            SelectFileFromContext(file,owningItem);
            _search.Reset();
            await _fileActions.DeleteFileWithConfirmAsync(file);
        };

        flyout.Items.Add(play);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(addToPlaylist);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(showInFolder);
        flyout.Items.Add(copyPath);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(rename);
        flyout.Items.Add(delete);

        return flyout;
    }

    private async Task ShowAddToPlaylistDialogAsync()
    {
        if (_contextMenuTargetFile == null) return;

        var store = new PlaylistStore();
        var playlists = await store.GetAllAsync();

        // Create dialog
        var dialog = new ContentDialog
        {
            Title = "Add to Playlist",
            PrimaryButtonText = "Add",
            SecondaryButtonText = "New Playlist",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        // Create ListView for playlists
        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinHeight = 200,
            MaxHeight = 400
        };

        foreach (var playlist in playlists)
        {
            listView.Items.Add(new ListViewItem
            {
                Content = $"{playlist.Name} ({playlist.Items.Count} videos)",
                Tag = playlist.Id
            });
        }

        // Select first item by default if any exist
        if (listView.Items.Count > 0)
            listView.SelectedIndex = 0;

        // Add empty state message if no playlists
        if (playlists.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "No playlists yet. Click 'New Playlist' to create one.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dialog.Content = emptyText;
            dialog.IsPrimaryButtonEnabled = false;
        }
        else
        {
            dialog.Content = listView;
        }

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && listView.SelectedItem is ListViewItem selectedItem)
        {
            // Add to selected playlist
            if (selectedItem.Tag is Guid playlistId)
            {
                try
                {
                    await store.AddItemsAsync(playlistId, new[] { _contextMenuTargetFile });
                    
                    var targetPlaylist = playlists.FirstOrDefault(p => p.Id == playlistId);
                    var successDialog = new ContentDialog
                    {
                        Title = "Video Added",
                        Content = $"Added '{_contextMenuTargetFile.DisplayName}' to playlist '{targetPlaylist?.Name ?? "Unknown"}'.",
                        CloseButtonText = "OK",
                        XamlRoot = RootGrid.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Error",
                        Content = $"Failed to add video to playlist: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = RootGrid.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            // Create new playlist
            await AddToNewPlaylist_Click(null!, null!);
        }
    }

    private async Task AddToNewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetFile == null) return;

        // Show dialog to get playlist name
        var dialog = new ContentDialog
        {
            Title = "New Playlist",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
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
                await store.AddItemsAsync(playlist.Id, new[] { _contextMenuTargetFile });
                
                // Show success message
                var successDialog = new ContentDialog
                {
                    Title = "Playlist Created",
                    Content = $"Created playlist '{playlist.Name}' and added '{_contextMenuTargetFile.DisplayName}'.",
                    CloseButtonText = "OK",
                    XamlRoot = RootGrid.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to create playlist: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = RootGrid.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }
    }

    private void SelectFileFromContext(StorageFile file,NavigationViewItem item)
    {
        Shell.Navigation.SelectedItem = item;
        _selectedFile = file;
    }

    private void SetWindowIcon(IntPtr hwnd)
    {
        try
        {
            // Determine base directory for both packaged and unpackaged scenarios
            string baseDirectory;
            try
            {
                // Try packaged app path first
                baseDirectory = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            }
            catch
            {
                // Fall back to unpackaged app path
                baseDirectory = AppContext.BaseDirectory;
            }

            // Construct path to the .ico file
            var iconPath = System.IO.Path.Combine(baseDirectory, "Assets", "Video-Beast.ico");

            if (System.IO.File.Exists(iconPath))
            {
                // WinUI 3 uses AppWindow.SetIcon for setting window icons
                _appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Icon setting is optional, silently fail if not available
        }
    }
}
