using Microsoft.UI.Xaml.Controls;

namespace VideoBeast.Controls;

public sealed partial class AppShell : UserControl
{
    public AppShell()
    {
        InitializeComponent();
    }

    public AppTitleBar TitleBar => AppTitleBar;
    public NavigationView Navigation => NavView;
    public Frame Frame => RootFrame;

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusBar.Title = "VideoBeast";
            StatusBar.Message = message;
            StatusBar.Severity = severity;
            StatusBar.IsOpen = true;
        });
    }

    public void HideStatus()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusBar.IsOpen = false;
        });
    }
}
