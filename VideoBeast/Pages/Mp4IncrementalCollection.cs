using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;

using Microsoft.UI.Xaml.Data;

using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Search;

namespace VideoBeast.Pages;

public sealed class Mp4IncrementalCollection : ObservableCollection<VideoListItem>, ISupportIncrementalLoading
{
    private readonly StorageFileQueryResult _query;
    private uint _index;
    private bool _hasMore = true;
    private const uint PageSize = 200;

    public Mp4IncrementalCollection(StorageFolder folder)
    {
        var options = new QueryOptions(CommonFileQuery.OrderByName,new[] { ".mp4" })
        {
            FolderDepth = FolderDepth.Shallow
        };

        _query = folder.CreateFileQueryWithOptions(options);
    }

    public bool HasMoreItems => _hasMore;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run(async ct =>
        {
            if (!_hasMore)
                return new LoadMoreItemsResult { Count = 0 };

            var take = Math.Max(count,PageSize);
            var batch = await _query.GetFilesAsync(_index,take);

            foreach (var f in batch)
                Add(new VideoListItem(f));

            _index += (uint)batch.Count;
            if (batch.Count == 0)
                _hasMore = false;

            return new LoadMoreItemsResult { Count = (uint)batch.Count };
        });
    }
}
