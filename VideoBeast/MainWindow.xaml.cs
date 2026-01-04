// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Text;
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

    // Track what is currently playing so context-menu delete/rename doesn't stop unrelated playback
    private string? _playingFilePath;

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

                // Optional: hide window border/title bar
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
                    // Restore Nav first
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
    // Public API for SettingsPage / PlayerPage
    // ---------------------------
    public PlayerSettings GetPlayerSettings() => _playerSettings;

    // ✅ Save-only (NO ApplySettings call)
    public void SavePlayerSettings(PlayerSettings settings)
    {
        _playerSettings = (settings ?? new PlayerSettings()).Clone();
        PlayerSettingsStore.Save(_playerSettings);
    }

    // ✅ Save + Apply
    public void SaveAndApplyPlayerSettings(PlayerSettings settings)
    {
        SavePlayerSettings(settings);

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
        var item = new NavigationViewItem
        {
            Content = file.Name,
            Icon = new SymbolIcon(Symbol.Video),
            Tag = new NodeContent(file.Name,Symbol.Video,null,file)
        };

        // ✅ Context menu (right-click / touch-and-hold)
        item.ContextFlyout = CreateFileContextFlyout(file);

        // ✅ Make right-click also "select" the item logically (and visually)
        item.RightTapped += (_,__) =>
        {
            NavView.SelectedItem = item;
            _selectedFile = file;
            _searchSelectedFile = null;
        };

        // Optional: keyboard context menu key (Shift+F10)
        item.KeyDown += (_,e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Application ||
                (e.Key == Windows.System.VirtualKey.F10 && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))))
            {
                NavView.SelectedItem = item;
                _selectedFile = file;
                _searchSelectedFile = null;
            }
        };

        return item;
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
    // NavigationView: lazy-load on expand
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

            _playingFilePath = file.Path;
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
                _playingFilePath = _pendingPlayFile.Path;
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

        var folder = await picker.PickSingleFolderAsync();
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

        await DeleteFileWithConfirmAsync(_selectedFile);
    }

    private async void OpenFolder_Click()
    {
        if (!await EnsureLibraryFolderAsync()) return;
        await Launcher.LaunchFolderAsync(_libraryFolder!);
    }

    // ---------------------------
    // Context menu + file operations
    // ---------------------------
    private MenuFlyout CreateFileContextFlyout(StorageFile file)
    {
        var flyout = new MenuFlyout();

        var openContaining = new MenuFlyoutItem
        {
            Text = "Open containing folder",
            Icon = new SymbolIcon(Symbol.Folder)
        };
        openContaining.Click += async (_,__) =>
        {
            SelectFileFromContext(file);
            await OpenContainingFolderAsync(file);
        };

        var copyPath = new MenuFlyoutItem
        {
            Text = "Copy path",
            Icon = new SymbolIcon(Symbol.Copy)
        };
        copyPath.Click += (_,__) =>
        {
            SelectFileFromContext(file);
            CopyPathToClipboard(file);
        };

        var rename = new MenuFlyoutItem
        {
            Text = "Rename",
            Icon = new SymbolIcon(Symbol.Edit)
        };
        rename.Click += async (_,__) =>
        {
            SelectFileFromContext(file);
            await RenameFileWithDialogAsync(file);
        };

        var delete = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new SymbolIcon(Symbol.Delete)
        };
        delete.Click += async (_,__) =>
        {
            SelectFileFromContext(file);
            await DeleteFileWithConfirmAsync(file);
        };

        flyout.Items.Add(openContaining);
        flyout.Items.Add(copyPath);
        flyout.Items.Add(rename);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);

        return flyout;
    }

    private void SelectFileFromContext(StorageFile file)
    {
        _selectedFile = file;
        _searchSelectedFile = null;
    }

    private async Task OpenContainingFolderAsync(StorageFile file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file.Path);
            if (string.IsNullOrWhiteSpace(dir))
            {
                ShowStatus("Could not resolve the containing folder.",InfoBarSeverity.Warning);
                return;
            }

            var folder = await StorageFolder.GetFolderFromPathAsync(dir);
            await Launcher.LaunchFolderAsync(folder);
        }
        catch
        {
            ShowStatus("Could not open containing folder (access denied).",InfoBarSeverity.Error);
        }
    }

    private void CopyPathToClipboard(StorageFile file)
    {
        try
        {
            var dp = new DataPackage();
            dp.SetText(file.Path);

            Clipboard.SetContent(dp);
            Clipboard.Flush();

            ShowStatus("Path copied to clipboard.");
        }
        catch
        {
            ShowStatus("Failed to copy path to clipboard.",InfoBarSeverity.Error);
        }
    }

    private async Task RenameFileWithDialogAsync(StorageFile file)
    {
        if (!await EnsureLibraryFolderAsync()) return;

        string oldPath = file.Path;
        string ext = file.FileType; // ".mp4"

        // If the file is playing, stop first to avoid file lock issues
        if (string.Equals(_playingFilePath,oldPath,StringComparison.OrdinalIgnoreCase)
            && RootFrame.Content is VideoBeast.Pages.PlayerPage pagePlaying)
        {
            pagePlaying.StopAndShowEmpty();
            _playingFilePath = null;
            await Task.Yield();
        }

        var baseName = await PromptRenameBaseNameAsync(file,ext);
        if (baseName is null)
            return;

        // If they included extension, strip it
        if (!string.IsNullOrEmpty(ext) && baseName.EndsWith(ext,StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^ext.Length];

        if (string.IsNullOrWhiteSpace(baseName))
        {
            ShowStatus("Rename canceled (empty name).",InfoBarSeverity.Warning);
            return;
        }

        if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowStatus("Invalid file name characters.",InfoBarSeverity.Warning);
            return;
        }

        string newName = baseName + ext;

        try
        {
            await file.RenameAsync(newName,NameCollisionOption.FailIfExists);

            // Update selected reference if we renamed the selected file
            if (_selectedFile?.Path == oldPath)
                _selectedFile = file;

            await RebuildNavMenuAsync();
            ShowStatus("Renamed file.");
        }
        catch
        {
            ShowStatus("Rename failed (name may already exist or file is locked).",InfoBarSeverity.Error);
        }
    }

    private async Task<string?> PromptRenameBaseNameAsync(StorageFile file,string ext)
    {
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
            XamlRoot = RootGrid.XamlRoot,
            Title = "Rename file",
            Content = content,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        return tb.Text?.Trim();
    }

    private async Task DeleteFileWithConfirmAsync(StorageFile file)
    {
        if (!await EnsureLibraryFolderAsync()) return;

        bool ok = await ConfirmDeleteAsync(file);
        if (!ok)
        {
            ShowStatus("Delete canceled.");
            return;
        }

        string targetPath = file.Path;

        // If deleting the currently playing file, stop first to release file handle
        if (string.Equals(_playingFilePath,targetPath,StringComparison.OrdinalIgnoreCase)
            && RootFrame.Content is VideoBeast.Pages.PlayerPage pagePlaying)
        {
            pagePlaying.StopAndShowEmpty();
            _playingFilePath = null;
            await Task.Yield();
        }

        try
        {
            await file.DeleteAsync(StorageDeleteOption.Default);

            if (_selectedFile?.Path == targetPath)
                _selectedFile = null;

            await RebuildNavMenuAsync();
            ShowStatus("Deleted file.");
        }
        catch
        {
            ShowStatus("Delete failed (file may be locked).",InfoBarSeverity.Error);
        }
    }

    private async Task<bool> ConfirmDeleteAsync(StorageFile file)
    {
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
            XamlRoot = RootGrid.XamlRoot,
            Title = "Confirm delete",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
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
        foreach (var f in files)
        {
            await f.CopyAsync(_libraryFolder!,f.Name,NameCollisionOption.GenerateUniqueName);
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
