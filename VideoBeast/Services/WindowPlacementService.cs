using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.UI.Windowing;

using Windows.Graphics;
using Windows.Storage;

namespace VideoBeast.Services;

public sealed class WindowPlacementService
{
    private const string PlacementKey = "WindowPlacementV1";

    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;

    private const int MinVisibleWidth = 100;
    private const int MinVisibleHeight = 100;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public WindowPlacement? LoadPlacement()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(PlacementKey,out var raw)
                && raw is string json
                && !string.IsNullOrWhiteSpace(json))
            {
                return JsonSerializer.Deserialize<WindowPlacement>(json,Options);
            }
        }
        catch
        {
            // swallow + fall back
        }

        return null;
    }

    public void SavePlacement(AppWindow appWindow)
    {
        if (appWindow is null) return;

        var presenterKind = appWindow.Presenter.Kind;
        var placement = new WindowPlacement
        {
            Width = appWindow.Size.Width,
            Height = appWindow.Size.Height,
            X = appWindow.Position.X,
            Y = appWindow.Position.Y,
            IsCompactOverlay = presenterKind == AppWindowPresenterKind.CompactOverlay,
            WasFullScreen = presenterKind == AppWindowPresenterKind.FullScreen,
            PresenterKind = presenterKind
        };

        if (presenterKind == AppWindowPresenterKind.Overlapped
            && appWindow.Presenter is OverlappedPresenter op)
            placement.IsMaximized = op.State == OverlappedPresenterState.Maximized;

        var json = JsonSerializer.Serialize(placement,Options);
        ApplicationData.Current.LocalSettings.Values[PlacementKey] = json;
    }

    public void ApplyPlacement(AppWindow appWindow,WindowPlacement placement)
    {
        if (appWindow is null || placement is null) return;

        var presenterKind = NormalizePresenterKind(placement);

        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id,DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var target = new RectInt32(placement.X,placement.Y,placement.Width,placement.Height);

        var safeTarget = IsPlacementVisible(target,workArea)
            ? target
            : BuildCenteredRect(workArea);

        if (presenterKind == AppWindowPresenterKind.CompactOverlay)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            appWindow.MoveAndResize(safeTarget);
            return;
        }

        appWindow.MoveAndResize(safeTarget);

        if (placement.IsMaximized && appWindow.Presenter is OverlappedPresenter op)
            op.Maximize();
    }

    private static AppWindowPresenterKind NormalizePresenterKind(WindowPlacement placement)
    {
        if (placement.PresenterKind != AppWindowPresenterKind.Overlapped)
            return placement.PresenterKind;

        if (placement.IsCompactOverlay)
            return AppWindowPresenterKind.CompactOverlay;

        if (placement.WasFullScreen)
            return AppWindowPresenterKind.FullScreen;

        return AppWindowPresenterKind.Overlapped;
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

    private static RectInt32 BuildCenteredRect(RectInt32 workArea)
    {
        int width = Math.Min(DefaultWidth,workArea.Width);
        int height = Math.Min(DefaultHeight,workArea.Height);

        int x = workArea.X + Math.Max(0,(workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0,(workArea.Height - height) / 2);

        return new RectInt32(x,y,width,height);
    }
}
