using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Windows.Storage;

namespace VideoBeast.Services;

public sealed class LibrarySearchService
{
    public sealed class SearchSuggestion
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

    public async Task<IReadOnlyList<SearchSuggestion>> SearchMp4Async(
        StorageFolder scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length < 2)
            return Array.Empty<SearchSuggestion>();

        IReadOnlyList<StorageFile> files;
        try
        {
            files = await scope.GetFilesAsync();
        }
        catch
        {
            return Array.Empty<SearchSuggestion>();
        }

        ct.ThrowIfCancellationRequested();

        return files
            .Where(f => string.Equals(f.FileType,".mp4",StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Name.Contains(query,StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name)
            .Take(maxResults)
            .Select(f => new SearchSuggestion(f.Name,f))
            .ToList();
    }
}
