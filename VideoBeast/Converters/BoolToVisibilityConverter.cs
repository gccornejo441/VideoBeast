using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace VideoBeast.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    // Optional defaults
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value,Type targetType,object parameter,string language)
    {
        var flag = value is bool b && b;

        // parameter = "Invert" or "invert"
        if (parameter is string s && s.Equals("Invert",StringComparison.OrdinalIgnoreCase))
            flag = !flag;

        return flag ? TrueValue : FalseValue;
    }
    
    public object ConvertBack(object value,Type targetType,object parameter,string language)
    {
        if (value is Visibility v)
        {
            var flag = v == TrueValue;

            if (parameter is string s && s.Equals("Invert",StringComparison.OrdinalIgnoreCase))
                flag = !flag;

            return flag;
        }

        return false;
    }
}
