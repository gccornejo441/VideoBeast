using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using VideoBeast.Ai;

using Windows.Storage;

namespace VideoBeast.Services;

public sealed class SearchCoordinator
{
    private readonly Func<StorageFolder?> _getLibraryFolder;
    private readonly Func<StorageFolder?> _getSelectedFolder;
    private readonly Func<global::VideoBeast.PlayerSettings> _getPlayerSettings;
    private readonly Action<StorageFile> _onFileChosen;
    private readonly Func<Microsoft.UI.Xaml.XamlRoot> _getXamlRoot;
    private readonly Func<Type, object?, bool> _navigateToPage;
    private readonly Action<string, InfoBarSeverity> _showStatus;

    private readonly PlaybackCoordinator _playback;
    private readonly LibrarySearchService _searchService;
    private readonly ActionRouter _actionRouter;

    private StorageFile? _searchSelectedFile;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _aiCts;
    private string? _pendingAiCommand;

    private OllamaClient? _ollamaClient;
    private readonly AiCommandParser _aiParser = new();
    private AiCommandExecutor? _aiExecutor;

    public SearchCoordinator(
        Func<StorageFolder?> getLibraryFolder,
        Func<StorageFolder?> getSelectedFolder,
        Func<global::VideoBeast.PlayerSettings> getPlayerSettings,
        Action<StorageFile> onFileChosen,
        PlaybackCoordinator playback,
        LibrarySearchService searchService,
        ActionRouter actionRouter,
        Func<Microsoft.UI.Xaml.XamlRoot> getXamlRoot,
        Func<Type, object?, bool> navigateToPage,
        Action<string, InfoBarSeverity> showStatus)
    {
        _getLibraryFolder = getLibraryFolder ?? throw new ArgumentNullException(nameof(getLibraryFolder));
        _getSelectedFolder = getSelectedFolder ?? throw new ArgumentNullException(nameof(getSelectedFolder));
        _getPlayerSettings = getPlayerSettings ?? throw new ArgumentNullException(nameof(getPlayerSettings));
        _onFileChosen = onFileChosen ?? throw new ArgumentNullException(nameof(onFileChosen));
        _getXamlRoot = getXamlRoot ?? throw new ArgumentNullException(nameof(getXamlRoot));
        _navigateToPage = navigateToPage ?? throw new ArgumentNullException(nameof(navigateToPage));
        _showStatus = showStatus ?? throw new ArgumentNullException(nameof(showStatus));

        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _actionRouter = actionRouter ?? throw new ArgumentNullException(nameof(actionRouter));

        InitializeAiServices();
    }

    private void InitializeAiServices()
    {
        var settings = OllamaSettingsStore.Load();
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.DefaultModel))
        {
            _ollamaClient = new OllamaClient(settings.BaseUrl);
            _aiExecutor = new AiCommandExecutor(
                getXamlRoot: _getXamlRoot,
                getLibraryFolder: _getLibraryFolder,
                getSelectedFolder: _getSelectedFolder,
                getPlayerSettings: _getPlayerSettings,
                playback: _playback,
                searchService: _searchService,
                actionRouter: _actionRouter,
                navigateToPage: _navigateToPage,
                showStatus: _showStatus);
        }
    }

    public void RefreshAiSettings()
    {
        _ollamaClient?.Dispose();
        _ollamaClient = null;
        _aiExecutor = null;
        InitializeAiServices();
    }

    public void Reset()
    {
        _searchSelectedFile = null;
        _pendingAiCommand = null;
        _cts?.Cancel();
        _cts = null;
        _aiCts?.Cancel();
        _aiCts = null;
    }

    public async void HandleTextChanged(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _searchSelectedFile = null;
        _pendingAiCommand = null;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var q = sender.Text?.Trim() ?? string.Empty;

        if (q.StartsWith(">"))
        {
            var command = q.Substring(1).Trim();
            if (command.Length > 0)
            {
                var settings = OllamaSettingsStore.Load();
                
                if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.DefaultModel))
                {
                    sender.ItemsSource = new List<string> { "Enable Local AI + select a model in Settings" };
                    _pendingAiCommand = null;
                    return;
                }

                if (_ollamaClient != null)
                {
                    var available = await _ollamaClient.IsAvailableAsync(ct);
                    if (ct.IsCancellationRequested) return;

                    if (!available)
                    {
                        sender.ItemsSource = new List<string> { $"Ollama not reachable at {settings.BaseUrl}" };
                        _pendingAiCommand = null;
                        return;
                    }
                }

                _pendingAiCommand = command;
                sender.ItemsSource = new List<string> { $"Run AI command: {command}" };
            }
            else
            {
                sender.ItemsSource = null;
            }
            return;
        }

        if (q.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        var library = _getLibraryFolder();
        if (library is null)
        {
            sender.ItemsSource = null;
            return;
        }

        var scope = _getSelectedFolder() ?? library;

        try
        {
            var results = await _searchService.SearchMp4Async(scope,q,10,ct);
            if (ct.IsCancellationRequested) return;

            sender.ItemsSource = results;
        }
        catch (OperationCanceledException) { }
        catch
        {
            sender.ItemsSource = null;
        }
    }

    public void HandleSuggestionChosen(AutoSuggestBox sender,AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string aiCommand && !string.IsNullOrEmpty(_pendingAiCommand))
        {
            sender.Text = ">" + _pendingAiCommand;
            return;
        }

        if (args.SelectedItem is LibrarySearchService.SearchSuggestion s)
        {
            _searchSelectedFile = s.File;
            sender.Text = s.DisplayText;
        }
    }

    public async void HandleQuerySubmitted(AutoSuggestBox sender,AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var q = sender.Text?.Trim() ?? string.Empty;
        
        if (q.StartsWith(">"))
        {
            var settings = OllamaSettingsStore.Load();
            
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.DefaultModel))
            {
                _navigateToPage(typeof(VideoBeast.Pages.SettingsPage), null);
                sender.Text = string.Empty;
                return;
            }

            if (_ollamaClient != null)
            {
                var available = await _ollamaClient.IsAvailableAsync();
                if (!available)
                {
                    _navigateToPage(typeof(VideoBeast.Pages.SettingsPage), null);
                    sender.Text = string.Empty;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_pendingAiCommand))
            {
                _aiCts?.Cancel();
                _aiCts = new CancellationTokenSource();
                var ct = _aiCts.Token;

                await ExecuteAiCommandAsync(_pendingAiCommand, ct);

                sender.Text = string.Empty;
                _pendingAiCommand = null;
            }
            return;
        }

        StorageFile? file = _searchSelectedFile;

        if (file is null && args.ChosenSuggestion is LibrarySearchService.SearchSuggestion chosen)
            file = chosen.File;

        if (file is null)
            return;

        _onFileChosen(file);

        var settings2 = _getPlayerSettings();
        await _playback.RequestPlayAsync(file,settings2);
    }

    private async Task ExecuteAiCommandAsync(string command, CancellationToken ct)
    {
        var settings = OllamaSettingsStore.Load();
        
        if (!settings.Enabled)
        {
            _showStatus("AI commands are disabled. Enable in Settings.", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultModel))
        {
            _showStatus("No AI model selected. Configure in Settings.", InfoBarSeverity.Warning);
            return;
        }

        if (_ollamaClient == null || _aiExecutor == null)
        {
            _showStatus("AI services not initialized. Check Settings.", InfoBarSeverity.Error);
            return;
        }

        _showStatus("Processing AI command...", InfoBarSeverity.Informational);

        try
        {
            var response = await _ollamaClient.ChatAsync(
                settings.DefaultModel,
                _aiParser.GetSystemPrompt(),
                command,
                ct);

            ct.ThrowIfCancellationRequested();

            var parsed = _aiParser.Parse(response);
            await _aiExecutor.ExecuteAsync(parsed, ct);
        }
        catch (OperationCanceledException)
        {
            _showStatus("AI command cancelled", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _showStatus($"AI command failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }
}
