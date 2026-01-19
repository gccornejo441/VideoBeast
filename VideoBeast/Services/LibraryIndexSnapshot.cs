using System.Collections.Generic;

namespace VideoBeast.Services;

public sealed class LibraryIndexSnapshot
{
    public IReadOnlyList<LibraryIndexEntry> Entries { get; }
    public IReadOnlyDictionary<string, int[]> PrefixMap { get; }

    public LibraryIndexSnapshot(
        IReadOnlyList<LibraryIndexEntry> entries,
        IReadOnlyDictionary<string, int[]> prefixMap)
    {
        Entries = entries;
        PrefixMap = prefixMap;
    }
}
