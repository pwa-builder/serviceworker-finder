using Polly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Common
{
    public static class HttpClientExtensions
    {
        private const string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.110 Safari/537.36 Edg/96.0.1054.57 PWABuilderHttpAgent"; // Note: this should include PWABuilderHttpAgent, as Cloudflare has whitelisted this UA

        /// <summary>
        /// Creates an HttpClient with PWABuilder's user agent.
        /// </summary>
        /// <param name="factory"></param>
        /// <returns></returns>
        public static HttpClient CreateClientWithUserAgent(this IHttpClientFactory factory)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        /// <summary>
        /// Does an HTTP GET with a specified timeout.
        /// </summary>
        /// <param name="http"></param>
        /// <param name="uri"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        /// <exception cref="TimeoutException">Thrown when the timeout expires.</exception>
        public static async Task<string> GetStringAsync(this HttpClient http, Uri? uri, TimeSpan timeout, CancellationToken cancelToken)
        {
            // If we were given CancellationToken.None, then use our own.
            var timeoutCancelTokenSrc = default(CancellationTokenSource);
            if (cancelToken == CancellationToken.None)
            {
                timeoutCancelTokenSrc = new CancellationTokenSource();
                cancelToken = timeoutCancelTokenSrc.Token;
            }

            try
            {
                return await Policy.TimeoutAsync(timeout)
                    .ExecuteAsync(async token => await http.GetStringAsync(uri, token), cancelToken);
            }
            catch (Polly.Timeout.TimeoutRejectedException timeoutError)
            {
                timeoutCancelTokenSrc?.Cancel();
                throw new TimeoutException(timeoutError.Message, timeoutError);
            }
        }

        /// <summary>
        /// Sends an HTTP request with a timeout.
        /// </summary>
        /// <param name="http"></param>
        /// <param name="message"></param>
        /// <param name="timeout"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TimeoutException">Thrown when the timeout expires.</exception>
        public static async Task<HttpResponseMessage> SendAsync(this HttpClient http, HttpRequestMessage message, TimeSpan timeout, CancellationToken cancelToken)
        {
            // If we were given CancellationToken.None, then use our own.
            var timeoutCancelTokenSrc = default(CancellationTokenSource);
            if (cancelToken == CancellationToken.None)
            {
                timeoutCancelTokenSrc = new CancellationTokenSource();
                cancelToken = timeoutCancelTokenSrc.Token;
            }

            try
            {
                return await Policy.TimeoutAsync(timeout)
                    .ExecuteAsync(async token => await http.SendAsync(message, token), cancelToken);
            }
            catch (Polly.Timeout.TimeoutRejectedException timeoutError)
            {
                timeoutCancelTokenSrc?.Cancel();
                throw new TimeoutException(timeoutError.Message, timeoutError);
            }
        }
    }
}
