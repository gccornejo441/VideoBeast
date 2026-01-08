using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml.Controls;

using VideoBeast.Pages;

namespace VideoBeast.Services;

public sealed class StatusService
{
    private readonly Func<InfoBar> _getInfoBar;
    private int _showVersion;

    public StatusService(Func<InfoBar> getInfoBar)
    {
        _getInfoBar = getInfoBar ?? throw new ArgumentNullException(nameof(getInfoBar));
    }

    public void Show(
        string message,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        int? autoDismissMs = null)
    {
        var bar = _getInfoBar();
        var version = Interlocked.Increment(ref _showVersion);

    
        int? ms = autoDismissMs ?? (severity is InfoBarSeverity.Informational or InfoBarSeverity.Success
            ? 4000
            : (int?)null);

        bar.DispatcherQueue.TryEnqueue(() =>
        {
            bar.IsClosable = true;
            bar.Severity = severity;
            bar.Message = message ?? string.Empty;
            bar.IsOpen = true;
        });

        if (ms is int delay && delay > 0)
            _ = AutoCloseAsync(bar,version,delay);
    }

    private async Task AutoCloseAsync(InfoBar bar,int version,int ms)
    {
        await Task.Delay(ms).ConfigureAwait(false);

        bar.DispatcherQueue.TryEnqueue(() =>
        {
            if (version == Volatile.Read(ref _showVersion))
                bar.IsOpen = false;
        });
    }

    public void Close()
    {
        var bar = _getInfoBar();
        Interlocked.Increment(ref _showVersion); 
        bar.DispatcherQueue.TryEnqueue(() => bar.IsOpen = false);
    }

    /// <summary>
    /// Best-practice UX when no library is selected:
    /// - Update PlayerPage to show empty state
    /// - Show a warning in the status bar
    /// </summary>
    public void ShowMissingLibrary(Frame frame)
    {
        frame?.DispatcherQueue.TryEnqueue(() =>
        {
            if (frame.Content is PlayerPage page)
            {
                page.SetCurrentFolderText("No folder selected");
                page.ShowEmptyState(true);
            }
        });

        Show("Choose a library folder first.",InfoBarSeverity.Warning);
    }
}
