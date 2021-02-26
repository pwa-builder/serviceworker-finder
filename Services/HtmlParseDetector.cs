using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Polly;
using PWABuilder.ServiceWorkerDetector.Common;
using PWABuilder.ServiceWorkerDetector.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Services
{
    /// <summary>
    /// Service that uses HTML parsing to detect a service worker. It parses the HTML of the page, looking for service worker registration and a valid URL to a service worker.
    /// </summary>
    /// <remarks>
    /// In our testing, parsing the HTML to find the service worker doesn't work for many sites. Many sites have service worker registration buried deep beneath one or more scripts.
    /// </remarks>
    public class HtmlParseDetector
    {
        private readonly HttpClient http;
        private readonly ServiceWorkerCodeAnalyzer swAnalyzer;
        private readonly ILogger<HtmlParseDetector> logger;

        private static readonly Regex swRegex = new Regex("navigator\\s*.\\s*serviceWorker\\s*.\\s*register\\(['|\"]([^'\"]+)['|\"]");
        private const string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/88.0.4324.182 Safari/537.36 Edg/88.0.705.74";
        private const string serviceWorkerNameFallback = "/dynamically-generated";
        private static readonly TimeSpan httpTimeout = TimeSpan.FromSeconds(5);

        public HtmlParseDetector(
            IHttpClientFactory httpClientFactory,
            ServiceWorkerCodeAnalyzer swAnalyzer,
            ILogger<HtmlParseDetector> logger)
        {
            this.http = httpClientFactory.CreateClient();
            this.http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            this.swAnalyzer = swAnalyzer;
            this.logger = logger;
        }

        public async Task<ServiceWorkerDetectionResult> Run(Uri uri)
        {
            try
            {
                return await RunCore(uri);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Error running HTML parse service worker detector for {uri}", uri);
                return new ServiceWorkerDetectionResult
                {
                    NoServiceWorkerFoundDetails = "Error running HTML parse service worker detector: " + error.ToString()
                };
            }
        }

        private async Task<ServiceWorkerDetectionResult> RunCore(Uri uri)
        {
            var pageHtml = await GetPageHtml(uri);
            var htmlDoc = GetDocument(pageHtml);

            // Find the service worker inside the HTML of the page.
            var swUrl = await GetServiceWorkerUrlFromDoc(uri, htmlDoc) ??
                await GetServiceWorkerUrlFromScripts(uri, htmlDoc) ?? // Can't find sw reg in the HTML doc? Search its scripts.
                await GetServiceWorkerUrlFromCommonFileNames(uri); // Still can't find sw reg? Take a guess at some common SW URLs.

            var swUri = GetAbsoluteUri(uri, swUrl);
            var swScript = await TryGetScriptContents(swUri, CancellationToken.None) ?? string.Empty;
            return new ServiceWorkerDetectionResult
            {
                HasPushRegistration = swAnalyzer.CheckPushNotification(swScript),
                HasBackgroundSync = swAnalyzer.CheckBackgroundSync(swScript),
                HasPeriodicBackgroundSync = swAnalyzer.CheckPeriodicSync(swScript),
                NoServiceWorkerFoundDetails = swUrl == null ? "Couldn't find a service worker registration via HTML parsing" : string.Empty,
                Scope = swUrl != null ? uri : null,
                ServiceWorkerDetectionTimedOut = false,
                Url = swUri
            };
        }

        private async Task<string?> GetServiceWorkerUrlFromScripts(Uri uri, HtmlDocument htmlDoc)
        {
            // Grab the <script> elements with a src element.
            var scripts = GetScriptsFromDoc(uri, htmlDoc)
                .ToArray();
 
            // Any of the scripts PWABuilder's own pwaupdate component? If so, we have a service worker.
            if (scripts.Any(scriptUri => scriptUri == new Uri("https://cdn.jsdelivr.net/npm/@pwabuilder/pwaupdate")))
            {
                return "/pwabuilder-sw.js";
            }

            // Do any of those have a service worker registration in them?
            var tokenSource = new CancellationTokenSource();
            var fetchScriptsTasks = GetScriptsFromDoc(uri, htmlDoc)
                .Select(src => TryGetScriptContents(src, tokenSource.Token))
                .Select(async scriptContents => GetServiceWorkerUrlFromText(await scriptContents) ?? string.Empty)
                .ToArray();
            var serviceWorkerUrl = await fetchScriptsTasks.FirstResult(i => !string.IsNullOrEmpty(i), httpTimeout, tokenSource.Token);
            tokenSource.Cancel();

            if (serviceWorkerUrl == serviceWorkerNameFallback)
            {
                // We have a service worker, but we couldn't determine its URL. 
                // This indicates the sw registration is done via dynamic code, e.g. navigator.serviceWorker.register(someVariable)
                // In such a case, see if we can determine the real URL.
                var realSwUrl = await this.GetServiceWorkerUrlFromCommonFileNames(uri);
                return realSwUrl ?? serviceWorkerUrl;
            }

            return serviceWorkerUrl;
        }

        private async Task<string> GetPageHtml(Uri uri)
        {
            using var http2Request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Version = new Version(2, 0)
            };
            using var result = await http.SendAsync(http2Request, httpTimeout, CancellationToken.None);
            if (!result.IsSuccessStatusCode)
            {
                logger.LogWarning("Unable to fetch page HTML due to HTTP {code}. Reason: {statusText}", result.StatusCode, result.ReasonPhrase);
                result.EnsureSuccessStatusCode();
            }

            var contentString = await result.Content.ReadAsStringAsync();
            logger.LogInformation("Successfully fetched {url} via HTML parsing", uri);
            return contentString;
        }

        private HtmlDocument GetDocument(string pageHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(pageHtml);
            return doc;
        }

        private async Task<string?> GetServiceWorkerUrlFromDoc(Uri uri, HtmlDocument doc)
        {
            var swUrl = doc.DocumentNode.SelectNodes("//script")
                .Select(script => GetServiceWorkerUrlFromText(script.InnerText))
                .FirstOrDefault(swUrl => !string.IsNullOrWhiteSpace(swUrl));            
            if (swUrl == null)
            {
                logger.LogWarning("Unable to find service worker registration in page HTML");
            }

            if (swUrl == serviceWorkerNameFallback)
            {
                // We have a service worker, but we couldn't determine its URL. 
                // This indicates the sw registration is done via dynamic code, e.g. navigator.serviceWorker.register(someVariable)
                // In such a case, see if we can determine the real URL.
                var realSwUrl = await this.GetServiceWorkerUrlFromCommonFileNames(uri);
                return realSwUrl ?? swUrl;
            }

            return swUrl;
        }

        private string? GetServiceWorkerUrlFromText(string? input)
        {
            if (input == null)
            {
                return null;
            }

            var regMatch = swRegex.Match(input);
            if (regMatch.Success || input.Contains("navigator.serviceWorker.register(", StringComparison.InvariantCulture))
            {
                // See if we can extract the service worker URL
                var urlGroup = regMatch.Groups.Cast<Group>().ElementAtOrDefault(1);
                if (!regMatch.Success || urlGroup == null || string.IsNullOrWhiteSpace(urlGroup.Value))
                {
                    logger.LogWarning("Found service worker registration script, but unable to determine the service worker URL. Returning dynamically generated URL placeholder.");
                    return serviceWorkerNameFallback;
                }

                return urlGroup.Value;
            }

            return null;
        }

        private IEnumerable<Uri> GetScriptsFromDoc(Uri uri, HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//script")
                .Select(n => n.GetAttributeValue("src", null))
                .Select(src => GetAbsoluteUri(uri, src))
                // COMMENTED OUT: we can't check if it's on domain, because the script may be served from a CDN // .Where(srcUri => srcUri?.Host == uri.Host)
                .Where(src => src != null)
                .Select(srcUri => srcUri!); 
        }

        private Uri? GetAbsoluteUri(Uri baseUri, string? relativeOrEmpty)
        {
            if (string.IsNullOrWhiteSpace(relativeOrEmpty))
            {
                return null;
            }

            Uri.TryCreate(baseUri, relativeOrEmpty, out var absoluteUri);
            return absoluteUri;
        }

        private async Task<string> TryGetScriptContents(Uri? scriptUri, CancellationToken cancelToken)
        {
            if (scriptUri == null)
            {
                return string.Empty;
            }

            try
            {
                var scriptContents = await http.GetStringAsync(scriptUri, httpTimeout, cancelToken);
                return scriptContents;
            }
            catch (TimeoutException)
            {
                logger.LogWarning("Cancelled fetch of script {url} because it took too long.");
                return string.Empty;
            }
            catch(Exception fetchError)
            {
                logger.LogWarning(fetchError, "Unable to load script contents due to error. Script was at {src}", scriptUri);
                return string.Empty;
            }
        }

        

        private async Task<bool> TryCheckIfExists(Uri uri, string[] acceptHeaders, CancellationToken cancelToken)
        {
            // Send a HEAD message to see if it exists.
            // Reminder: HEAD is identical to GET, except it only returns the headers, no body.
            try
            {
                using var headMessage = new HttpRequestMessage(HttpMethod.Head, uri);
                foreach (var header in acceptHeaders)
                {
                    headMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(header));
                }
                using var result = await this.http.SendAsync(headMessage, httpTimeout, cancelToken);

                return result.IsSuccessStatusCode &&
                    acceptHeaders.Any(header => result.Content.Headers.ContentType?.MediaType?.Contains(header, StringComparison.InvariantCultureIgnoreCase) == true);
            }
            catch (TimeoutException)
            {
                logger.LogInformation("Timeout occurred when checking for existence of {url}", uri);
                return false;
            }
            catch (Exception headException)
            {
                logger.LogInformation(headException, "Error when checking for existence of {url}", uri);
                return false;
            }
        }

        private async Task<string?> GetServiceWorkerUrlFromCommonFileNames(Uri uri)
        {
            var commonSwFileNames = new[]
            {
                "/sw.js",
                "/service-worker.js",
                "/serviceworker.js",
                "/superpwa-sw.js",
                "/ngsw-worker.js",
                "/sw-amp.js",
                "/firebase-messaging-sw.js",
                "/pwabuilder-sw.js",
                "/serviceworker"
            };

            var commonSwFileNamesWithUrls = commonSwFileNames
                .Select(fileName => new
                {
                    FileName = fileName,
                    Uri = new Uri(uri, fileName)
                });
            var cancelTokenSource = new CancellationTokenSource();

            // Run the check on each URL and select the details.
            var checkTasks = commonSwFileNamesWithUrls
                .Select(i => TryCheckIfExists(i.Uri, new[] { "application/javascript", "text/javascript" }, cancelTokenSource.Token)
                    .ContinueWith(task => new
                    {
                        FileName = i.FileName,
                        Uri = i.Uri,
                        UriExists = task.IsCompletedSuccessfully && task.Result == true
                    }))
                .ToArray();

            // Find the first one with a real live URL.
            var matchingResultTask = checkTasks.FirstResult(i => i.UriExists, httpTimeout, cancelTokenSource.Token);
            try
            {
                var firstUrlExists = await Policy.TimeoutAsync(TimeSpan.FromSeconds(2))
                    .ExecuteAsync(async () => await matchingResultTask);
                return firstUrlExists?.FileName;
            }
            catch (Polly.Timeout.TimeoutRejectedException)
            {
                // Couldn't find a URI that exists within the timeout period.
                return null;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Unexpected exception when checking if SW URLs exist");
                return null;
            }
            finally
            {
                cancelTokenSource.Cancel();
            }
        }
    }
}
