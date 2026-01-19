namespace VideoBeast.Services;

public sealed class LibraryIndexEntry
{
    public string Name { get; }
    public string NameNormalized { get; }
    public string FullPath { get; }
    public SuggestionKind Kind { get; }

    public LibraryIndexEntry(string name, string nameNormalized, string fullPath, SuggestionKind kind)
    {
        Name = name;
        NameNormalized = nameNormalized;
        FullPath = fullPath;
        Kind = kind;
    }
}
