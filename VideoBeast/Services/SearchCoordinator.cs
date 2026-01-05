// Services/SearchCoordinator.cs
using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml.Controls;

using Windows.Storage;

namespace VideoBeast.Services;

public sealed class SearchCoordinator
{
    private readonly Func<StorageFolder?> _getLibraryFolder;
    private readonly Func<StorageFolder?> _getSelectedFolder;
    private readonly Func<global::VideoBeast.PlayerSettings> _getPlayerSettings;
    private readonly Action<StorageFile> _onFileChosen;

    private readonly PlaybackCoordinator _playback;
    private readonly LibrarySearchService _searchService;

    private StorageFile? _searchSelectedFile;
    private CancellationTokenSource? _cts;

    public SearchCoordinator(
        Func<StorageFolder?> getLibraryFolder,
        Func<StorageFolder?> getSelectedFolder,
        Func<global::VideoBeast.PlayerSettings> getPlayerSettings,
        Action<StorageFile> onFileChosen,
        PlaybackCoordinator playback,
        LibrarySearchService searchService)
    {
        _getLibraryFolder = getLibraryFolder ?? throw new ArgumentNullException(nameof(getLibraryFolder));
        _getSelectedFolder = getSelectedFolder ?? throw new ArgumentNullException(nameof(getSelectedFolder));
        _getPlayerSettings = getPlayerSettings ?? throw new ArgumentNullException(nameof(getPlayerSettings));
        _onFileChosen = onFileChosen ?? throw new ArgumentNullException(nameof(onFileChosen));

        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    public void Reset()
    {
        _searchSelectedFile = null;
        _cts?.Cancel();
        _cts = null;
    }

    public async void HandleTextChanged(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _searchSelectedFile = null;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var q = sender.Text?.Trim() ?? string.Empty;

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
        if (args.SelectedItem is LibrarySearchService.SearchSuggestion s)
        {
            _searchSelectedFile = s.File;
            sender.Text = s.DisplayText;
        }
    }

    public async void HandleQuerySubmitted(AutoSuggestBox sender,AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        StorageFile? file = _searchSelectedFile;

        if (file is null && args.ChosenSuggestion is LibrarySearchService.SearchSuggestion chosen)
            file = chosen.File;

        if (file is null)
            return;

        _onFileChosen(file);

        var settings = _getPlayerSettings();
        await _playback.RequestPlayAsync(file,settings);
    }
}
