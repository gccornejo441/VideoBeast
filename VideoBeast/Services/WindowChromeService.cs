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

    private bool _prevExtendsContentIntoTitleBar;

    private Visibility _prevTitleBarVisibility;

    private bool _prevNavIsPaneVisible;
    private bool _prevNavIsPaneOpen;
    private bool _prevNavIsSettingsVisible;
    private NavigationViewPaneDisplayMode _prevNavPaneDisplayMode;

    private double _prevNavCompactPaneLength;
    private double _prevNavOpenPaneLength;

    private readonly WindowModeService _windowMode;

    public bool IsPlayerFullscreen { get; private set; }

    public WindowChromeService(
        AppWindow appWindow,
        DispatcherQueue dispatcherQueue,
        Func<bool> getExtendsContentIntoTitleBar,
        Action<bool> setExtendsContentIntoTitleBar,
        Action<UIElement?> setTitleBar,
        Func<UIElement> getTitleBarElement,
        Func<NavigationView> getNavView)
    {
        _appWindow = appWindow;
        _dispatcherQueue = dispatcherQueue;

        _getExtendsContentIntoTitleBar = getExtendsContentIntoTitleBar;
        _setExtendsContentIntoTitleBar = setExtendsContentIntoTitleBar;

        _setTitleBar = setTitleBar;
        _getTitleBarElement = getTitleBarElement;

        _getNavView = getNavView;

        _windowMode = new WindowModeService(_appWindow);
    }

    public void TogglePlayerFullscreen() => SetPlayerFullscreen(!IsPlayerFullscreen);

    public void SetPlayerFullscreen(bool on)
    {
        if (IsPlayerFullscreen == on) return;
        IsPlayerFullscreen = on;

        var nav = _getNavView();
        var titleBar = _getTitleBarElement();

        try
        {
            if (on)
            {
                _prevExtendsContentIntoTitleBar = _getExtendsContentIntoTitleBar();

                _prevTitleBarVisibility = titleBar.Visibility;

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
                ApplyShellFullscreen(nav,titleBar,enable: true);
            }
            else
            {
                _windowMode.ExitFullScreen();

                if (_appWindow.Presenter is OverlappedPresenter op)
                {
                    op.SetBorderAndTitleBar(true,true);
                }

                ApplyShellFullscreen(nav,titleBar,enable: false);
            }
        }
        catch { }
    }

    private void ApplyShellFullscreen(NavigationView nav,UIElement titleBar,bool enable)
    {
        void Apply()
        {
            if (enable)
            {
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
