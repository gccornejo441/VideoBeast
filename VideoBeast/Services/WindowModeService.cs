using System;

using Microsoft.UI.Windowing;

using Windows.Graphics;

namespace VideoBeast.Services;

public sealed class WindowModeService
{
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;

    private const int MinVisibleWidth = 100;
    private const int MinVisibleHeight = 100;

    private readonly AppWindow _appWindow;

    private RectInt32? _lastNormalBounds;
    private bool _lastNormalWasMaximized;

    public WindowModeService(AppWindow appWindow)
    {
        _appWindow = appWindow;
    }

    public RectInt32? LastNormalBounds => _lastNormalBounds;

    public bool LastNormalWasMaximized => _lastNormalWasMaximized;

    public void EnterFullScreen()
    {
        if (_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) return;

        RememberNormalBoundsIfNeeded();
        _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    public void ExitFullScreen()
    {
        if (_appWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen) return;

        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        RestoreNormalBounds();
    }

    private void RememberNormalBoundsIfNeeded()
    {
        if (_appWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) return;

        _lastNormalBounds = new RectInt32(
            _appWindow.Position.X,
            _appWindow.Position.Y,
            _appWindow.Size.Width,
            _appWindow.Size.Height);

        if (_appWindow.Presenter is OverlappedPresenter op)
            _lastNormalWasMaximized = op.State == OverlappedPresenterState.Maximized;
        else
            _lastNormalWasMaximized = false;
    }

    private void RestoreNormalBounds()
    {
        if (_appWindow.Presenter is not OverlappedPresenter op) return;

        if (_lastNormalWasMaximized)
        {
            op.Maximize();
            return;
        }

        var bounds = _lastNormalBounds ?? BuildCenteredRect();
        var safeBounds = EnsureVisible(bounds);
        _appWindow.MoveAndResize(safeBounds);
    }

    private RectInt32 BuildCenteredRect()
    {
        var workArea = GetWorkArea();

        int width = Math.Min(DefaultWidth,workArea.Width);
        int height = Math.Min(DefaultHeight,workArea.Height);

        int x = workArea.X + Math.Max(0,(workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0,(workArea.Height - height) / 2);

        return new RectInt32(x,y,width,height);
    }

    private RectInt32 EnsureVisible(RectInt32 bounds)
    {
        var workArea = GetWorkArea();

        return IsPlacementVisible(bounds,workArea)
            ? bounds
            : BuildCenteredRect();
    }

    private RectInt32 GetWorkArea()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id,DisplayAreaFallback.Primary);
        return displayArea.WorkArea;
    }

    private static bool IsPlacementVisible(RectInt32 placement,RectInt32 workArea)
    {
        int left = Math.Max(placement.X,workArea.X);
        int top = Math.Max(placement.Y,workArea.Y);
        int right = Math.Min(placement.X + placement.Width,workArea.X + workArea.Width);
        int bottom = Math.Min(placement.Y + placement.Height,workArea.Y + workArea.Height);

        int overlapWidth = right - left;
        int overlapHeight = bottom - top;

        return overlapWidth >= MinVisibleWidth && overlapHeight >= MinVisibleHeight;
    }
}
