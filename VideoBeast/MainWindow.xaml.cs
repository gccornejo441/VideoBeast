// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;

using WinRT.Interop;

namespace VideoBeast;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private const string LibraryFolderToken = "LibraryFolderToken";
    private const string PlaceholderTag = "__placeholder__";

    private StorageFolder? _libraryFolder;
    private StorageFolder? _selectedFolder;
    private StorageFile? _selectedFile;

    // Search (AutoSuggestBox)
    private StorageFile? _searchSelectedFile;

    // Settings (player)
    private PlayerSettings _playerSettings = new();

    // For navigation -> play after PlayerPage loads
    private StorageFile? _pendingPlayFile;

    // AppWindow (window chrome)
    private readonly AppWindow _appWindow;

    // "YouTube fullscreen" (player fullscreen) state (NOT AppWindow fullscreen presenter)
    private bool _playerFullscreen;

    // Saved UI chrome state so we can restore perfectly
    private bool _prevExtendsContentIntoTitleBar;
    private bool _prevNavIsPaneVisible;
    private bool _prevNavIsPaneOpen;
    private bool _prevNavIsSettingsVisible;
    private NavigationViewPaneDisplayMode _prevNavPaneDisplayMode;

    private double _prevNavCompactPaneLength;
    private double _prevNavOpenPaneLength;

    private Visibility _prevTitleBarVisibility;
    private Visibility _prevStatusBarVisibility;
    private bool _prevStatusBarIsOpen;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(1200,740));

        // Use custom TitleBar initially
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        _playerSettings = PlayerSettingsStore.Load();

        // start app
        _ = InitializeLibraryAsync();
        RootFrame.Navigate(typeof(VideoBeast.Pages.PlayerPage));
    }

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

        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
            page.ApplySettings(_playerSettings);

        ShowStatus("Player stretch saved.");
    }

    // ---------------------------
    // YouTube-style fullscreen (player fullscreen)
    // - hides chrome (TitleBar, StatusBar, Nav pane/settings)
    // - DOES NOT change AppWindow presenter to FullScreen
    // ---------------------------
    public bool IsPlayerFullscreen => _playerFullscreen;

    public void SetPlayerFullscreen(bool on)
    {
        if (_playerFullscreen == on) return;
        _playerFullscreen = on;

        try
        {
            if (on)
            {
                // Save current chrome state
                _prevExtendsContentIntoTitleBar = ExtendsContentIntoTitleBar;

                _prevTitleBarVisibility = titleBar.Visibility;

                _prevStatusBarVisibility = StatusBar.Visibility;
                _prevStatusBarIsOpen = StatusBar.IsOpen;

                _prevNavIsPaneVisible = NavView.IsPaneVisible;
                _prevNavIsPaneOpen = NavView.IsPaneOpen;
                _prevNavIsSettingsVisible = NavView.IsSettingsVisible;
                _prevNavPaneDisplayMode = NavView.PaneDisplayMode;

                _prevNavCompactPaneLength = NavView.CompactPaneLength;
                _prevNavOpenPaneLength = NavView.OpenPaneLength;

                // Hide chrome
                StatusBar.IsOpen = false;
                StatusBar.Visibility = Visibility.Collapsed;

                titleBar.Visibility = Visibility.Collapsed;

                // Keep content extending to the top, but remove draggable region
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(null);

                // Optional (recommended for “YouTube feel”): hide window border/title bar
                // Still an overlapped window (NOT fullscreen presenter).
                if (_appWindow.Presenter is OverlappedPresenter op)
                {
                    op.SetBorderAndTitleBar(false,false);
                }

                // Apply Nav collapse AFTER a layout tick to avoid the “left strip” glitch
                DispatcherQueue.TryEnqueue(() =>
                {
                    NavView.IsSettingsVisible = false;
                    NavView.IsPaneOpen = false;

                    // IMPORTANT: do NOT use LeftMinimal in this mode (it reserves the compact strip)
                    NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;

                    // Kill any reserved pane space completely
                    NavView.CompactPaneLength = 0;
                    NavView.OpenPaneLength = 0;

                    // Set LAST so it doesn’t re-reserve space
                    NavView.IsPaneVisible = false;

                    NavView.UpdateLayout();
                });
            }
            else
            {
                // Restore window chrome (border/titlebar) if we hid it
                if (_appWindow.Presenter is OverlappedPresenter op)
                {
                    op.SetBorderAndTitleBar(true,true);
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    // Restore Nav first (so layout is correct before bringing chrome back)
                    NavView.PaneDisplayMode = _prevNavPaneDisplayMode;

                    NavView.CompactPaneLength = _prevNavCompactPaneLength;
                    NavView.OpenPaneLength = _prevNavOpenPaneLength;

                    NavView.IsPaneVisible = _prevNavIsPaneVisible;
                    NavView.IsPaneOpen = _prevNavIsPaneOpen;
                    NavView.IsSettingsVisible = _prevNavIsSettingsVisible;

                    // Restore TitleBar + StatusBar
                    titleBar.Visibility = _prevTitleBarVisibility;

                    StatusBar.Visibility = _prevStatusBarVisibility;
                    StatusBar.IsOpen = _prevStatusBarIsOpen;

                    ExtendsContentIntoTitleBar = _prevExtendsContentIntoTitleBar;
                    if (ExtendsContentIntoTitleBar)
                        SetTitleBar(titleBar);

                    NavView.UpdateLayout();
                });
            }
        }
        catch
        {
            // Don’t crash if window/chrome toggling fails
        }
    }

    public void TogglePlayerFullscreen() => SetPlayerFullscreen(!IsPlayerFullscreen);

    // ---------------------------
    // Public API for SettingsPage
    // ---------------------------
    public PlayerSettings GetPlayerSettings() => _playerSettings;

    public void SaveAndApplyPlayerSettings(PlayerSettings settings)
    {
        _playerSettings = settings;
        PlayerSettingsStore.Save(settings);

        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
            page.ApplySettings(_playerSettings);

        ShowStatus("Player settings saved.");
    }

    // ---------------------------
    // TitleBar actions
    // ---------------------------
    private void TitleBar_PaneToggleRequested(TitleBar sender,object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender,object args)
    {
        if (RootFrame.CanGoBack)
            RootFrame.GoBack();
    }

    // ---------------------------
    // Startup / library restore
    // ---------------------------
    private async Task InitializeLibraryAsync()
    {
        _libraryFolder = await TryGetLibraryFolderAsync();
        _selectedFolder = _libraryFolder;

        await RebuildNavMenuAsync();

        // Update PlayerPage UI
        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
        {
            page.SetCurrentFolderText(_libraryFolder?.Path ?? "No folder selected");
            page.ShowEmptyState(_libraryFolder is null);
            page.ApplySettings(_playerSettings);
        }
    }

    // ---------------------------
    // NavigationView: menu building
    // ---------------------------
    private async Task RebuildNavMenuAsync()
    {
        NavView.MenuItems.Clear();

        // Library header
        NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "Library" });

        if (_libraryFolder is null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = "No folder selected",
                IsEnabled = false,
                Icon = new SymbolIcon(Symbol.Folder)
            });
        }
        else
        {
            var root = CreateFolderNavItem(_libraryFolder);
            root.IsExpanded = true;
            await LoadFolderChildrenIntoNavItemAsync(_libraryFolder,root);
            NavView.MenuItems.Add(root);
        }

        // Separator
        NavView.MenuItems.Add(new NavigationViewItemSeparator());

        // Actions header
        NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "Actions" });

        NavView.MenuItems.Add(CreateActionItem("Choose folder",Symbol.Folder,"action:chooseFolder"));
        NavView.MenuItems.Add(CreateActionItem("Import MP4",Symbol.Add,"action:import"));
        NavView.MenuItems.Add(CreateActionItem("Refresh",Symbol.Refresh,"action:refresh"));
        NavView.MenuItems.Add(CreateActionItem("Delete selected",Symbol.Delete,"action:delete"));
        NavView.MenuItems.Add(CreateActionItem("Open folder",Symbol.OpenFile,"action:openFolder"));
    }

    private NavigationViewItem CreateActionItem(string label,Symbol icon,string tag)
    {
        return new NavigationViewItem
        {
            Content = label,
            Icon = new SymbolIcon(icon),
            Tag = tag,
            SelectsOnInvoked = false
        };
    }

    private NavigationViewItem CreateFolderNavItem(StorageFolder folder)
    {
        var item = new NavigationViewItem
        {
            Content = folder.Name,
            Icon = new SymbolIcon(Symbol.Folder),
            Tag = new NodeContent(folder.Name,Symbol.Folder,folder,null)
        };

        // Placeholder for chevron + lazy load
        item.MenuItems.Add(new NavigationViewItem
        {
            Content = "Loading…",
            IsEnabled = false,
            Tag = PlaceholderTag
        });

        return item;
    }

    private NavigationViewItem CreateFileNavItem(StorageFile file)
    {
        return new NavigationViewItem
        {
            Content = file.Name,
            Icon = new SymbolIcon(Symbol.Video),
            Tag = new NodeContent(file.Name,Symbol.Video,null,file)
        };
    }

    private async Task LoadFolderChildrenIntoNavItemAsync(StorageFolder folder,NavigationViewItem folderItem)
    {
        folderItem.MenuItems.Clear();

        IReadOnlyList<StorageFolder> folders;
        IReadOnlyList<StorageFile> files;

        try
        {
            folders = await folder.GetFoldersAsync();
            files = await folder.GetFilesAsync();
        }
        catch
        {
            folderItem.MenuItems.Add(new NavigationViewItem
            {
                Content = "Access denied",
                IsEnabled = false
            });
            return;
        }

        foreach (var sub in folders.OrderBy(f => f.Name))
            folderItem.MenuItems.Add(CreateFolderNavItem(sub));

        foreach (var file in files
            .Where(f => string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name))
        {
            folderItem.MenuItems.Add(CreateFileNavItem(file));
        }

        if (folderItem.MenuItems.Count == 0)
        {
            folderItem.MenuItems.Add(new NavigationViewItem
            {
                Content = "(empty)",
                IsEnabled = false
            });
        }
    }

    // ---------------------------
    // NavigationView: lazy-load on expand (prevents WinRT invalid vector subscript)
    // ---------------------------
    private async void NavView_Expanding(NavigationView sender,NavigationViewItemExpandingEventArgs args)
    {
        if (args.ExpandingItemContainer is not NavigationViewItem nvi)
            return;

        if (nvi.Tag is not NodeContent c || c.Folder is null)
            return;

        // lazy-load if placeholder present
        if (nvi.MenuItems.Count == 1
            && nvi.MenuItems[0] is NavigationViewItem ph
            && ph.Tag is string s
            && s == PlaceholderTag)
        {
            await LoadFolderChildrenIntoNavItemAsync(c.Folder,nvi);
        }
    }

    // ---------------------------
    // NavigationView: invoked + settings
    // ---------------------------
    private void NavView_SelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            RootFrame.Navigate(typeof(VideoBeast.Pages.SettingsPage));
        }
    }

    private async void NavView_ItemInvoked(NavigationView sender,NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            RootFrame.Navigate(typeof(VideoBeast.Pages.SettingsPage));
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem nvi)
            return;

        // Action items
        if (nvi.Tag is string tagStr && tagStr.StartsWith("action:",StringComparison.Ordinal))
        {
            switch (tagStr)
            {
                case "action:chooseFolder":
                    ChooseFolder_Click();
                    break;
                case "action:import":
                    Import_Click();
                    break;
                case "action:refresh":
                    Refresh_Click();
                    break;
                case "action:delete":
                    Remove_Click();
                    break;
                case "action:openFolder":
                    OpenFolder_Click();
                    break;
            }
            return;
        }

        // Folder/file items
        if (nvi.Tag is NodeContent c)
        {
            if (c.File is not null)
            {
                _selectedFile = c.File;
                _searchSelectedFile = null;

                await PlayFileAsync(c.File);
                return;
            }

            if (c.Folder is not null)
            {
                _selectedFolder = c.Folder;

                if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
                    page.SetCurrentFolderText(c.Folder.Path);

                ShowStatus($"Selected folder: {c.Folder.Path}");
            }
        }
    }

    private async Task PlayFileAsync(StorageFile file)
    {
        _pendingPlayFile = file;

        // Ensure we’re on PlayerPage
        if (RootFrame.CurrentSourcePageType != typeof(VideoBeast.Pages.PlayerPage))
            RootFrame.Navigate(typeof(VideoBeast.Pages.PlayerPage));

        // If already there, play immediately
        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
        {
            page.ApplySettings(_playerSettings);
            page.Play(file,_playerSettings);
            _pendingPlayFile = null;
        }
    }

    private void RootFrame_Navigated(object sender,Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
        {
            page.ApplySettings(_playerSettings);
            page.SetCurrentFolderText(_libraryFolder?.Path ?? "No folder selected");

            if (_pendingPlayFile is not null)
            {
                page.Play(_pendingPlayFile,_playerSettings);
                _pendingPlayFile = null;
            }
            else
            {
                page.ShowEmptyState(_libraryFolder is null);
            }
        }
    }

    // ---------------------------
    // Search (Top AutoSuggestBox)
    // ---------------------------
    private async void TopSearchBox_TextChanged(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _searchSelectedFile = null;

        string q = sender.Text?.Trim() ?? "";
        if (q.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        if (_libraryFolder is null)
            return;

        StorageFolder scope = _selectedFolder ?? _libraryFolder;

        IReadOnlyList<StorageFile> files;
        try
        {
            files = await scope.GetFilesAsync();
        }
        catch
        {
            sender.ItemsSource = null;
            return;
        }

        var matches = files
            .Where(f => string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Name.Contains(q,StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name)
            .Take(10)
            .Select(f => new SearchSuggestion(f.Name,f))
            .ToList();

        sender.ItemsSource = matches;
    }

    private void TopSearchBox_SuggestionChosen(AutoSuggestBox sender,AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchSuggestion s)
        {
            _searchSelectedFile = s.File;
            sender.Text = s.DisplayText;
        }
    }

    private async void TopSearchBox_QuerySubmitted(AutoSuggestBox sender,AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        StorageFile? file = _searchSelectedFile;

        if (file is null && args.ChosenSuggestion is SearchSuggestion chosen)
            file = chosen.File;

        if (file is null)
            return;

        _selectedFile = file;
        await PlayFileAsync(file);
    }

    private sealed class SearchSuggestion
    {
        public string DisplayText { get; }
        public StorageFile File { get; }

        public SearchSuggestion(string displayText,StorageFile file)
        {
            DisplayText = displayText;
            File = file;
        }

        public override string ToString() => DisplayText;
    }

    // ---------------------------
    // FolderPicker + FutureAccessList
    // ---------------------------
    private async Task<StorageFolder?> TryGetLibraryFolderAsync()
    {
        try
        {
            return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(LibraryFolderToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<StorageFolder?> PickAndStoreLibraryFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker,hwnd);

        StorageFolder folder = await picker.PickSingleFolderAsync();
        if (folder is null) return null;

        StorageApplicationPermissions.FutureAccessList.AddOrReplace(LibraryFolderToken,folder);
        return folder;
    }

    private async Task<bool> EnsureLibraryFolderAsync()
    {
        _libraryFolder ??= await TryGetLibraryFolderAsync();
        if (_selectedFolder is null) _selectedFolder = _libraryFolder;

        if (_libraryFolder is null)
        {
            if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
            {
                page.SetCurrentFolderText("No folder selected");
                page.ShowEmptyState(true);
            }

            ShowStatus("Choose a library folder first.",InfoBarSeverity.Warning);
            return false;
        }

        return true;
    }

    // ---------------------------
    // Actions
    // ---------------------------
    private async void ChooseFolder_Click()
    {
        var folder = await PickAndStoreLibraryFolderAsync();
        if (folder is null) return;

        _libraryFolder = folder;
        _selectedFolder = folder;
        _selectedFile = null;

        await RebuildNavMenuAsync();

        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
        {
            page.SetCurrentFolderText(folder.Path);
            page.ShowEmptyState(false);
            page.ApplySettings(_playerSettings);
        }

        ShowStatus("Library folder saved.");
    }

    private async void Refresh_Click()
    {
        if (!await EnsureLibraryFolderAsync()) return;
        await RebuildNavMenuAsync();
        ShowStatus("Library refreshed.");
    }

    private async void Import_Click()
    {
        if (!await EnsureLibraryFolderAsync()) return;

        StorageFolder destination = _selectedFolder ?? _libraryFolder!;

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".mp4");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker,hwnd);

        var picked = await picker.PickMultipleFilesAsync();
        if (picked is null || picked.Count == 0) return;

        int imported = 0;
        foreach (var f in picked)
        {
            if (!string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
                continue;

            await f.CopyAsync(destination,f.Name,NameCollisionOption.GenerateUniqueName);
            imported++;
        }

        await RebuildNavMenuAsync();

        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
            page.ShowEmptyState(false);

        ShowStatus($"Imported {imported} video(s).");
    }

    private async void Remove_Click()
    {
        if (!await EnsureLibraryFolderAsync()) return;

        if (_selectedFile is null)
        {
            ShowStatus("Select an MP4 to delete.",InfoBarSeverity.Warning);
            return;
        }

        try
        {
            await _selectedFile.DeleteAsync();
            _selectedFile = null;

            if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
                page.StopAndShowEmpty();

            await RebuildNavMenuAsync();
            ShowStatus("Deleted file.");
        }
        catch
        {
            ShowStatus("Delete failed (file may be locked).",InfoBarSeverity.Error);
        }
    }

    private async void OpenFolder_Click()
    {
        if (!await EnsureLibraryFolderAsync()) return;
        await Launcher.LaunchFolderAsync(_libraryFolder!);
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
        if (!await EnsureLibraryFolderAsync()) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var files = items.OfType<StorageFile>()
            .Where(f => string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            ShowStatus("Drop .mp4 files only.",InfoBarSeverity.Warning);
            return;
        }

        int imported = 0;
        foreach (var file in files)
        {
            await file.CopyAsync(_libraryFolder!,file.Name,NameCollisionOption.GenerateUniqueName);
            imported++;
        }

        await RebuildNavMenuAsync();

        if (RootFrame.Content is VideoBeast.Pages.PlayerPage page)
            page.ShowEmptyState(false);

        ShowStatus($"Imported {imported} video(s) via drag and drop.");
    }

    // ---------------------------
    // UI helper
    // ---------------------------
    private void ShowStatus(string message,InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private sealed class NodeContent
    {
        public string Name { get; }
        public Symbol Icon { get; }
        public StorageFolder? Folder { get; }
        public StorageFile? File { get; }

        public NodeContent(string name,Symbol icon,StorageFolder? folder,StorageFile? file)
        {
            Name = name;
            Icon = icon;
            Folder = folder;
            File = file;
        }

        public override string ToString() => Name;
    }
}
