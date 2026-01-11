using System.Collections.Generic;

namespace VideoBeast.Playlists;

public sealed class PlaylistCollection
{
    public List<PlaylistModel> Playlists { get; set; } = new();
}
