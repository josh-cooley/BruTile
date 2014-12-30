using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Cache
{
    class AsyncNullCache : IAsyncPersistentCache<byte[]>
    {
        public void Add(TileIndex index, byte[] tile)
        {
            // do nothing
        }

        public void Remove(TileIndex index)
        {
            throw new NotImplementedException();
        }

        public byte[] Find(TileIndex index)
        {
            return null;
        }

        public Task AddAsync(TileIndex index, byte[] tile)
        {
            // do nothing
            return Task.FromResult<object>(null);
        }

        public Task RemoveAsync(TileIndex index)
        {
            throw new NotImplementedException();
        }

        public Task<byte[]> FindAsync(TileIndex index)
        {
            return Task.FromResult<byte[]>(null);
        }
    }
}
