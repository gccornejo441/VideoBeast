using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoBeast.Playlists;
using VideoBeast.Services;
using Windows.Storage;
using Windows.Storage.Search;

namespace VideoBeast.Ai;

public sealed class AiCommandExecutor
{
    private readonly Func<XamlRoot> _getXamlRoot;
    private readonly Func<StorageFolder?> _getLibraryFolder;
    private readonly Func<StorageFolder?> _getSelectedFolder;
    private readonly Func<PlayerSettings> _getPlayerSettings;
    private readonly PlaybackCoordinator _playback;
    private readonly LibrarySearchService _searchService;
    private readonly ActionRouter _actionRouter;
    private readonly Func<Type, object?, bool> _navigateToPage;
    private readonly Action<string, InfoBarSeverity> _showStatus;

    public AiCommandExecutor(
        Func<XamlRoot> getXamlRoot,
        Func<StorageFolder?> getLibraryFolder,
        Func<StorageFolder?> getSelectedFolder,
        Func<PlayerSettings> getPlayerSettings,
        PlaybackCoordinator playback,
        LibrarySearchService searchService,
        ActionRouter actionRouter,
        Func<Type, object?, bool> navigateToPage,
        Action<string, InfoBarSeverity> showStatus)
    {
        _getXamlRoot = getXamlRoot;
        _getLibraryFolder = getLibraryFolder;
        _getSelectedFolder = getSelectedFolder;
        _getPlayerSettings = getPlayerSettings;
        _playback = playback;
        _searchService = searchService;
        _actionRouter = actionRouter;
        _navigateToPage = navigateToPage;
        _showStatus = showStatus;
    }

    public async Task<bool> ExecuteAsync(AiCommandParser.ParsedCommand command, CancellationToken ct)
    {
        if (command.Type == "unknown")
        {
            _showStatus(command.ConfirmationText ?? "Unknown command", InfoBarSeverity.Warning);
            return false;
        }

        if (command.NeedsConfirmation && !string.IsNullOrEmpty(command.ConfirmationText))
        {
            var confirmed = await ShowConfirmationAsync(command.ConfirmationText);
            if (!confirmed)
            {
                _showStatus("Command cancelled", InfoBarSeverity.Informational);
                return false;
            }
        }

        try
        {
            switch (command.Type)
            {
                case "open_page":
                    return await ExecuteOpenPageAsync(command.Args);
                case "play_video":
                    return await ExecutePlayVideoAsync(command.Args, ct);
                case "import":
                    return await ExecuteImportAsync(command.Args);
                case "create_playlist":
                    return await ExecuteCreatePlaylistAsync(command.Args, ct);
                default:
                    _showStatus($"Command type '{command.Type}' not supported", InfoBarSeverity.Warning);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _showStatus($"Command failed: {ex.Message}", InfoBarSeverity.Error);
            return false;
        }
    }

    private Task<bool> ExecuteOpenPageAsync(JsonElement args)
    {
        if (!args.TryGetProperty("page", out var pageElement))
        {
            _showStatus("Missing 'page' argument", InfoBarSeverity.Warning);
            return Task.FromResult(false);
        }

        var page = pageElement.GetString()?.ToLowerInvariant();
        Type? pageType = page switch
        {
            "player" => typeof(VideoBeast.Pages.PlayerPage),
            "playlists" => typeof(VideoBeast.Pages.PlaylistsPage),
            "settings" => typeof(VideoBeast.Pages.SettingsPage),
            _ => null
        };

        if (pageType == null)
        {
            _showStatus($"Unknown page: {page}", InfoBarSeverity.Warning);
            return Task.FromResult(false);
        }

        var navigated = _navigateToPage(pageType, null);
        if (navigated)
            _showStatus($"Opened {page} page", InfoBarSeverity.Success);

        return Task.FromResult(navigated);
    }

    private async Task<bool> ExecutePlayVideoAsync(JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("query", out var queryElement))
        {
            _showStatus("Missing 'query' argument", InfoBarSeverity.Warning);
            return false;
        }

        var query = queryElement.GetString();
        if (string.IsNullOrWhiteSpace(query))
        {
            _showStatus("Empty search query", InfoBarSeverity.Warning);
            return false;
        }

        var scopeStr = "library";
        if (args.TryGetProperty("scope", out var scopeElement))
            scopeStr = scopeElement.GetString()?.ToLowerInvariant() ?? "library";

        var library = _getLibraryFolder();
        if (library == null)
        {
            _showStatus("No library folder set", InfoBarSeverity.Warning);
            return false;
        }

        var scope = scopeStr == "currentfolder" ? (_getSelectedFolder() ?? library) : library;

        var results = await _searchService.SearchMp4Async(scope, query, 10, ct);
        if (ct.IsCancellationRequested) return false;

        if (results.Count == 0)
        {
            _showStatus($"No videos found matching '{query}'", InfoBarSeverity.Warning);
            return false;
        }

        var file = results[0].File;
        var settings = _getPlayerSettings();
        await _playback.RequestPlayAsync(file, settings);
        _showStatus($"Playing: {file.Name}", InfoBarSeverity.Success);
        return true;
    }

    private async Task<bool> ExecuteImportAsync(JsonElement args)
    {
        var source = "picker";
        if (args.TryGetProperty("source", out var sourceElement))
        {
            source = sourceElement.GetString()?.ToLowerInvariant() ?? "picker";
        }

        var actionTag = source switch
        {
            "picker" => "action:import",
            _ => "action:import"
        };

        var invoked = await _actionRouter.TryInvokeAsync(actionTag);
        if (invoked)
        {
            _showStatus("Import started", InfoBarSeverity.Success);
        }
        return invoked;
    }

    private async Task<bool> ExecuteCreatePlaylistAsync(JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("name", out var nameElement))
        {
            _showStatus("Missing 'name' argument", InfoBarSeverity.Warning);
            return false;
        }

        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            _showStatus("Empty playlist name", InfoBarSeverity.Warning);
            return false;
        }

        var scopeStr = "library";
        if (args.TryGetProperty("fromScope", out var scopeElement))
            scopeStr = scopeElement.GetString()?.ToLowerInvariant() ?? "library";

        var filter = string.Empty;
        if (args.TryGetProperty("filter", out var filterElement))
            filter = filterElement.GetString() ?? string.Empty;

        var library = _getLibraryFolder();
        if (library == null)
        {
            _showStatus("No library folder set", InfoBarSeverity.Warning);
            return false;
        }

        var useCurrent = scopeStr == "currentfolder";
        var scope = useCurrent ? (_getSelectedFolder() ?? library) : library;

        var depth = useCurrent ? FolderDepth.Shallow : FolderDepth.Deep;
        var options = new QueryOptions(CommonFileQuery.DefaultQuery, new[] { ".mp4" })
        {
            FolderDepth = depth
        };

        var query = scope.CreateFileQueryWithOptions(options);
        var files = await query.GetFilesAsync(0, 500);
        if (ct.IsCancellationRequested) return false;

        var mp4Files = files.ToList();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            mp4Files = mp4Files
                .Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (mp4Files.Count == 0)
        {
            _showStatus("No videos found to add to playlist", InfoBarSeverity.Warning);
            return false;
        }

        var store = new PlaylistStore();
        var playlist = await store.CreateAsync(name);
        await store.AddItemsAsync(playlist.Id, mp4Files.ToArray());

        _showStatus($"Created playlist '{name}' with {mp4Files.Count} videos", InfoBarSeverity.Success);
        return true;
    }

    private async Task<bool> ShowConfirmationAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm Command",
            Content = message,
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _getXamlRoot()
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
