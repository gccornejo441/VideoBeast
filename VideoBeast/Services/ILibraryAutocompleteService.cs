using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoBeast.Services;

public interface ILibraryAutocompleteService
{
    Task<IReadOnlyList<LibrarySuggestion>> GetSuggestionsAsync(
        string query,
        int maxResults,
        CancellationToken ct);

    Task EnsureIndexAsync(CancellationToken ct);

    Task RequestRebuildAsync(CancellationToken ct);
}
