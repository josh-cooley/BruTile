using System.Threading.Tasks;

namespace BruTile
{
    public interface IAsyncTileProvider : ITileProvider
    {
        Task<byte[]> GetTileAsync(TileInfo tileInfo);





        //public System.Threading.Tasks.Task<byte[]> GetTileAsync(TileInfo tileInfo)
        //{
        //    var bytes = _persistentCache.Find(tileInfo.Index);
        //    if (bytes == null)
        //    {
        //        var bytesTask = _fetchTileAsync(_request.GetUri(tileInfo));
        //        return bytesTask.ContinueWith(fetchResult =>
        //        {
        //            var fetchedBytes = fetchResult.Result;
        //            if (fetchedBytes != null) _persistentCache.Add(tileInfo.Index, fetchedBytes);
        //            return fetchedBytes;
        //        });
        //    }
        //    else
        //    {
        //        return Utilities.TaskFromValue(bytes);
        //    }
        //}
    }
}
