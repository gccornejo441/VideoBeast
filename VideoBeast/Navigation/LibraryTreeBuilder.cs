using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

using Windows.Storage;

namespace VideoBeast.Navigation;

public sealed class LibraryTreeBuilder
{
    public const string PlaceholderTag = "__placeholder__";

    private readonly Func<StorageFile,NavigationViewItem,FlyoutBase?>? _fileFlyoutFactory;
    private readonly Action<StorageFile,NavigationViewItem>? _onFileRightTapped;

    public LibraryTreeBuilder(
        Func<StorageFile,NavigationViewItem,FlyoutBase?>? fileFlyoutFactory = null,
        Action<StorageFile,NavigationViewItem>? onFileRightTapped = null)
    {
        _fileFlyoutFactory = fileFlyoutFactory;
        _onFileRightTapped = onFileRightTapped;
    }

    public async Task RebuildAsync(NavigationView nav,StorageFolder? libraryFolder)
    {
        nav.MenuItems.Clear();

        // Library header
        nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Library" });

        if (libraryFolder is null)
        {
            nav.MenuItems.Add(new NavigationViewItem
            {
                Content = "No folder selected",
                IsEnabled = false,
                Icon = new SymbolIcon(Symbol.Folder)
            });
        }
        else
        {
            var root = CreateFolderNavItem(libraryFolder);
            root.IsExpanded = true;

            await LoadFolderChildrenIntoNavItemAsync(libraryFolder,root);

            nav.MenuItems.Add(root);
        }

        // Separator
        nav.MenuItems.Add(new NavigationViewItemSeparator());

        // Actions header
        nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Actions" });

        nav.MenuItems.Add(CreateActionItem("Choose folder",Symbol.Folder,"action:chooseFolder"));
        nav.MenuItems.Add(CreateActionItem("Import MP4",Symbol.Add,"action:import"));
        nav.MenuItems.Add(CreateActionItem("Refresh",Symbol.Refresh,"action:refresh"));
        nav.MenuItems.Add(CreateActionItem("Delete selected",Symbol.Delete,"action:delete"));
        nav.MenuItems.Add(CreateActionItem("Open folder",Symbol.OpenFile,"action:openFolder"));
    }

    public async Task HandleExpandingAsync(NavigationViewItemExpandingEventArgs args)
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

        // Optional context flyout
        if (_fileFlyoutFactory is not null)
            item.ContextFlyout = _fileFlyoutFactory(file,item);

        // Optional right-click selection hook
        if (_onFileRightTapped is not null)
        {
            item.RightTapped += (_,__) => _onFileRightTapped(file,item);
        }

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
}
