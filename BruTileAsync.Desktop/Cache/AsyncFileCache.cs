using BruTile.Locks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Cache
{
    [Serializable]
    public class AsyncFileCache : IAsyncPersistentCache<byte[]>
    {
        private readonly AsyncReaderWriterLock _rwLock = new AsyncReaderWriterLock();
        private readonly string _directory;
        private readonly string _format;
        private readonly TimeSpan _cacheExpireTime = TimeSpan.Zero;
        private static readonly TaskCompletionSource<bool> CompletedTaskSource = new TaskCompletionSource<bool>();

        static AsyncFileCache()
        {
            CompletedTaskSource.SetResult(false);
        }

        /// <remarks>
        ///   The constructor creates the storage _directory if it does not exist.
        /// </remarks>
        public AsyncFileCache(string directory, string format, TimeSpan cacheExpireTime)
        {
            _directory = directory;
            _format = format;
            _cacheExpireTime = cacheExpireTime;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <remarks>
        /// The constructor creates the storage _directory if it does not exist.
        /// </remarks>
        public AsyncFileCache(string directory, string format)
            : this(directory, format, TimeSpan.Zero)
        {
        }

        public void Add(TileIndex index, byte[] tile)
        {
            using (var releaser = _rwLock.WriterLock())
            {
                var directoryName = GetDirectoryName(index);
                var fileInfo = new FileInfo(Path.Combine(directoryName, GetSimpleFileName(index)));
                if (Exists(fileInfo))
                {
                    return; // ignore
                }
                if (!Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                WriteToFile(tile, fileInfo);
            }
        }

        public void Remove(TileIndex index)
        {
            using (var releaser = _rwLock.WriterLock())
            {
                var fileInfo = new FileInfo(GetFileName(index));
                if (Exists(fileInfo))
                {
                    fileInfo.Delete();
                }
            }
        }

        public byte[] Find(TileIndex index)
        {
            using (var releaser = _rwLock.ReaderLock())
            {
                var fileInfo = new FileInfo(GetFileName(index));
                if (!Exists(fileInfo))
                    return null;
                return ReadFromFile(fileInfo);
            }
        }

        public async Task AddAsync(TileIndex index, byte[] tile)
        {
            using (var releaser = await _rwLock.WriterLockAsync().ConfigureAwait(false))
            {
                var directoryName = GetDirectoryName(index);
                var fileInfo = new FileInfo(Path.Combine(directoryName, GetSimpleFileName(index)));
                if (Exists(fileInfo))
                {
                    return; // ignore
                }
                if (!Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                await WriteToFileAsync(tile, fileInfo).ConfigureAwait(false);
            }
        }

        public async Task RemoveAsync(TileIndex index)
        {
            using (var releaser = await _rwLock.WriterLockAsync().ConfigureAwait(false))
            {
                var fileInfo = new FileInfo(GetFileName(index));
                if (Exists(fileInfo))
                {
                    fileInfo.Delete();
                }
            }
        }

        public async Task<byte[]> FindAsync(TileIndex index)
        {
            using (var releaser = await _rwLock.ReaderLockAsync().ConfigureAwait(false))
            {
                var fileInfo = new FileInfo(GetFileName(index));
                if (!Exists(fileInfo))
                    return null;
                return await ReadFromFileAsync(fileInfo).ConfigureAwait(false);
            }
        }

        private string GetDirectoryName(TileIndex index)
        {
            return Path.Combine(_directory,
                index.Level.ToString(CultureInfo.InvariantCulture).Replace(':', '_'),
                index.Col.ToString(CultureInfo.InvariantCulture));
        }

        private string GetSimpleFileName(TileIndex index)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", index.Row, _format);
        }

        private string GetFileName(TileIndex index)
        {
            return Path.Combine(GetDirectoryName(index), GetSimpleFileName(index));
        }

        private bool Exists(FileInfo fileInfo)
        {
            if (fileInfo.Exists)
            {
                return _cacheExpireTime == TimeSpan.Zero || (DateTime.Now - fileInfo.LastWriteTime) <= _cacheExpireTime;
            }
            return false;
        }

        private void WriteToFile(byte[] tile, FileInfo fileInfo)
        {
            using (var fileStream = new FileStream(fileInfo.FullName, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                fileStream.Write(tile, 0, tile.Length);
                fileStream.Flush();
                fileStream.Close();
            }
        }

        private async Task WriteToFileAsync(byte[] tile, FileInfo fileInfo)
        {
            using (var fileStream = new FileStream(fileInfo.FullName, FileMode.Create, FileAccess.Write, FileShare.Write, 4096, FileOptions.Asynchronous))
            {
                await fileStream.WriteAsync(tile, 0, tile.Length).ConfigureAwait(false);
                await fileStream.FlushAsync().ConfigureAwait(false);
                fileStream.Close();
            }
        }

        private byte[] ReadFromFile(FileInfo fileInfo)
        {
            return File.ReadAllBytes(fileInfo.FullName);
        }

        private async Task<byte[]> ReadFromFileAsync(FileInfo fileInfo)
        {
            var buffer = new byte[fileInfo.Length];
            int offset = 0;
            int count = buffer.Length;
            using (var fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
            {
                // read until there are no more bytes to read
                while ((count -= await fileStream.ReadAsync(buffer, offset, count).ConfigureAwait(false)) > 0)
                {
                    offset = buffer.Length - count;
                }
                fileStream.Close();
            }
            return buffer;
        }
    }
}
