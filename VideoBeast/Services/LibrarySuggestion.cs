using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace VideoBeast.Services;

public sealed class LibrarySuggestion
{
    public string DisplayText { get; }
    public string FullPath { get; }
    public SuggestionKind Kind { get; }
    public Symbol Icon { get; }

    public StorageFile? File { get; }
    public StorageFolder? Folder { get; }

    public double Score { get; }
    public int MatchIndex { get; }

    public LibrarySuggestion(
        string displayText,
        string fullPath,
        SuggestionKind kind,
        Symbol icon,
        StorageFile? file,
        StorageFolder? folder,
        double score,
        int matchIndex)
    {
        DisplayText = displayText;
        FullPath = fullPath;
        Kind = kind;
        Icon = icon;
        File = file;
        Folder = folder;
        Score = score;
        MatchIndex = matchIndex;
    }

    public override string ToString() => DisplayText;
}
