using System;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoBeast.Services;

public sealed class WindowChromeService
{
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueue _dispatcherQueue;

    private readonly Func<bool> _getExtendsContentIntoTitleBar;
    private readonly Action<bool> _setExtendsContentIntoTitleBar;

    private readonly Action<UIElement?> _setTitleBar;
    private readonly Func<UIElement> _getTitleBarElement;

    private readonly Func<NavigationView> _getNavView;
    private readonly Func<InfoBar> _getStatusBar;

    private bool _prevExtendsContentIntoTitleBar;

    private Visibility _prevTitleBarVisibility;
    private Visibility _prevStatusBarVisibility;
    private bool _prevStatusBarIsOpen;

    private bool _prevNavIsPaneVisible;
    private bool _prevNavIsPaneOpen;
    private bool _prevNavIsSettingsVisible;
    private NavigationViewPaneDisplayMode _prevNavPaneDisplayMode;

    private double _prevNavCompactPaneLength;
    private double _prevNavOpenPaneLength;

    private readonly WindowModeService _windowMode;

    public bool IsPlayerFullscreen { get; private set; }
    public bool IsCompactOverlay => _appWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay;

    public WindowChromeService(
        AppWindow appWindow,
        DispatcherQueue dispatcherQueue,
        Func<bool> getExtendsContentIntoTitleBar,
        Action<bool> setExtendsContentIntoTitleBar,
        Action<UIElement?> setTitleBar,
        Func<UIElement> getTitleBarElement,
        Func<NavigationView> getNavView,
        Func<InfoBar> getStatusBar)
    {
        _appWindow = appWindow;
        _dispatcherQueue = dispatcherQueue;

        _getExtendsContentIntoTitleBar = getExtendsContentIntoTitleBar;
        _setExtendsContentIntoTitleBar = setExtendsContentIntoTitleBar;

        _setTitleBar = setTitleBar;
        _getTitleBarElement = getTitleBarElement;

        _getNavView = getNavView;
        _getStatusBar = getStatusBar;

        _windowMode = new WindowModeService(_appWindow);
    }

    public void TogglePlayerFullscreen() => SetPlayerFullscreen(!IsPlayerFullscreen);

    public void SetPlayerFullscreen(bool on)
    {
        if (IsPlayerFullscreen == on) return;
        IsPlayerFullscreen = on;

        var nav = _getNavView();
        var status = _getStatusBar();
        var titleBar = _getTitleBarElement();

        try
        {
            if (on)
            {
                _prevExtendsContentIntoTitleBar = _getExtendsContentIntoTitleBar();

                _prevTitleBarVisibility = titleBar.Visibility;

                _prevStatusBarVisibility = status.Visibility;
                _prevStatusBarIsOpen = status.IsOpen;

                _prevNavIsPaneVisible = nav.IsPaneVisible;
                _prevNavIsPaneOpen = nav.IsPaneOpen;
                _prevNavIsSettingsVisible = nav.IsSettingsVisible;
                _prevNavPaneDisplayMode = nav.PaneDisplayMode;

                _prevNavCompactPaneLength = nav.CompactPaneLength;
                _prevNavOpenPaneLength = nav.OpenPaneLength;

                if (_appWindow.Presenter is OverlappedPresenter op)
                {
                    op.SetBorderAndTitleBar(false,false);
                }

                _windowMode.EnterFullScreen();
                ApplyShellFullscreen(nav,status,titleBar,enable: true);
            }
            else
            {
                _windowMode.ExitFullScreen();

                if (_appWindow.Presenter is OverlappedPresenter op)
                {
                    op.SetBorderAndTitleBar(true,true);
                }

                ApplyShellFullscreen(nav,status,titleBar,enable: false);
            }
        }
        catch { }
    }

    public void ToggleCompactOverlay()
    {
        if (IsPlayerFullscreen)
            SetPlayerFullscreen(false);

        _windowMode.ToggleCompactOverlay();
    }

    private void ApplyShellFullscreen(NavigationView nav,InfoBar status,UIElement titleBar,bool enable)
    {
        void Apply()
        {
            if (enable)
            {
                status.IsOpen = false;
                status.Visibility = Visibility.Collapsed;

                titleBar.Visibility = Visibility.Collapsed;

                _setExtendsContentIntoTitleBar(true);
                _setTitleBar(null);

                nav.IsSettingsVisible = false;
                nav.IsPaneOpen = false;

                nav.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;

                nav.CompactPaneLength = 0;
                nav.OpenPaneLength = 0;

                nav.IsPaneVisible = false;
            }
            else
            {
                nav.PaneDisplayMode = _prevNavPaneDisplayMode;

                nav.CompactPaneLength = _prevNavCompactPaneLength;
                nav.OpenPaneLength = _prevNavOpenPaneLength;

                nav.IsPaneVisible = _prevNavIsPaneVisible;
                nav.IsPaneOpen = _prevNavIsPaneOpen;
                nav.IsSettingsVisible = _prevNavIsSettingsVisible;

                titleBar.Visibility = _prevTitleBarVisibility;

                status.Visibility = _prevStatusBarVisibility;
                status.IsOpen = _prevStatusBarIsOpen;

                _setExtendsContentIntoTitleBar(_prevExtendsContentIntoTitleBar);
                if (_prevExtendsContentIntoTitleBar)
                    _setTitleBar(titleBar);
            }

            nav.UpdateLayout();
        }

        if (_dispatcherQueue.HasThreadAccess)
            Apply();
        else
            _dispatcherQueue.TryEnqueue(Apply);
    }
}
