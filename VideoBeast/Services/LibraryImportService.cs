using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

using WinRT.Interop;

namespace VideoBeast.Services;

public sealed class LibraryImportService
{
    public sealed record ImportResult(int ImportedCount,string Message,InfoBarSeverity Severity);

    public async Task<ImportResult> ImportWithPickerAsync(
        Window window,
        StorageFolder destination)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".mp4");

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker,hwnd);

        var picked = await picker.PickMultipleFilesAsync();
        if (picked is null || picked.Count == 0)
            return new ImportResult(0,"Import canceled.",InfoBarSeverity.Informational);

        int imported = 0;

        foreach (var f in picked)
        {
            if (!string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
                continue;

            await f.CopyAsync(destination,f.Name,NameCollisionOption.GenerateUniqueName);
            imported++;
        }

        if (imported == 0)
            return new ImportResult(0,"No .mp4 files were selected.",InfoBarSeverity.Warning);

        return new ImportResult(imported,$"Imported {imported} video(s).",InfoBarSeverity.Informational);
    }

    public async Task<ImportResult> ImportFromDropAsync(
        DataPackageView dataView,
        StorageFolder destination)
    {
        if (!dataView.Contains(StandardDataFormats.StorageItems))
            return new ImportResult(0,"Nothing to import.",InfoBarSeverity.Warning);

        IReadOnlyList<IStorageItem> items;
        try
        {
            items = await dataView.GetStorageItemsAsync();
        }
        catch
        {
            return new ImportResult(0,"Drop failed (could not read items).",InfoBarSeverity.Error);
        }

        var files = items
            .OfType<StorageFile>()
            .Where(f => string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
            return new ImportResult(0,"Drop .mp4 files only.",InfoBarSeverity.Warning);

        int imported = 0;

        foreach (var f in files)
        {
            await f.CopyAsync(destination,f.Name,NameCollisionOption.GenerateUniqueName);
            imported++;
        }

        return new ImportResult(imported,$"Imported {imported} video(s) via drag and drop.",InfoBarSeverity.Informational);
    }
}
