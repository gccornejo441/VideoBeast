using System.ComponentModel;
using System.Runtime.CompilerServices;

using Windows.Storage;

namespace VideoBeast.Pages;

public sealed class VideoListItem : INotifyPropertyChanged
{
    public StorageFile File { get; }

    public string Name => File?.Name ?? "";
    public string FileType => File?.FileType ?? "";
    public string Path => File?.Path ?? "";

    private bool _isNowPlaying;
    public bool IsNowPlaying
    {
        get => _isNowPlaying;
        set
        {
            if (_isNowPlaying == value) return;
            _isNowPlaying = value;
            OnPropertyChanged();
        }
    }

    public VideoListItem(StorageFile file) => File = file;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
}
