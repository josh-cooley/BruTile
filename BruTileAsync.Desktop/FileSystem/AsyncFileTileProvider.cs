// Copyright (c) BruTile developers team. All rights reserved. See License.txt in the project root for license information.

using System;
using System.IO;
using BruTile.Cache;

namespace BruTile.FileSystem
{
    public class AsyncFileTileProvider : IAsyncTileProvider
    {
        readonly AsyncFileCache _fileCache;

        public AsyncFileTileProvider(string directory, string format, TimeSpan cacheExpireTime)
        {
            _fileCache = new AsyncFileCache(directory, format, cacheExpireTime);
        }

        public AsyncFileTileProvider(AsyncFileCache fileCache)
        {
            _fileCache = fileCache;
        }

        public byte[] GetTile(TileInfo tileInfo)
        {
            byte[] bytes = _fileCache.Find(tileInfo.Index);
            if (bytes == null) throw new FileNotFoundException("The tile was not found at it's expected location");
            return bytes;
        }

        public System.Threading.Tasks.Task<byte[]> GetTileAsync(TileInfo tileInfo)
        {
            return _fileCache.FindAsync(tileInfo.Index)
                .ContinueWith(bytesTask =>
                {
                    var bytes = bytesTask.Result;
                    if (bytes == null) throw new FileNotFoundException("The tile was not found at it's expected location");
                    return bytes;
                });
        }
    }
}
