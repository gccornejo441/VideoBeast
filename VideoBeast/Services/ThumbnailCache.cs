using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Microsoft.UI.Xaml.Media.Imaging;

namespace VideoBeast.Services;

public sealed class ThumbnailCache
{
    private static ThumbnailCache? _instance;
    public static ThumbnailCache Instance => _instance ??= new ThumbnailCache();

    private readonly StorageFolder _cacheFolder;

    private ThumbnailCache()
    {
        _cacheFolder = ApplicationData.Current.LocalCacheFolder;
    }

    /// <summary>
    /// Gets or generates a thumbnail for a video file.
    /// Returns a cache key that can be used to retrieve the thumbnail later.
    /// </summary>
    public async Task<string?> GetOrCreateThumbnailKeyAsync(StorageFile videoFile, string? folderToken = null)
    {
        try
        {
            // Generate stable key based on folder token + filename
            var keyInput = !string.IsNullOrEmpty(folderToken)
                ? $"{folderToken}|{videoFile.Name}"
                : videoFile.Path;
            var key = GenerateKey(keyInput);
            var thumbnailFile = await GetThumbnailFileAsync(key);

            // If thumbnail doesn't exist, create it
            if (thumbnailFile == null)
            {
                var thumbnail = await videoFile.GetThumbnailAsync(ThumbnailMode.VideosView, 256);
                if (thumbnail != null && thumbnail.Size > 0)
                {
                    thumbnailFile = await _cacheFolder.CreateFileAsync($"thumb_{key}.jpg", CreationCollisionOption.ReplaceExisting);
                    using (var stream = await thumbnailFile.OpenStreamForWriteAsync())
                    {
                        await thumbnail.AsStreamForRead().CopyToAsync(stream);
                    }
                }
            }

            return thumbnailFile != null ? key : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a thumbnail image from cache using the key.
    /// </summary>
    public async Task<BitmapImage?> LoadThumbnailAsync(string key)
    {
        try
        {
            var thumbnailFile = await GetThumbnailFileAsync(key);
            if (thumbnailFile == null) return null;

            var bitmap = new BitmapImage();
            using (var stream = await thumbnailFile.OpenReadAsync())
            {
                await bitmap.SetSourceAsync(stream);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a playlist cover from the first item's thumbnail.
    /// </summary>
    public Task<string?> GeneratePlaylistCoverAsync(string? firstItemKey)
    {
        // For now, just return the first item's key
        // In the future, could generate a collage of multiple thumbnails
        return Task.FromResult(firstItemKey);
    }

    /// <summary>
    /// Saves a custom image file to the cache and returns its key.
    /// </summary>
    public async Task<string?> SaveCustomImageAsync(StorageFile imageFile)
    {
        try
        {
            // Generate a unique key for this custom image
            var key = $"custom_{Guid.NewGuid():N}";
            var thumbnailFile = await _cacheFolder.CreateFileAsync($"thumb_{key}.jpg", CreationCollisionOption.ReplaceExisting);
            
            // Copy the image file
            await imageFile.CopyAndReplaceAsync(thumbnailFile);
            
            return key;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes orphaned thumbnails not referenced by any playlist.
    /// </summary>
    public async Task CleanupOrphanedThumbnailsAsync(IEnumerable<string> referencedKeys, int maxAgeDays = 30)
    {
        try
        {
            var referencedSet = new HashSet<string>(referencedKeys);
            var files = await _cacheFolder.GetFilesAsync();
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);

            foreach (var file in files)
            {
                if (!file.Name.StartsWith("thumb_") || !file.Name.EndsWith(".jpg"))
                    continue;

                // Extract key from filename: thumb_{key}.jpg
                var key = file.Name.Substring(6, file.Name.Length - 10);

                if (!referencedSet.Contains(key))
                {
                    var props = await file.GetBasicPropertiesAsync();
                    if (props.DateModified < cutoffDate)
                    {
                        await file.DeleteAsync();
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Deletes a thumbnail by key if it exists.
    /// </summary>
    public async Task DeleteThumbnailAsync(string key)
    {
        try
        {
            var file = await _cacheFolder.GetFileAsync($"thumb_{key}.jpg");
            await file.DeleteAsync();
        }
        catch (FileNotFoundException)
        {
            // Already deleted, that's fine
        }
    }

    private async Task<StorageFile?> GetThumbnailFileAsync(string key)
    {
        try
        {
            return await _cacheFolder.GetFileAsync($"thumb_{key}.jpg");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private string GenerateKey(string filePath)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16]; // Use first 16 chars
    }
}
