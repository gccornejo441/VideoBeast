// MainWindow.xaml.cs
using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

using VideoBeast.Navigation;
using VideoBeast.Services;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

using WinRT.Interop;

namespace VideoBeast;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    // Library folder state
    private readonly LibraryFolderService _folderService = new();

    // Import
    private readonly LibraryImportService _importService = new();

    // Playback orchestration
    private readonly PlaybackCoordinator _playback;

    // Status
    private readonly StatusService _status;

    // Guard
    private readonly LibraryGuardService _guard;

    // Search
    private readonly SearchCoordinator _search;

    // Action routing
    private readonly ActionRouter _actions;

    private StorageFile? _selectedFile;

    // Settings (player)
    private PlayerSettings _playerSettings = new();

    // AppWindow (window chrome)
    private readonly AppWindow _appWindow;

    // Navigation builder
    private readonly LibraryTreeBuilder _navTreeBuilder;

    // File actions
    private readonly LibraryFileActions _fileActions;

    // Chrome / fullscreen behavior
    private readonly WindowChromeService _chrome;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        _playback = new PlaybackCoordinator(() => Shell.Frame);
        _status = new StatusService(() => Shell.Status);
        _guard = new LibraryGuardService(_folderService,_status,() => Shell.Frame);

        var searchService = new LibrarySearchService();
        _search = new SearchCoordinator(
            getLibraryFolder: () => _folderService.LibraryFolder,
            getSelectedFolder: () => _folderService.SelectedFolder,
            getPlayerSettings: () => _playerSettings,
            onFileChosen: file => _selectedFile = file,
            playback: _playback,
            searchService: searchService);

        // Router setup
        _actions = new ActionRouter(onError: (_,__) => _status.Show("Action failed.",InfoBarSeverity.Error));
        _actions.Register("action:chooseFolder",ChooseFolderAsync);
        _actions.Register("action:import",ImportAsync);
        _actions.Register("action:refresh",RefreshAsync);
        _actions.Register("action:delete",DeleteSelectedAsync);
        _actions.Register("action:openFolder",OpenFolderAsync);

        // AppWindow
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1200,740));

        // Chrome Service
        _chrome = new WindowChromeService(
            appWindow: _appWindow,
            dispatcherQueue: DispatcherQueue,
            getExtendsContentIntoTitleBar: () => ExtendsContentIntoTitleBar,
            setExtendsContentIntoTitleBar: v => ExtendsContentIntoTitleBar = v,
            setTitleBar: ui => SetTitleBar(ui),
            getTitleBarElement: () => Shell.TitleBar.TitleBarControl,
            getNavView: () => Shell.Navigation,
            getStatusBar: () => Shell.Status
        );

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Shell.TitleBar.TitleBarControl);

        // File actions service
        _fileActions = new LibraryFileActions(
            getXamlRoot: () => RootGrid.XamlRoot,
            ensureLibraryFolderAsync: async () => (await _guard.RequireLibraryAsync()) is not null,
            rebuildNavMenuAsync: RebuildNavMenuAsync,
            stopPlaybackIfPlayingAsync: _playback.StopIfPlayingAsync,
            showStatus: (msg,sev) => _status.Show(msg,sev),
            onDeletedPath: deletedPath =>
            {
                if (_selectedFile?.Path == deletedPath)
                    _selectedFile = null;
            });

        // Navigation builder
        _navTreeBuilder = new LibraryTreeBuilder(
            fileFlyoutFactory: (file,item) => CreateFileContextFlyout(file,item),
            onFileRightTapped: (file,item) =>
            {
                Shell.Navigation.SelectedItem = item;
                _selectedFile = file;
                _search.Reset();
            });

        // Wire shell events
        Shell.Navigation.Expanding += NavView_Expanding;
        Shell.Navigation.ItemInvoked += NavView_ItemInvoked;
        Shell.Navigation.SelectionChanged += NavView_SelectionChanged;

        Shell.Frame.Navigated += RootFrame_Navigated;

        Shell.TitleBar.PaneToggleRequested += TitleBar_PaneToggleRequested;
        Shell.TitleBar.BackRequested += TitleBar_BackRequested;

        // Search events delegated to SearchCoordinator
        Shell.TitleBar.TextChanged += (s,e) => _search.HandleTextChanged(s,e);
        Shell.TitleBar.SuggestionChosen += (s,e) => _search.HandleSuggestionChosen(s,e);
        Shell.TitleBar.QuerySubmitted += (s,e) => _search.HandleQuerySubmitted(s,e);

        // settings load
        _playerSettings = PlayerSettingsStore.Load();

        // start app
        _ = InitializeLibraryAsync();
        Shell.Frame.Navigate(typeof(VideoBeast.Pages.PlayerPage));

        UpdateBackButtonVisibility();
    }

    // still referenced by XAML: Loaded="RootGrid_Loaded"
    private void RootGrid_Loaded(object sender,RoutedEventArgs e)
    {
        // optional: min size, etc.
    }

    // ---------------------------
    // Legacy/compat API (Stretch)
    // ---------------------------
    public static Stretch DefaultPlayerStretch => Stretch.Uniform;
    public static string PlayerStretchSettingKey => "PlayerStretch";

    public void SavePlayerStretch(Stretch stretch)
    {
        _playerSettings.Stretch = stretch;
        PlayerSettingsStore.Save(_playerSettings);

        if (Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
            page.ApplySettings(_playerSettings);

        _status.Show("Player stretch saved.");
    }

    // ---------------------------
    // Fullscreen (delegated)
    // ---------------------------
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

    // ---------------------------
    // Public API for SettingsPage / PlayerPage
    // ---------------------------
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

        _status.Show("Player settings saved.");
    }

    // --------------------------------------------------------
    // NEW: UI delegates for PlayerPage playlist context actions
    // --------------------------------------------------------
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

    // ---------------------------
    // TitleBar actions
    // ---------------------------
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

    // ---------------------------
    // Startup / library restore
    // ---------------------------
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

    // ---------------------------
    // Navigation menu building
    // ---------------------------
    private Task RebuildNavMenuAsync()
        => _navTreeBuilder.RebuildAsync(Shell.Navigation,_folderService.LibraryFolder);

    private async void NavView_Expanding(NavigationView sender,NavigationViewItemExpandingEventArgs args)
    {
        await _navTreeBuilder.HandleExpandingAsync(args);
    }

    // ---------------------------
    // NavigationView: invoked + settings
    // ---------------------------
    private void NavView_SelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Shell.Frame.Navigate(typeof(VideoBeast.Pages.SettingsPage));
            UpdateBackButtonVisibility();
        }
    }

    private async void NavView_ItemInvoked(NavigationView sender,NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            Shell.Frame.Navigate(typeof(VideoBeast.Pages.SettingsPage));
            UpdateBackButtonVisibility();
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem nvi)
            return;

        // Action items (routed)
        if (nvi.Tag is string tagStr && tagStr.StartsWith("action:",StringComparison.Ordinal))
        {
            bool handled = await _actions.TryInvokeAsync(tagStr);
            if (!handled)
                _status.Show($"Unknown action: {tagStr}",InfoBarSeverity.Warning);

            return;
        }

        // Folder/file items
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

                if (Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
                {
                    page.SetCurrentFolderText(c.Folder.Path);

                    // NEW: load playlist panel from the selected folder
                    await page.LoadFolderAsync(c.Folder);
                }

                _status.Show($"Selected folder: {c.Folder.Path}");
                _search.Reset();
            }
        }
    }

    private void RootFrame_Navigated(object sender,Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        UpdateBackButtonVisibility();

        _playback.HandleFrameNavigated(
            e: e,
            settings: _playerSettings,
            currentFolderText: _folderService.LibraryFolder?.Path ?? "No folder selected",
            isLibraryMissing: _folderService.LibraryFolder is null);
    }

    // ---------------------------
    // Routed Actions (Task-based)
    // ---------------------------
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

        // Optional: immediately load playlist for library root
        if (Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
        {
            page.SetCurrentFolderText(folder.Path);
            await page.LoadFolderAsync(folder);
        }

        _status.Show("Library folder saved.");
        _search.Reset();
    }

    private async Task RefreshAsync()
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        await RebuildNavMenuAsync();

        // Optional: reload playlist for selected folder if available
        if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
            await page.LoadFolderAsync(_folderService.SelectedFolder);

        _status.Show("Library refreshed.");
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

            // If a folder is selected, refresh playlist
            if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
                await page.LoadFolderAsync(_folderService.SelectedFolder);
        }

        _status.Show(result.Message,result.Severity);
        _search.Reset();
    }

    private async Task DeleteSelectedAsync()
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        if (_selectedFile is null)
        {
            _status.Show("Select an MP4 to delete.",InfoBarSeverity.Warning);
            return;
        }

        await _fileActions.DeleteFileWithConfirmAsync(_selectedFile);
        _search.Reset();

        // If we deleted from the selected folder, refresh playlist
        if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
            await page.LoadFolderAsync(_folderService.SelectedFolder);
    }

    private async Task OpenFolderAsync()
    {
        var library = await _guard.RequireLibraryAsync();
        if (library is null) return;

        await Launcher.LaunchFolderAsync(library);
    }

    // ---------------------------
    // Drag & Drop import
    // ---------------------------
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

            // Refresh playlist for current folder
            if (_folderService.SelectedFolder is not null && Shell.Frame.Content is VideoBeast.Pages.PlayerPage page)
                await page.LoadFolderAsync(_folderService.SelectedFolder);
        }

        _status.Show(result.Message,result.Severity);
        _search.Reset();
    }

    // ---------------------------
    // Context menu (NavigationView items)
    // ---------------------------
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
        flyout.Items.Add(showInFolder);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(copyPath);
        flyout.Items.Add(rename);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);

        return flyout;
    }

    private void SelectFileFromContext(StorageFile file,NavigationViewItem item)
    {
        Shell.Navigation.SelectedItem = item;
        _selectedFile = file;
    }
}
