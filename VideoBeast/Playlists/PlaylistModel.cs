using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoBeast.Playlists;

public sealed class PlaylistModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _coverImageKey;
    
    public Guid Id { get; set; }
    
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }
    
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    
    public string? CoverImageKey
    {
        get => _coverImageKey;
        set
        {
            if (_coverImageKey != value)
            {
                _coverImageKey = value;
                OnPropertyChanged();
            }
        }
    }
    
    public List<PlaylistItemModel> Items { get; set; } = new();
    
    // Computed property for item count
    public int ItemCount => Items.Count;
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    // Method to notify when items collection changes
    public void NotifyItemsChanged()
    {
        OnPropertyChanged(nameof(ItemCount));
    }
}
