using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Web
{
    public class AsyncWebTileProvider : IAsyncTileProvider, IRequest
    {
        private readonly IAsyncPersistentCache<byte[]> _persistentCache;
        private readonly Func<Uri, Task<byte[]>> _fetchTile;
        private readonly WebTileProvider _provider;

        public AsyncWebTileProvider(IRequest request = null, IAsyncPersistentCache<byte[]> persistentCache = null,
            Func<Uri, Task<byte[]>> fetchTile = null)
        {
            _persistentCache = persistentCache ?? new AsyncNullCache();
            _fetchTile = fetchTile ?? (AsyncRequestHelper.FetchImageAsync);
            Func<Uri, byte[]> _waitFetchTile = null;
            if (fetchTile != null)
            {
                // should use async api, but if requested, the async api will
                // have to be called on another thread so this thread can wait.
                _waitFetchTile = uri => Task.Run(() => _fetchTile(uri)).Result;
            }
            _provider = new WebTileProvider(request, persistentCache, _waitFetchTile);
        }

        public async Task<byte[]> GetTileAsync(TileInfo tileInfo)
        {
            var bytes = await _persistentCache.FindAsync(tileInfo.Index);
            if (bytes == null)
            {
                bytes = await _fetchTile(GetUri(tileInfo));
                if (bytes != null) await _persistentCache.AddAsync(tileInfo.Index, bytes);
            }
            return bytes;
        }

        public byte[] GetTile(TileInfo tileInfo)
        {
            return _provider.GetTile(tileInfo);
        }

        public Uri GetUri(TileInfo info)
        {
            return _provider.GetUri(info);
        }
    }
}
