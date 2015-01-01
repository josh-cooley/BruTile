﻿// Copyright 2008 - Paul den Dulk (Geodan)

using BruTile.Cache;
using BruTile.Samples.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BruTile.Demo
{
    public class AsyncFetcher<T>
    {
        private readonly ITileCache<Tile<T>> _memoryCache;
        private readonly ITileSourceAsync _tileSource;
        private Extent _extent;
        private double _resolution;
        private readonly IList<TileIndex> _tilesInProgress = new List<TileIndex>();
        private readonly IFetchStrategy _strategy = new FetchStrategy();
        private volatile bool _isAborted;
        private readonly Retries _retries = new Retries();

        public event DataChangedEventHandler<T> DataChanged;

        public AsyncFetcher(ITileSourceAsync tileSource, ITileCache<Tile<T>> memoryCache)
        {
            if (tileSource == null) throw new ArgumentException("TileProvider can not be null");
            _tileSource = tileSource;

            if (memoryCache == null) throw new ArgumentException("MemoryCache can not be null");
            _memoryCache = memoryCache;
        }

        public void ViewChanged(Extent newExtent, double newResolution)
        {
            _extent = newExtent;
            _resolution = newResolution;
            StartFetch();
        }

        private void StartFetch()
        {
            Task.Run(() => Fetch());
        }

        public void AbortFetch()
        {
            _isAborted = true;
        }

        private async Task Fetch()
        {
            if (_tileSource.Schema == null)
            {
                return;
            }

            var requestedResolution = _resolution;
            var requestedExtent = _extent;

            var levelId = Utilities.GetNearestLevel(_tileSource.Schema.Resolutions, requestedResolution);
            var tilesWanted = _strategy.GetTilesWanted(_tileSource.Schema, requestedExtent, levelId);
            _retries.Clear();

            var tilesMissing = GetTilesMissing(tilesWanted, _memoryCache, _retries);
            while (!_isAborted &&
                tilesMissing.Count != 0 &&
                requestedResolution == _resolution &&
                requestedExtent == _extent)
            {
                // wait for all fetches to complete so that we can check if we need to try again on missing tiles
                await FetchTiles(tilesMissing);
                // check again to see if we are missing anything.
                tilesMissing = GetTilesMissing(tilesWanted, _memoryCache, _retries);
            }
        }

        private static IList<TileInfo> GetTilesMissing(IEnumerable<TileInfo> tilesWanted,
            ITileCache<Tile<T>> memoryCache, Retries retries)
        {
            return tilesWanted.Where(
                info => memoryCache.Find(info.Index) == null &&
                !retries.ReachedMax(info.Index)).ToList();
        }

        private Task FetchTiles(IEnumerable<TileInfo> tilesMissing)
        {
            return Task.WhenAll(tilesMissing.Select(FetchTile));
        }

        private async Task FetchTile(TileInfo info)
        {
            // first some checks
            if (_isAborted) return;
            if (_tilesInProgress.Contains(info.Index)) return;
            if (_retries.ReachedMax(info.Index)) return;

            // prepare for request
            lock (_tilesInProgress) { _tilesInProgress.Add(info.Index); }
            _retries.PlusOne(info.Index);

            // now we can go for the request.
            await FetchAsync(info);
        }

        private async Task FetchAsync(TileInfo tileInfo)
        {
            Exception error = null;
            Tile<T> tile = null;

            if (_isAborted)
                return;

            try
            {
                if (_tileSource != null)
                {
                    byte[] data = await _tileSource.GetTileAsync(tileInfo);
                    tile = new Tile<T> { Data = data, Info = tileInfo };
                }
            }
            catch (Exception ex) //This may seem a bit weird. We catch the exception to pass it as an argument. This is because we are on a worker thread here, we cannot just let it fall through. 
            {
                error = ex;
            }

            lock (_tilesInProgress)
            {
                if (_tilesInProgress.Contains(tileInfo.Index))
                    _tilesInProgress.Remove(tileInfo.Index);
            }

            if (DataChanged != null && !_isAborted)
                DataChanged(this, new DataChangedEventArgs<T>(error, false, tile));
        }

        /// <summary>
        /// Keeps track of retries per tile. This class doesn't do much interesting work
        /// but makes the rest of the code a bit easier to read.
        /// </summary>
        class Retries
        {
            private readonly IDictionary<TileIndex, int> _retries = new Dictionary<TileIndex, int>();
            private const int MaxRetries = 0;

            public bool ReachedMax(TileIndex index)
            {
                int retryCount = (!_retries.Keys.Contains(index)) ? 0 : _retries[index];
                return retryCount > MaxRetries;
            }

            public void PlusOne(TileIndex index)
            {
                if (!_retries.Keys.Contains(index)) _retries.Add(index, 0);
                else _retries[index]++;
            }

            public void Clear()
            {
                _retries.Clear();
            }
        }


        interface IFetchStrategy
        {
            IList<TileInfo> GetTilesWanted(ITileSchema schema, Extent extent, string levelId);
        }

        class FetchStrategy : IFetchStrategy
        {
            public static BruTile.Samples.Common.HashSet<int> GetPreFetchLevels(int min, int max)
            {
                var preFetchLayers = new BruTile.Samples.Common.HashSet<int>();
                int level = min;
                var step = 1;
                while (level <= max)
                {
                    preFetchLayers.Add(level);
                    level += step;
                    step++;
                }
                return preFetchLayers;
            }

            public IList<TileInfo> GetTilesWanted(ITileSchema schema, Extent extent, string levelId)
            {
                IList<TileInfo> infos = new List<TileInfo>();
                // Iterating through all levels from current to zero. If lower levels are
                // not availeble the renderer can fall back on higher level tiles. 
                var resolution = schema.Resolutions[levelId].UnitsPerPixel;
                var levels = schema.Resolutions.Where(k => resolution <= k.Value.UnitsPerPixel).OrderByDescending(x => x.Value.UnitsPerPixel);

                //var levelCount = levels.Count();
                foreach (var level in levels)
                {
                    var tileInfos = schema.GetTileInfos(extent, level.Key);
                    tileInfos = SortByPriority(tileInfos, extent.CenterX, extent.CenterY);

                    //var count = infosOfLevel.Count();
                    foreach (var info in tileInfos)
                    {
                        if ((info.Index.Row >= 0) && (info.Index.Col >= 0)) infos.Add(info);
                    }
                }

                return infos;
            }

            private static IEnumerable<TileInfo> SortByPriority(IEnumerable<TileInfo> tiles, double centerX, double centerY)
            {
                return tiles.OrderBy(t => Distance(centerX, centerY, t.Extent.CenterX, t.Extent.CenterY));
            }

            public static double Distance(double x1, double y1, double x2, double y2)
            {
                return Math.Sqrt(Math.Pow(x1 - x2, 2.0) + Math.Pow(y1 - y2, 2.0));
            }
        }
    }
}