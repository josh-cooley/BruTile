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
        private const int MaxThreads = 4;
        private readonly AutoResetEvent _waitHandle = new AutoResetEvent(false);
        private readonly IFetchStrategy _strategy = new FetchStrategy();
        private volatile bool _isAborted;
        private volatile bool _isViewChanged;
        private readonly Retries _retries = new Retries();

        public event DataChangedEventHandler<T> DataChanged;

        public AsyncFetcher(ITileSourceAsync tileSource, ITileCache<Tile<T>> memoryCache)
        {
            if (tileSource == null) throw new ArgumentException("TileProvider can not be null");
            _tileSource = tileSource;

            if (memoryCache == null) throw new ArgumentException("MemoryCache can not be null");
            _memoryCache = memoryCache;

            //StartFetchLoop();
        }

        public void ViewChanged(Extent newExtent, double newResolution)
        {
            _extent = newExtent;
            _resolution = newResolution;
            _isViewChanged = true;
            //_waitHandle.Set();
            StartFetch();
        }

        private void StartFetchLoop()
        {
            ThreadPool.QueueUserWorkItem(FetchLoop);
        }

        private void StartFetch()
        {
            DoFetch();
        }

        public void AbortFetch()
        {
            _isAborted = true;
            //_waitHandle.Set(); // active fetch loop so it can run out of the loop
        }

        private void FetchLoop(object state)
        {
            IEnumerable<TileInfo> tilesWanted = null;

            while (!_isAborted)
            {
                _waitHandle.WaitOne();

                if (_tileSource.Schema == null)
                {
                    _waitHandle.Reset();    // set in wait mode 
                    continue;              // and go to begin of loop to wait
                }

                if (_isViewChanged || tilesWanted == null)
                {
                    var levelId = Utilities.GetNearestLevel(_tileSource.Schema.Resolutions, _resolution);
                    tilesWanted = _strategy.GetTilesWanted(_tileSource.Schema, _extent, levelId);
                    _retries.Clear();
                    _isViewChanged = false;
                }

                var tilesMissing = GetTilesMissing(tilesWanted, _memoryCache, _retries);

                FetchTiles(tilesMissing);

                if (tilesMissing.Count == 0) { _waitHandle.Reset(); }

                if (_tilesInProgress.Count >= MaxThreads) { _waitHandle.Reset(); }
            }
        }

        private void DoFetch()
        {
            IEnumerable<TileInfo> tilesWanted = null;

            if (_tileSource.Schema == null)
            {
                return;
            }

            if (_isViewChanged || tilesWanted == null)
            {
                var levelId = Utilities.GetNearestLevel(_tileSource.Schema.Resolutions, _resolution);
                tilesWanted = _strategy.GetTilesWanted(_tileSource.Schema, _extent, levelId);
                _retries.Clear();
                _isViewChanged = false;
            }

            var tilesMissing = GetTilesMissing(tilesWanted, _memoryCache, _retries);

            DoFetchTiles(tilesMissing);
        }

        private static IList<TileInfo> GetTilesMissing(IEnumerable<TileInfo> tilesWanted,
            ITileCache<Tile<T>> memoryCache, Retries retries)
        {
            return tilesWanted.Where(
                info => memoryCache.Find(info.Index) == null &&
                !retries.ReachedMax(info.Index)).ToList();
        }

        private void FetchTiles(IEnumerable<TileInfo> tilesMissing)
        {
            foreach (TileInfo info in tilesMissing)
            {
                if (_tilesInProgress.Count >= MaxThreads) return;
                FetchTile(info);
            }
        }

        private void DoFetchTiles(IEnumerable<TileInfo> tilesMissing)
        {
            foreach (TileInfo info in tilesMissing)
            {
                if (_isAborted)
                    return;
                DoFetchTile(info);
            }
        }

        private void FetchTile(TileInfo info)
        {
            // first some checks
            if (_tilesInProgress.Contains(info.Index)) return;
            if (_retries.ReachedMax(info.Index)) return;

            // prepare for request
            lock (_tilesInProgress) { _tilesInProgress.Add(info.Index); }
            _retries.PlusOne(info.Index);

            // now we can go for the request.
            var task = FetchAsync(info);
        }

        private void DoFetchTile(TileInfo info)
        {
            // first some checks
            if (_tilesInProgress.Contains(info.Index)) return;
            if (_retries.ReachedMax(info.Index)) return;

            // prepare for request
            lock (_tilesInProgress) { _tilesInProgress.Add(info.Index); }
            _retries.PlusOne(info.Index);

            // now we can go for the request.
            var task = DoFetchAsync(info);
        }

        private async Task FetchAsync(TileInfo tileInfo)
        {
            Exception error = null;
            Tile<T> tile = null;

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

            _waitHandle.Set();

            if (DataChanged != null && !_isAborted)
                DataChanged(this, new DataChangedEventArgs<T>(error, false, tile));
        }

        private async Task DoFetchAsync(TileInfo tileInfo)
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