using System;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;

namespace VideoBeast.Converters;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,string language)
        => value is bool b && b ? 1.0 : 0.0;

    public object ConvertBack(object value,Type targetType,object parameter,string language)
        => throw new NotSupportedException();
}

public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,string language)
        => value is bool b && b ? FontWeights.SemiBold : FontWeights.Normal;

    public object ConvertBack(object value,Type targetType,object parameter,string language)
        => throw new NotSupportedException();
}
