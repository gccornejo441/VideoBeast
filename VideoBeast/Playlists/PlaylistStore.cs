using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Windows.Storage;
using Windows.Storage.AccessCache;

using VideoBeast.Services;

namespace VideoBeast.Playlists;

public sealed class PlaylistStore
{
    private const string FileName = "playlists.json";
    private const string TokenPrefix = "PlaylistFolder_";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly StorageFolder _storageFolder = ApplicationData.Current.LocalFolder;

    // Shared static cache so all PlaylistStore instances work with the same objects
    private static PlaylistCollection? _cache;

    /// <summary>
    /// Load all playlists from disk.
    /// </summary>
    public async Task<IReadOnlyList<PlaylistModel>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();
            return _cache!.Playlists.AsReadOnly();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Create a new empty playlist with a new GUID.
    /// </summary>
    public async Task<PlaylistModel> CreateAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var now = DateTimeOffset.UtcNow;
            var playlist = new PlaylistModel
            {
                Id = Guid.NewGuid(),
                Name = name,
                CreatedUtc = now,
                UpdatedUtc = now
            };

            _cache!.Playlists.Add(playlist);
            await SaveAsync();

            return playlist;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Rename a playlist and update UpdatedUtc.
    /// </summary>
    public async Task RenameAsync(Guid playlistId, string newName)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is null)
                throw new InvalidOperationException($"Playlist {playlistId} not found.");

            playlist.Name = newName;
            playlist.UpdatedUtc = DateTimeOffset.UtcNow;

            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Remove a playlist.
    /// </summary>
    public async Task DeleteAsync(Guid playlistId)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is null)
                return;

            _cache.Playlists.Remove(playlist);
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Add video files to a playlist (generate tokens, extract display names).
    /// </summary>
    public async Task AddItemsAsync(Guid playlistId, IEnumerable<StorageFile> files)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is null)
                throw new InvalidOperationException($"Playlist {playlistId} not found.");

            var maxSortIndex = playlist.Items.Count > 0
                ? playlist.Items.Max(i => i.SortIndex)
                : -1;

            int i = 0;
            foreach (var file in files)
            {
                // Get or create token for parent folder
                var parentFolder = await file.GetParentAsync();
                var folderToken = await GetOrCreateFolderTokenInternalAsync(parentFolder);

                // Get file properties for hints
                var properties = await file.GetBasicPropertiesAsync();

                var item = new PlaylistItemModel
                {
                    Id = Guid.NewGuid(),
                    PlaylistId = playlistId,
                    SortIndex = maxSortIndex + i + 1,
                    DisplayName = Path.GetFileNameWithoutExtension(file.Name),
                    FolderToken = folderToken,
                    FileName = file.Name,
                    LastKnownFullPath = file.Path,
                    SizeBytesHint = properties.Size,
                    LastWriteTimeUtcHint = properties.DateModified.ToString("o")
                };

                // Generate thumbnail key
                item.ThumbnailKey = await ThumbnailCache.Instance.GetOrCreateThumbnailKeyAsync(file, folderToken);

                playlist.Items.Add(item);
                i++;
            }

            // Update playlist cover if not set
            if (string.IsNullOrEmpty(playlist.CoverImageKey) && playlist.Items.Count > 0)
            {
                var firstItemWithThumb = playlist.Items.FirstOrDefault(i => !string.IsNullOrEmpty(i.ThumbnailKey));
                if (firstItemWithThumb != null)
                {
                    playlist.CoverImageKey = await ThumbnailCache.Instance.GeneratePlaylistCoverAsync(firstItemWithThumb.ThumbnailKey);
                }
            }

            playlist.NotifyItemsChanged();
            playlist.UpdatedUtc = DateTimeOffset.UtcNow;
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Remove an item from a playlist.
    /// </summary>
    public async Task RemoveItemAsync(Guid playlistId, Guid itemId)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is null)
                return;

            var item = playlist.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null)
                return;

            playlist.Items.Remove(item);
            playlist.NotifyItemsChanged();
            playlist.UpdatedUtc = DateTimeOffset.UtcNow;

            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reorder items by updating SortIndex.
    /// </summary>
    public async Task ReorderAsync(Guid playlistId, IReadOnlyList<Guid> itemIdsInOrder)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist is null)
                throw new InvalidOperationException($"Playlist {playlistId} not found.");

            for (int i = 0; i < itemIdsInOrder.Count; i++)
            {
                var item = playlist.Items.FirstOrDefault(it => it.Id == itemIdsInOrder[i]);
                if (item is not null)
                {
                    item.SortIndex = i;
                }
            }

            playlist.UpdatedUtc = DateTimeOffset.UtcNow;
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Try to resolve a playlist item back to a StorageFile using the token.
    /// Returns (file, isMissing) tuple.
    /// </summary>
    public async Task<(StorageFile? file, bool isMissing)> TryResolveAsync(PlaylistItemModel item)
    {
        try
        {
            // Primary: token -> folder -> file
            var folder = await StorageApplicationPermissions.FutureAccessList
                .GetFolderAsync(item.FolderToken);

            var file = await folder.GetFileAsync(item.FileName);
            return (file, false);
        }
        catch
        {
            // Fallback: try LastKnownFullPath
            if (!string.IsNullOrEmpty(item.LastKnownFullPath))
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.LastKnownFullPath);
                    return (file, false);
                }
                catch { }
            }

            return (null, true);
        }
    }

    /// <summary>
    /// Get or create a token for a folder in the FutureAccessList (public version).
    /// </summary>
    public async Task<string> GetOrCreateFolderTokenAsync(StorageFolder folder)
    {
        await _gate.WaitAsync();
        try
        {
            return await GetOrCreateFolderTokenInternalAsync(folder);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Update an existing playlist item with new file reference information.
    /// </summary>
    public async Task UpdateItemAsync(Guid playlistId, PlaylistItemModel updatedItem)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();

            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return;

            var existingItem = playlist.Items.FirstOrDefault(i => i.Id == updatedItem.Id);
            if (existingItem == null) return;

            // Update properties
            existingItem.FolderToken = updatedItem.FolderToken;
            existingItem.FileName = updatedItem.FileName;
            existingItem.LastKnownFullPath = updatedItem.LastKnownFullPath;
            existingItem.SizeBytesHint = updatedItem.SizeBytesHint;
            existingItem.LastWriteTimeUtcHint = updatedItem.LastWriteTimeUtcHint;
            existingItem.DisplayName = updatedItem.DisplayName;
            existingItem.ThumbnailKey = updatedItem.ThumbnailKey;

            playlist.UpdatedUtc = DateTimeOffset.UtcNow;
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Update the cover image key for a playlist.
    /// </summary>
    public async Task UpdateCoverImageAsync(Guid playlistId, string? coverImageKey)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadAsync();
            
            var playlist = _cache!.Playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return;
            
            playlist.CoverImageKey = coverImageKey;
            playlist.UpdatedUtc = DateTimeOffset.UtcNow;
            
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Invalidate the cache to force a reload on next access.
    /// Useful when external changes are made to the playlist data.
    /// </summary>
    public static void InvalidateCache()
    {
        _cache = null;
    }

    /// <summary>
    /// Get or create a token for a folder in the FutureAccessList (internal, no locking).
    /// </summary>
    private async Task<string> GetOrCreateFolderTokenInternalAsync(StorageFolder folder)
    {
        // Check if we already have a token for this folder
        var entries = StorageApplicationPermissions.FutureAccessList.Entries;

        foreach (var entry in entries)
        {
            if (entry.Token.StartsWith(TokenPrefix))
            {
                try
                {
                    var existingFolder = await StorageApplicationPermissions.FutureAccessList
                        .GetFolderAsync(entry.Token);

                    if (existingFolder.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Token;
                    }
                }
                catch
                {
                    // Token invalid, continue
                }
            }
        }

        // Create new token
        var token = $"{TokenPrefix}{Guid.NewGuid():N}";
        StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder);
        return token;
    }

    /// <summary>
    /// Load playlists from disk with corruption recovery.
    /// </summary>
    private async Task LoadAsync()
    {
        if (_cache != null) return;

        try
        {
            var file = await _storageFolder.GetFileAsync(FileName);
            var json = await FileIO.ReadTextAsync(file);
            _cache = JsonSerializer.Deserialize<PlaylistCollection>(json) ?? new PlaylistCollection();
        }
        catch (FileNotFoundException)
        {
            _cache = new PlaylistCollection();
        }
        catch (JsonException)
        {
            // Corrupted JSON - rename and start fresh
            try
            {
                var badFile = await _storageFolder.GetFileAsync(FileName);
                await badFile.RenameAsync($"playlists.bad.{DateTimeOffset.UtcNow.Ticks}.json");
            }
            catch { }

            _cache = new PlaylistCollection();
        }
    }

    /// <summary>
    /// Save the entire playlist collection back to disk with atomic write.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_cache is null)
            return;

        var tempFile = await _storageFolder.CreateFileAsync("playlists.json.tmp", CreationCollisionOption.ReplaceExisting);

        var json = JsonSerializer.Serialize(_cache, Options);
        await FileIO.WriteTextAsync(tempFile, json);

        // Atomic replace: delete old, rename temp
        try
        {
            var mainFile = await _storageFolder.GetFileAsync(FileName);
            await mainFile.DeleteAsync();
        }
        catch (FileNotFoundException)
        {
            // File doesn't exist yet, that's fine
        }

        await tempFile.RenameAsync(FileName);
    }
}
