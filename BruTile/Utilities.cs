// Copyright (c) BruTile developers team. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BruTile
{
    public static class Utilities
    {
        /// <summary>
        ///   Reads data from a stream until the end is reached. The
        ///   data is returned as a byte array. An IOException is
        ///   thrown if any of the underlying IO calls fail.
        /// </summary>
        /// <param name = "stream">The stream to read data from</param>
        public static byte[] ReadFully(Stream stream)
        {
            //thanks to: http://www.yoda.arachsys.com/csharp/readbinary.html
            var buffer = new byte[32768];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        return ms.ToArray();
                    }
                    ms.Write(buffer, 0, read);
                }
            }
        }

        public static Task<byte[]> ReadBytesAsync(Stream stream, int count)
        {
            var bytes = new byte[count];
            return ReadBytesCoreAsync(stream, bytes, 0, count)
                .ContinueWith(ti =>
                    {
                        System.Diagnostics.Debug.Assert(ti.Result == count);
                        return bytes;
                    });
        }

        private static Task<int> ReadBytesCoreAsync(Stream stream, byte[] buffer, int offset, int count)
        {
            return ReadStreamAsync(stream, buffer, offset, count)
                .Then(read =>
            {
                if (offset + read == count)
                    return TaskFromValue(read);
                else
                    return ReadBytesCoreAsync(stream, buffer, offset + read, count - read).ContinueWith(ti => read + ti.Result);
            });
        }

        private static Task<int> ReadStreamAsync(Stream stream, byte[] buffer, int offset, int count)
        {
            return Task.Factory.FromAsync(
                new Func<byte[], int, int, AsyncCallback, object, IAsyncResult>(stream.BeginRead),
                new Func<IAsyncResult, int>(stream.EndRead),
                buffer, offset, count, null);
        }

        public static Task<T2> Then<T1, T2>(this Task<T1> first, Func<T1, Task<T2>> next)
        {
            // http://blogs.msdn.com/b/pfxteam/archive/2010/11/21/10094564.aspx
            if (first == null) throw new ArgumentNullException("first");
            if (next == null) throw new ArgumentNullException("next");

            var tcs = new TaskCompletionSource<T2>();
            first.ContinueWith(delegate
            {
                if (first.IsFaulted) tcs.TrySetException(first.Exception.InnerExceptions);
                else if (first.IsCanceled) tcs.TrySetCanceled();
                else
                {
                    try
                    {
                        var t = next(first.Result);
                        if (t == null) tcs.TrySetCanceled();
                        else t.ContinueWith(delegate
                        {
                            if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerExceptions);
                            else if (t.IsCanceled) tcs.TrySetCanceled();
                            else tcs.TrySetResult(t.Result);
                        }, TaskContinuationOptions.ExecuteSynchronously);
                    }
                    catch (Exception exc) { tcs.TrySetException(exc); }
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
            return tcs.Task;
        }

        public static Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout, Action timeoutAction)
        {
            //http://blogs.msdn.com/b/pfxteam/archive/2011/11/10/10235834.aspx
            // couldn't use the overload of ContinueWith suggested in footnote #2
            // because we can't pass a state variable.  Using capture instead.

            // Short-circuit #1: infinite timeout or task already completed
            if (task.IsCompleted || (millisecondsTimeout == Timeout.Infinite))
            {
                // Either the task has already completed or timeout will never occur.
                // No proxy necessary.
                return task;
            }

            // tcs.Task will be returned as a proxy to the caller
            TaskCompletionSource<TResult> tcs =
                new TaskCompletionSource<TResult>();

            // Short-circuit #2: zero timeout
            if (millisecondsTimeout == 0)
            {
                // We've already timed out.
                tcs.SetException(new TimeoutException());
                return tcs.Task;
            }

            // Set up a timer to complete after the specified timeout period
            Timer timer = new Timer(state =>
            {
                // alert the caller that a timeout has occured, allowing possible cleanup.
                timeoutAction();

                // Recover your state information
                var myTcs = (TaskCompletionSource<TResult>)state;

                // Fault our proxy with a TimeoutException
                myTcs.TrySetException(new TimeoutException());
            }, tcs, millisecondsTimeout, Timeout.Infinite);

            // Wire up the logic for what happens when source task completes
            task.ContinueWith(antecedent =>
            {
                // Cancel the Timer
                timer.Dispose();

                // Marshal results to proxy
                MarshalTaskResults(antecedent, tcs);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

            return tcs.Task;
        }

        internal static void MarshalTaskResults<TResult>(
            Task source, TaskCompletionSource<TResult> proxy)
        {
            switch (source.Status)
            {
                case TaskStatus.Faulted:
                    proxy.TrySetException(source.Exception);
                    break;
                case TaskStatus.Canceled:
                    proxy.TrySetCanceled();
                    break;
                case TaskStatus.RanToCompletion:
                    Task<TResult> castedSource = source as Task<TResult>;
                    proxy.TrySetResult(
                        castedSource == null ? default(TResult) : // source is a Task
                            castedSource.Result); // source is a Task<TResult>
                    break;
            }
        }

        internal static Task<T> TaskFromValue<T>(T value)
        {
            var taskSource = new System.Threading.Tasks.TaskCompletionSource<T>();
            taskSource.SetResult(value);
            return taskSource.Task;
        }

        public static string GetNearestLevel(IDictionary<string, Resolution> resolutions, double resolution)
        {
            if (resolutions.Count == 0)
            {
                throw new ArgumentException("No tile resolutions");
            }

            var localResolutions = resolutions.OrderByDescending(r => r.Value.UnitsPerPixel);

            //smaller than smallest
            if (localResolutions.Last().Value.UnitsPerPixel > resolution) return localResolutions.Last().Key;

            //bigger than biggest
            if (localResolutions.First().Value.UnitsPerPixel < resolution) return localResolutions.First().Key;

            string result = null;
            double resultDistance = double.MaxValue;
            foreach (var current in localResolutions)
            {
                double distance = Math.Abs(current.Value.UnitsPerPixel - resolution);
                if (distance < resultDistance)
                {
                    result = current.Key;
                    resultDistance = distance;
                }
            }
            if (result == null) throw new Exception("Unexpected error when calculating nearest level");
            return result;
        }

        public static string Version
        {
            get
            {
                string name = typeof(Utilities).Assembly.FullName;
                var asmName = new AssemblyName(name);
                return asmName.Version.Major + "." + asmName.Version.Minor;
            }
        }

        public static string DefaultUserAgent { get { return "BruTile/" + Version; } }

        public static string DefaultReferer { get { return string.Empty; } }
    }
}