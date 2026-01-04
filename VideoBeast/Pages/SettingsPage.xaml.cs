using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VideoBeast.Pages;

public sealed partial class SettingsPage : Page
{
    private Stretch _selectedStretch;

    public SettingsPage()
    {
        InitializeComponent();

        // Read from the live in-memory settings (what the app is actually using)
        _selectedStretch = MainWindow.Instance?.GetPlayerSettings().Stretch
                           ?? MainWindow.DefaultPlayerStretch;

        ApplySelectionToUI(_selectedStretch);
    }

    private void ApplySelectionToUI(Stretch stretch)
    {
        RbFit.IsChecked = stretch == Stretch.Uniform;
        RbFill.IsChecked = stretch == Stretch.UniformToFill;
        RbStretch.IsChecked = stretch == Stretch.Fill;
    }

    private void Scaling_Checked(object sender,RoutedEventArgs e)
    {
        if (sender == RbFit) _selectedStretch = Stretch.Uniform;
        else if (sender == RbFill) _selectedStretch = Stretch.UniformToFill;
        else if (sender == RbStretch) _selectedStretch = Stretch.Fill;
    }

    private void Reset_Click(object sender,RoutedEventArgs e)
    {
        _selectedStretch = MainWindow.DefaultPlayerStretch;
        ApplySelectionToUI(_selectedStretch);
    }

    private void Save_Click(object sender,RoutedEventArgs e)
    {
        // Persist + apply
        MainWindow.Instance?.SavePlayerStretch(_selectedStretch);

        // Go back if possible
        if (Frame?.CanGoBack == true)
            Frame.GoBack();
    }
}
