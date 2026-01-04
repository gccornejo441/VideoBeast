using System;

using Microsoft.UI.Xaml.Data;

namespace VideoBeast.Converters;

public sealed class VolumeThumbToolTipConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,string language)
    {
        if (value is double d)
        {
            var pct = (int)Math.Round(Math.Clamp(d,0,100));
            return $"{pct}%";
        }

        if (value is float f)
        {
            var pct = (int)Math.Round(Math.Clamp(f,0f,100f));
            return $"{pct}%";
        }

        return "0%";
    }

    public object ConvertBack(object value,Type targetType,object parameter,string language)
        => throw new NotSupportedException();
}

