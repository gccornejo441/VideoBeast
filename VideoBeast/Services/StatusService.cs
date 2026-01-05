using System;

using Microsoft.UI.Xaml.Controls;

using VideoBeast.Pages;

namespace VideoBeast.Services;

public sealed class StatusService
{
    private readonly Func<InfoBar> _getInfoBar;

    public StatusService(Func<InfoBar> getInfoBar)
    {
        _getInfoBar = getInfoBar ?? throw new ArgumentNullException(nameof(getInfoBar));
    }

    public void Show(string message,InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        var bar = _getInfoBar();
        bar.Severity = severity;
        bar.Message = message ?? string.Empty;
        bar.IsOpen = true;
    }

    public void Close()
    {
        var bar = _getInfoBar();
        bar.IsOpen = false;
    }

    /// <summary>
    /// Best-practice UX when no library is selected:
    /// - Update PlayerPage to show empty state
    /// - Show a warning in the status bar
    /// </summary>
    public void ShowMissingLibrary(Frame frame)
    {
        if (frame?.Content is PlayerPage page)
        {
            page.SetCurrentFolderText("No folder selected");
            page.ShowEmptyState(true);
        }

        Show("Choose a library folder first.",InfoBarSeverity.Warning);
    }
}
