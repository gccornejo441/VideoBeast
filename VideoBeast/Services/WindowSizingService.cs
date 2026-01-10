using System;

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
        ApplyDefaultSizeIfFirstLaunch(width,height,width,height);
    }

    public void ApplyDefaultSizeIfFirstLaunch(int width,int height,int fallbackWidth,int fallbackHeight)
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue(HasLaunchedBeforeKey,out var value)
            && value is bool hasLaunched
            && hasLaunched)
        {
            return;
        }

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id,DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var target = new SizeInt32(width,height);
        if (target.Width > workArea.Width || target.Height > workArea.Height)
            target = new SizeInt32(fallbackWidth,fallbackHeight);

        if (target.Width > workArea.Width || target.Height > workArea.Height)
        {
            target = new SizeInt32(
                Math.Min(target.Width,workArea.Width),
                Math.Min(target.Height,workArea.Height));
        }

        _appWindow.Resize(target);
        settings.Values[HasLaunchedBeforeKey] = true;
    }
}
