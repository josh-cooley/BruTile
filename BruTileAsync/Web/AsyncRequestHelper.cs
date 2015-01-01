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
        private static readonly HttpClientHandler _clientHandler;
        private static readonly HttpClient _client;

        static AsyncRequestHelper()
        {
            Timeout = 10000;
            _clientHandler = new HttpClientHandler();
            _client = new HttpClient(_clientHandler);
        }

        public static int Timeout { get; set; }

        public static ICredentials Credentials
        {
            get { return _clientHandler.Credentials; }
            set
            {
                _clientHandler.Credentials = value;
                // use default credentials when value is null
                _clientHandler.UseDefaultCredentials = (value == null);
            }
        }

        public static Task<byte[]> FetchImageAsync(Uri uri)
        {
            return FetchImageAsync(uri, CancellationToken.None);
        }

        public static async Task<byte[]> FetchImageAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (cancellationToken == null)
                throw new ArgumentNullException("cancellationToken");

            var timeout = Timeout;
            if (timeout == System.Threading.Timeout.Infinite)
            {
                return await CreateRequestAndFetchImageAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using (var timeoutCancelSource = new CancellationTokenSource(timeout))
                {
                    try
                    {
                        if (cancellationToken == CancellationToken.None)
                        {
                            return await CreateRequestAndFetchImageAsync(uri, timeoutCancelSource.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            // must merge cancellation tokens
                            using (var mergeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCancelSource.Token, cancellationToken))
                            {
                                return await CreateRequestAndFetchImageAsync(uri, mergeTokenSource.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (OperationCanceledException oce)
                    {
                        if (oce.CancellationToken == timeoutCancelSource.Token)
                        {
                            throw new TimeoutException("No response received in time.", oce);
                        }
                        throw;
                    }
                }
            }
        }

        private static async Task<byte[]> CreateRequestAndFetchImageAsync(Uri uri, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                return await FetchImageAsync(request, cancellationToken);
            }
        }

        public static Task<byte[]> FetchImageAsync(HttpRequestMessage request)
        {
            return FetchImageAsync(request, CancellationToken.None);
        }

        public static async Task<byte[]> FetchImageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
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
