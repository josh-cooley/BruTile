using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Web
{
    public class AsyncHttpTileSource : ITileSourceAsync, IRequest
    {
        private readonly AsyncWebTileProvider _provider;

        public  AsyncHttpTileSource(ITileSchema tileSchema, string urlFormatter, IEnumerable<string> serverNodes = null, 
            string apiKey = null, string name = null, IAsyncPersistentCache<byte[]> persistentCache = null, 
            Func<Uri, Task<byte[]>> tileFetcher = null)
            : this(tileSchema, new BasicRequest(urlFormatter, serverNodes, apiKey), name, persistentCache, tileFetcher)
        {
        }

        public AsyncHttpTileSource(ITileSchema tileSchema, IRequest request, string name = null, 
            IAsyncPersistentCache<byte[]> persistentCache = null, Func<Uri, Task<byte[]>> tileFetcher = null)
        {
            _provider = new AsyncWebTileProvider(request, persistentCache, tileFetcher);
            Schema = tileSchema;
            Name = name ?? string.Empty;
        }

        public AsyncHttpTileSource(HttpTileSource tileSource, IAsyncPersistentCache<byte[]> persistentCache = null, Func<Uri, Task<byte[]>> tileFetcher = null)
            : this(tileSource.Schema, tileSource, tileSource.Name, persistentCache, tileFetcher)
        {
        }

        public ITileSchema Schema { get; private set; }
        public string Name { get; set; }

        public byte[] GetTile(TileInfo tileInfo)
        {
            return _provider.GetTile(tileInfo);
        }

        public Task<byte[]> GetTileAsync(TileInfo tileInfo)
        {
            return _provider.GetTileAsync(tileInfo);
        }

        public Uri GetUri(TileInfo info)
        {
            return _provider.GetUri(info);
        }
    }
}
