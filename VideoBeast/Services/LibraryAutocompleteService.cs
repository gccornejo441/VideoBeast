using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace VideoBeast.Services;

public sealed class LibraryAutocompleteService : ILibraryAutocompleteService
{
    private readonly LibraryFolderService _libraryFolderService;
    private readonly LibraryIndexBuilder _indexBuilder;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    
    private LibraryIndexSnapshot? _snapshot;
    private CancellationTokenSource? _buildCts;

    public LibraryAutocompleteService(
        LibraryFolderService libraryFolderService,
        LibraryIndexBuilder indexBuilder)
    {
        _libraryFolderService = libraryFolderService;
        _indexBuilder = indexBuilder;
    }

    public async Task<IReadOnlyList<LibrarySuggestion>> GetSuggestionsAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        // Return empty if query too short
        if (query.Length < 2)
            return Array.Empty<LibrarySuggestion>();

        // Get snapshot
        var snapshot = _snapshot;
        if (snapshot == null)
            return Array.Empty<LibrarySuggestion>();

        // Normalize query
        var queryNormalized = query.ToLowerInvariant();

        // Get candidates using prefix map or all entries
        var candidates = GetCandidates(snapshot, queryNormalized);

        // Rank and filter matches
        var scored = new List<(LibraryIndexEntry entry, double score, int matchIndex)>();
        
        foreach (var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();
            
            var nameNorm = entry.NameNormalized;
            int matchIndex;
            double baseScore;

            if (nameNorm.StartsWith(queryNormalized))
            {
                matchIndex = 0;
                baseScore = 1000;
            }
            else if ((matchIndex = nameNorm.IndexOf(queryNormalized, StringComparison.Ordinal)) >= 0)
            {
                baseScore = 600;
            }
            else
            {
                continue; // No match
            }

            // Apply adjustments
            var score = baseScore;
            score += (50 - Math.Min(matchIndex, 50));
            score += (30 - Math.Min(nameNorm.Length, 30));
            score += entry.Kind == SuggestionKind.Video ? 10 : 0;

            scored.Add((entry, score, matchIndex));
        }

        // Sort by score desc, matchIndex asc, name, path
        var sorted = scored
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.matchIndex)
            .ThenBy(x => x.entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();

        // Convert to LibrarySuggestion
        var results = new List<LibrarySuggestion>(sorted.Count);
        foreach (var (entry, score, matchIndex) in sorted)
        {
            var icon = entry.Kind == SuggestionKind.Folder ? Symbol.Folder : Symbol.Video;
            
            results.Add(new LibrarySuggestion(
                displayText: entry.Name,
                fullPath: entry.FullPath,
                kind: entry.Kind,
                icon: icon,
                file: null, // Resolve on selection
                folder: null, // Resolve on selection
                score: score,
                matchIndex: matchIndex));
        }

        return results;
    }

    private IEnumerable<LibraryIndexEntry> GetCandidates(
        LibraryIndexSnapshot snapshot,
        string queryNormalized)
    {
        // Try prefix map lookup for efficiency
        if (queryNormalized.Length >= 2)
        {
            var prefix = queryNormalized.Substring(0, 2);
            if (snapshot.PrefixMap.TryGetValue(prefix, out var indices))
            {
                foreach (var idx in indices)
                {
                    yield return snapshot.Entries[idx];
                }
                yield break;
            }
        }

        // Fallback to all entries
        foreach (var entry in snapshot.Entries)
        {
            yield return entry;
        }
    }

    public async Task EnsureIndexAsync(CancellationToken ct)
    {
        // Already exists?
        if (_snapshot != null)
            return;

        var libraryFolder = _libraryFolderService.LibraryFolder;
        if (libraryFolder == null)
            return; // No library configured

        await _buildLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_snapshot != null)
                return;

            // Create cancellation source for this build
            _buildCts?.Cancel();
            _buildCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Build snapshot
            var newSnapshot = await _indexBuilder.BuildAsync(libraryFolder, _buildCts.Token);

            // Atomically swap
            Interlocked.Exchange(ref _snapshot, newSnapshot);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    public async Task RequestRebuildAsync(CancellationToken ct)
    {
        // Cancel any in-progress build
        _buildCts?.Cancel();

        // Clear snapshot
        Interlocked.Exchange(ref _snapshot, null);

        // Rebuild
        await EnsureIndexAsync(ct);
    }
}
