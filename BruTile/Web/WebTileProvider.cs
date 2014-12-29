// Copyright (c) BruTile developers team. All rights reserved. See License.txt in the project root for license information.

using BruTile.Cache;
using System;

namespace BruTile.Web
{
    public class WebTileProvider : ITileProviderAsync, IRequest
    {
        private readonly IPersistentCache<byte[]> _persistentCache;
        private readonly IRequest _request;
        private readonly Func<Uri, byte[]> _fetchTile;
        private readonly Func<Uri, System.Threading.Tasks.Task<byte[]>> _fetchTileAsync;

        public WebTileProvider(IRequest request = null, IPersistentCache<byte[]> persistentCache = null,
            Func<Uri, byte[]> fetchTile = null)
        {
            _request = request ?? new NullRequest();
            _persistentCache = persistentCache ?? new NullCache();
            _fetchTile = fetchTile ?? (RequestHelper.FetchImage);
            _fetchTileAsync = RequestHelper.FetchImageAsync;
        }

        public byte[] GetTile(TileInfo tileInfo)
        {
            var bytes = _persistentCache.Find(tileInfo.Index);
            if (bytes == null)
            {
                bytes = _fetchTile(_request.GetUri(tileInfo));
                if (bytes != null) _persistentCache.Add(tileInfo.Index, bytes);
            }
            return bytes;
        }


        public System.Threading.Tasks.Task<byte[]> GetTileAsync(TileInfo tileInfo)
        {
            var bytes = _persistentCache.Find(tileInfo.Index);
            if (bytes == null)
            {
                var bytesTask = _fetchTileAsync(_request.GetUri(tileInfo));
                return bytesTask.ContinueWith(fetchResult =>
                {
                    var fetchedBytes = fetchResult.Result;
                    if (fetchedBytes != null) _persistentCache.Add(tileInfo.Index, fetchedBytes);
                    return fetchedBytes;
                });
            }
            else
            {
                return Utilities.TaskFromValue(bytes);
            }
        }

        public Uri GetUri(TileInfo tileInfo)
        {
            return _request.GetUri(tileInfo);

        }
    }
}
