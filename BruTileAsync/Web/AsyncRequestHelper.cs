using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BruTile.Web
{
    public static class AsyncRequestHelper
    {
        static AsyncRequestHelper()
        {
            Timeout = 10000;
        }

        public static int Timeout { get; set; }

        public static ICredentials Credentials { get; set; }

        public static async Task<byte[]> FetchImageAsync(Uri uri)
        {
            CancellationTokenSource fetchCancelSource = new CancellationTokenSource();
            var fetchTask = FetchImageAsyncCore(uri, fetchCancelSource.Token);

            if (fetchTask.IsCompleted || Timeout == System.Threading.Timeout.Infinite)
            {
                // Either the task has already completed or timeout will never occur.
                // No proxy necessary.
                return await fetchTask.ConfigureAwait(false);
            }

            CancellationTokenSource delayCancelSource = new CancellationTokenSource();
            var timeoutTask = Task.Delay(Timeout, delayCancelSource.Token);

            var completedTask = await Task.WhenAny(fetchTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == fetchTask)
            {
                delayCancelSource.Cancel();
                return await fetchTask.ConfigureAwait(false);
            }
            else
            {
                fetchCancelSource.Cancel();
                throw new TimeoutException("No response received in time.");
            }
        }

        private static async Task<byte[]> FetchImageAsyncCore(Uri uri, CancellationToken cancellationToken)
        {
            using (var handler = new HttpClientHandler())
            {
                if (Credentials != null)
                {
                    handler.Credentials = Credentials;
                }
                else
                {
                    handler.UseDefaultCredentials = true;
                }
                using(var client = new HttpClient(handler))
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    return await FetchImageAsync(client, request, cancellationToken);
                }
            }
        }

        public static async Task<byte[]> FetchImageAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (response.Content.Headers.ContentType.MediaType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
                {
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                throw new WebResponseFormatException(await ComposeErrorMessage(response, response.RequestMessage.RequestUri.AbsoluteUri).ConfigureAwait(false));
            }
        }

        private static async Task<string> ComposeErrorMessage(HttpResponseMessage httpResponse, string uri)
        {
            string message = String.Format(
                CultureInfo.InvariantCulture,
                "Failed to retrieve tile from this uri:\n{0}\n.An image was expected but the received type was '{1}'.",
                uri,
                httpResponse.Content.Headers.ContentType
            );

            if (httpResponse.Content.Headers.ContentType.MediaType.StartsWith("text", StringComparison.OrdinalIgnoreCase))
            {
                message += String.Format(CultureInfo.InvariantCulture,
                  "\nThis was returned:\n{0}", await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            return message;
        }
    }
}
