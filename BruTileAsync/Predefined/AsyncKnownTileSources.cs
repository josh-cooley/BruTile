using BruTile.Cache;
using BruTile.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Predefined
{
    public static class AsyncKnownTileSources
    {
        /// <summary>
        /// Static factory method for known tile services
        /// </summary>
        /// <param name="source">The source</param>
        /// <param name="apiKey">An (optional) API key</param>
        /// <param name="persistentCache">A place to permanently store tiles (file of database)</param>
        /// <param name="tileFetcher">Option to override the web request</param>
        /// <returns>The tile source</returns>
        public static ITileSourceAsync Create(KnownTileSource source = KnownTileSource.OpenStreetMap, string apiKey = null,
            IAsyncPersistentCache<byte[]> persistentCache = null, Func<Uri, Task<byte[]>> tileFetcher = null)
        {
            var knownSource = KnownTileSources.Create(source, apiKey);
            var request = knownSource as IRequest;
            if (request == null)
                throw new NotSupportedException("KnownTileSource not known");
            return new AsyncHttpTileSource(knownSource.Schema, request, knownSource.Name, persistentCache, tileFetcher);
        }
    }
}
