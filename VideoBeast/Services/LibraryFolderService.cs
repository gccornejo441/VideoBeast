using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;

using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

using WinRT.Interop;

namespace VideoBeast.Services;

public sealed class LibraryFolderService
{
    private const string LibraryFolderToken = "LibraryFolderToken";

    public StorageFolder? LibraryFolder { get; private set; }
    public StorageFolder? SelectedFolder { get; private set; }

    public async Task RestoreAsync()
    {
        LibraryFolder = await TryGetStoredLibraryFolderAsync();
        SelectedFolder ??= LibraryFolder;
    }

    public void SetSelectedFolder(StorageFolder? folder)
    {
        SelectedFolder = folder;
    }

    public async Task<StorageFolder?> PickAndStoreLibraryFolderAsync(Window window)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker,hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return null;

        StorageApplicationPermissions.FutureAccessList.AddOrReplace(LibraryFolderToken,folder);

        LibraryFolder = folder;
        SelectedFolder = folder;

        return folder;
    }

    public async Task<bool> EnsureLibraryFolderAsync(Action onMissingUi)
    {
        if (LibraryFolder is null)
            LibraryFolder = await TryGetStoredLibraryFolderAsync();

        SelectedFolder ??= LibraryFolder;

        if (LibraryFolder is null)
        {
            onMissingUi?.Invoke();
            return false;
        }

        return true;
    }

    private static async Task<StorageFolder?> TryGetStoredLibraryFolderAsync()
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
}
