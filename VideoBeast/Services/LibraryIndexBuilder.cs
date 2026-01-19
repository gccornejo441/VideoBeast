using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace VideoBeast.Services;

public sealed class LibraryIndexBuilder
{
    public async Task<LibraryIndexSnapshot> BuildAsync(StorageFolder libraryRoot, CancellationToken ct)
    {
        // Run enumeration on background thread
        return await Task.Run(async () =>
        {
            var entries = new List<LibraryIndexEntry>();
            await EnumerateRecursiveAsync(libraryRoot, entries, ct);
            
            // Build prefix map
            var prefixMap = BuildPrefixMap(entries);
            
            return new LibraryIndexSnapshot(entries, prefixMap);
        }, ct);
    }

    private async Task EnumerateRecursiveAsync(
        StorageFolder folder, 
        List<LibraryIndexEntry> entries, 
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // Add this folder to index
            entries.Add(new LibraryIndexEntry(
                folder.Name,
                folder.Name.ToLowerInvariant(),
                folder.Path,
                SuggestionKind.Folder));

            // Enumerate files - add .mp4 files only
            var files = await folder.GetFilesAsync();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                
                if (Path.GetExtension(file.Name).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(new LibraryIndexEntry(
                        file.Name,
                        file.Name.ToLowerInvariant(),
                        file.Path,
                        SuggestionKind.Video));
                }
            }

            // Recursively enumerate subfolders
            var folders = await folder.GetFoldersAsync();
            foreach (var subfolder in folders)
            {
                ct.ThrowIfCancellationRequested();
                await EnumerateRecursiveAsync(subfolder, entries, ct);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip this folder silently
        }
        catch (FileNotFoundException)
        {
            // Folder may have been deleted during enumeration, skip
        }
    }

    private Dictionary<string, int[]> BuildPrefixMap(List<LibraryIndexEntry> entries)
    {
        const int PrefixLength = 2;
        
        var prefixGroups = new Dictionary<string, List<int>>();
        
        for (int i = 0; i < entries.Count; i++)
        {
            var normalized = entries[i].NameNormalized;
            if (normalized.Length >= PrefixLength)
            {
                var prefix = normalized.Substring(0, PrefixLength);
                if (!prefixGroups.TryGetValue(prefix, out var indices))
                {
                    indices = new List<int>();
                    prefixGroups[prefix] = indices;
                }
                indices.Add(i);
            }
        }

        // Convert to immutable dictionary with int arrays
        return prefixGroups.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToArray());
    }
}
