using Microsoft.UI.Windowing;

using Windows.Graphics;
using Windows.Storage;

namespace VideoBeast.Services;

public sealed class WindowSizingService
{
    private const string HasLaunchedBeforeKey = "HasLaunchedBefore";

    private readonly AppWindow _appWindow;

    public WindowSizingService(AppWindow appWindow)
    {
        _appWindow = appWindow;
    }

    public void ApplyMinimumSize(int minWidth,int minHeight)
    {
        if (_appWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = minWidth;
            op.PreferredMinimumHeight = minHeight;
        }
    }

    public void ApplyDefaultSizeIfFirstLaunch(int width,int height)
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue(HasLaunchedBeforeKey,out var value)
            && value is bool hasLaunched
            && hasLaunched)
        {
            return;
        }

        _appWindow.Resize(new SizeInt32(width,height));
        settings.Values[HasLaunchedBeforeKey] = true;
    }
}
