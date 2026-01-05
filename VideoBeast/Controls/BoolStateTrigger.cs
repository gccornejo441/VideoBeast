using Microsoft.UI.Xaml;

namespace VideoBeast;

public sealed class BoolStateTrigger : StateTriggerBase
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(BoolStateTrigger),
            new PropertyMetadata(false,OnIsActiveChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty,value);
    }

    private static void OnIsActiveChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        var trigger = (BoolStateTrigger)d;
        trigger.SetActive((bool)e.NewValue);
    }
}
