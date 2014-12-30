using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Cache
{
    public interface IAsyncPersistentCache<T> : IPersistentCache<T>, IAsyncTileCache<T>
    {
    }
}
