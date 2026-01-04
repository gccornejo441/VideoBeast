using System;

using Microsoft.UI.Xaml.Data;

namespace VideoBeast.Converters;
public sealed class SeekThumbToolTipConverter : IValueConverter
{
    // PlayerPage will keep this updated when duration changes.
    public double DurationSeconds { get; set; }

    public object Convert(object value,Type targetType,object parameter,string language)
    {
        if (value is not double v)
            return "0:00";

        // Your SeekSlider is 0..1 (fraction). Convert to seconds.
        var dur = DurationSeconds;
        var seconds = dur > 0.1
            ? Math.Clamp(v,0,1) * dur
            : 0;

        var t = TimeSpan.FromSeconds(seconds);

        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    public object ConvertBack(object value,Type targetType,object parameter,string language)
        => throw new NotSupportedException();
}
