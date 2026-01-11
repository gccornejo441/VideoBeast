using System;

namespace VideoBeast.Playlists;

public sealed class PlaylistItemModel
{
    public Guid Id { get; set; }
    public Guid PlaylistId { get; set; }
    public int SortIndex { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DurationText { get; set; }
    
    // Token-based file reference (WinUI-friendly approach)
    public string FolderToken { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    
    // Fallback hints for file resolution
    public string? LastKnownFullPath { get; set; }
    public ulong? SizeBytesHint { get; set; }
    public string? LastWriteTimeUtcHint { get; set; }
    
    // Thumbnail cache key
    public string? ThumbnailKey { get; set; }
    
    // Runtime state (not persisted)
    public bool IsMissing { get; set; }
}
