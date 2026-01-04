// PlayerSettings.cs
using Microsoft.UI.Xaml.Media;

using System.Text.Json.Serialization;

namespace VideoBeast;

public sealed class PlayerSettings
{
    // MediaPlayerElement DPs
    public bool AutoPlay { get; set; } = true;
    public bool AreTransportControlsEnabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Stretch Stretch { get; set; } = Stretch.Uniform;

    public bool IsFullWindow { get; set; } = false;

    // MediaPlayer settings
    public double Volume { get; set; } = 1.0;     // 0..1
    public bool IsMuted { get; set; } = false;
    public bool IsLoopingEnabled { get; set; } = false;

    // PlaybackSession settings
    public double PlaybackRate { get; set; } = 1.0; // e.g. 0.25..4.0

    // Optional: what color shows in “bars” area
    // (This is your container background, not the video itself.)
    public string LetterboxColorHex { get; set; } = "#FF000000";

    // Clone so MainWindow can store an immutable snapshot and avoid reference feedback loops.
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
