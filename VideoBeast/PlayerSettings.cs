using Microsoft.UI.Xaml.Media;

using System.Text.Json.Serialization;

namespace VideoBeast;

public sealed class PlayerSettings
{
    public bool AutoPlay { get; set; } = true;
    public bool AreTransportControlsEnabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Stretch Stretch { get; set; } = Stretch.Uniform;

    public bool IsFullWindow { get; set; } = false;

    public double Volume { get; set; } = 1.0;    
    public bool IsMuted { get; set; } = false;
    public bool IsLoopingEnabled { get; set; } = false;

    public double PlaybackRate { get; set; } = 1.0; 

    public string LetterboxColorHex { get; set; } = "#FF000000";

    public PlayerSettings Clone() => new PlayerSettings
    {
        AutoPlay = this.AutoPlay,
        AreTransportControlsEnabled = this.AreTransportControlsEnabled,
        Stretch = this.Stretch,
        IsFullWindow = this.IsFullWindow,

        Volume = this.Volume,
        IsMuted = this.IsMuted,
        IsLoopingEnabled = this.IsLoopingEnabled,

        PlaybackRate = this.PlaybackRate,
        LetterboxColorHex = this.LetterboxColorHex
    };
}
