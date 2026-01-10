using Microsoft.UI.Windowing;

namespace VideoBeast.Services;

public sealed class WindowPlacement
{
    public int Width { get; set; }
    public int Height { get; set; }

    public int X { get; set; }
    public int Y { get; set; }

    public bool IsMaximized { get; set; }
    public bool WasFullScreen { get; set; }

    public AppWindowPresenterKind PresenterKind { get; set; } = AppWindowPresenterKind.Overlapped;
}
