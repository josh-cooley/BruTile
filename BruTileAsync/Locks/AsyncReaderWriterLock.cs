using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BruTile.Locks
{
    class AsyncReaderWriterLock
    {
        private readonly Releaser _readerReleaser;
        private readonly Releaser _writerReleaser;
        private readonly Task<Releaser> _readerReleaserTask;
        private readonly Task<Releaser> _writerReleaserTask;

        private readonly Queue<TaskCompletionSource<Releaser>> _waitingWriters =
            new Queue<TaskCompletionSource<Releaser>>();
        private TaskCompletionSource<Releaser> _waitingReader =
            new TaskCompletionSource<Releaser>();
        private int _readersWaiting;

        private int _status;

        public AsyncReaderWriterLock()
        {
            _readerReleaser = new Releaser(this, writer: false);
            _writerReleaser = new Releaser(this, writer: true);
            _readerReleaserTask = Task.FromResult(_readerReleaser);
            _writerReleaserTask = Task.FromResult(_writerReleaser);
        }

        public Task<Releaser> ReaderLockAsync()
        {
            lock (_waitingWriters)
            {
                if (_status >= 0 && _waitingWriters.Count == 0)
                {
                    ++_status;
                    return _readerReleaserTask;
                }
                else
                {
                    ++_readersWaiting;
                    return _waitingReader.Task.ContinueWith(t => t.Result);
                }
            }
        }

        public Releaser ReaderLock()
        {
            Task<Releaser> waitingReader = null;
            lock(_waitingWriters)
            {
                if (_status >= 0 && _waitingWriters.Count == 0)
                {
                    ++_status;
                    return _readerReleaser;
                }
                else
                {
                    ++_readersWaiting;
                    waitingReader = _waitingReader.Task.ContinueWith(t => t.Result);
                }
            }
            waitingReader.Wait();
            return waitingReader.Result;
        }

        public Task<Releaser> WriterLockAsync()
        {
            lock (_waitingWriters)
            {
                if (_status == 0)
                {
                    _status = -1;
                    return _writerReleaserTask;
                }
                else
                {
                    var waiter = new TaskCompletionSource<Releaser>();
                    _waitingWriters.Enqueue(waiter);
                    return waiter.Task;
                }
            }
        }

        public Releaser WriterLock()
        {
            TaskCompletionSource<Releaser> waiter = null;
            lock (_waitingWriters)
            {
                if (_status == 0)
                {
                    _status = -1;
                    return _writerReleaser;
                }
                else
                {
                    waiter = new TaskCompletionSource<Releaser>();
                    _waitingWriters.Enqueue(waiter);
                }
            }
            waiter.Task.Wait();
            return waiter.Task.Result;
        }

        private void ReaderRelease()
        {
            TaskCompletionSource<Releaser> toWake = null;

            lock (_waitingWriters)
            {
                --_status;
                if (_status == 0 && _waitingWriters.Count > 0)
                {
                    _status = -1;
                    toWake = _waitingWriters.Dequeue();
                }
            }

            if (toWake != null)
                toWake.SetResult(new Releaser(this, true));
        }

        private void WriterRelease()
        {
            TaskCompletionSource<Releaser> toWake = null;
            bool toWakeIsWriter = false;

            lock (_waitingWriters)
            {
                if (_waitingWriters.Count > 0)
                {
                    toWake = _waitingWriters.Dequeue();
                    toWakeIsWriter = true;
                }
                else if (_readersWaiting > 0)
                {
                    toWake = _waitingReader;
                    _status = _readersWaiting;
                    _readersWaiting = 0;
                    _waitingReader = new TaskCompletionSource<Releaser>();
                }
                else _status = 0;
            }

            if (toWake != null)
                toWake.SetResult(new Releaser(this, toWakeIsWriter));
        }

        public struct Releaser : IDisposable
        {
            private readonly AsyncReaderWriterLock m_toRelease;
            private readonly bool m_writer;

            internal Releaser(AsyncReaderWriterLock toRelease, bool writer)
            {
                m_toRelease = toRelease;
                m_writer = writer;
            }

            public void Dispose()
            {
                if (m_toRelease != null)
                {
                    if (m_writer) m_toRelease.WriterRelease();
                    else m_toRelease.ReaderRelease();
                }
            }
        }
    }
}
