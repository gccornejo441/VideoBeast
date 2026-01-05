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
    public InfoBar Status => StatusBar;
}
