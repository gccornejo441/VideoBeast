using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VideoBeast.Services;

/// <summary>
/// Maps NavigationView action tags (e.g. "action:import") to async handlers,
/// so MainWindow doesn't need a switch statement.
/// </summary>
public sealed class ActionRouter
{
    private readonly Dictionary<string,Func<Task>> _handlers = new(StringComparer.Ordinal);
    private readonly Action<string,Exception>? _onError;

    public ActionRouter(Action<string,Exception>? onError = null)
    => _onError = onError;

    public void Register(string tag,Func<Task> handler)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("Tag cannot be null/empty.",nameof(tag));
        _handlers[tag] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public async Task<bool> TryInvokeAsync(string tag)
    {
        if (!_handlers.TryGetValue(tag,out var handler))
            return false;

        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(tag,ex);
        }

        return true;
    }
}
