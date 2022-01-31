using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Polly;
using PuppeteerSharp;
using PWABuilder.ServiceWorkerDetector.Common;
using PWABuilder.ServiceWorkerDetector.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Services
{
    /// <summary>
    /// Service that uses Puppeteer (headless Chrome) to detect a service worker.
    /// </summary>
    public class PuppeteerDetector
    {
        private readonly ILogger<PuppeteerDetector> logger;
        private readonly HttpClient http;
        private readonly ServiceWorkerCodeAnalyzer swCodeAnalyzer;
        private readonly AnalyticsService analyticsService;

        private const int chromeRevision = 869685;  // Each build of Puppeteer uses a specific Chromium version. See https://github.com/puppeteer/puppeteer/releases for which version of Chromium should work. Check Puppeteer-Sharp's default Chromium version: https://github.com/hardkoded/puppeteer-sharp/blob/master/lib/PuppeteerSharp/BrowserFetcher.cs
        private static readonly int serviceWorkerDetectionTimeoutMs = (int)TimeSpan.FromSeconds(20).TotalMilliseconds;
        private static readonly TimeSpan httpTimeout = TimeSpan.FromSeconds(5);

        public PuppeteerDetector(
            ILogger<PuppeteerDetector> logger,
            IHttpClientFactory httpClientFactory,
            ServiceWorkerCodeAnalyzer swCodeAnalyzer,
            AnalyticsService analyticsService)
        {
            this.logger = logger;
            this.http = httpClientFactory.CreateClientWithUserAgent();
            this.swCodeAnalyzer = swCodeAnalyzer;
            this.analyticsService = analyticsService;
        }

        /// <summary>
        /// Runs all service works checks.
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public async Task<ServiceWorkerDetectionResult> Run(Uri uri, bool cacheSuccessfulResults = true)
        {
            try
            {
                // First, see if we can find a service worker.
                using var serviceWorkerDetection = await DetectServiceWorker(uri);
                if (!serviceWorkerDetection.ServiceWorkerDetected)
                {
                    // No service worker? Punt.
                    return new ServiceWorkerDetectionResult
                    {
                        ServiceWorkerDetectionTimedOut = serviceWorkerDetection.TimedOut,
                        NoServiceWorkerFoundDetails = serviceWorkerDetection.NoServiceWorkerFoundDetails
                    };
                }

                var swScope = await TryGetScope(serviceWorkerDetection.Page, uri);
                var swContents = await TryGetScriptContents(serviceWorkerDetection.Worker.Uri, CancellationToken.None);
                this.logger.LogInformation("Successfully detected service worker for {uri}", uri);
                return new ServiceWorkerDetectionResult
                {
                    Url = serviceWorkerDetection.Worker.Uri,
                    Scope = swScope,
                    HasPushRegistration = await TryCheckPushRegistration(serviceWorkerDetection.Page, uri) || swCodeAnalyzer.CheckPushNotification(swContents),
                    HasBackgroundSync = swCodeAnalyzer.CheckBackgroundSync(swContents),
                    HasPeriodicBackgroundSync = swCodeAnalyzer.CheckPeriodicSync(swContents)
                };
            }
            catch (Exception error)
            {
                logger.LogError(error, "Error running all checks for {url}", uri);
                var isTimeout = error is TimeoutException || error is Polly.Timeout.TimeoutRejectedException || error.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
                return new ServiceWorkerDetectionResult
                {
                    NoServiceWorkerFoundDetails = "Error during Puppeteer service worker detection. " + error.Message,
                    ServiceWorkerDetectionTimedOut = isTimeout
                };
            }
        }

        public async Task<bool> GetPeriodicSyncStatus(Uri uri)
        {
            try
            {
                using var detectionResults = await DetectServiceWorker(uri);
                if (!detectionResults.ServiceWorkerDetected)
                {
                    throw new InvalidOperationException("Couldn't fetch periodic sync registration because no service worker was detected: " + detectionResults.NoServiceWorkerFoundDetails);
                }

                var swCode = await http.GetStringAsync(detectionResults.Worker.Uri);
                return swCode.Contains(".addEventListener('periodicsync'") || swCode.Contains(".addEventListener(\"periodicsync\"");
            }
            catch (Exception pushRegCheckError)
            {
                logger.LogError(pushRegCheckError, "Error running periodicsync check for {serviceWorkerUrl}", uri);
                throw;
            }
        }

        public async Task<bool> GetOfflineSupport(Uri uri)
        {
            // Service worker without offline support: https://pwa-sw-empty-fetch.glitch.me
            // Service worker with offline support: https://pwa-sw-offline.glitch.me, https://webboard.app, https://messianicradio.com, https://flashmath.cards

            using var detectionResults = await DetectServiceWorker(uri);
            if (!detectionResults.ServiceWorkerDetected)
            {
                logger.LogInformation("Unable to detect offline support because the service worker wasn't detected. {0}", detectionResults.NoServiceWorkerFoundDetails);
                return false;
            }

            try
            {
                // First, reload the page. This is necessary because service workers are often registered on an already-served page, so they might not be in the cache.
                await detectionResults.Page.ReloadAsync(serviceWorkerDetectionTimeoutMs, new[] { WaitUntilNavigation.Networkidle0 });

                // See if we have HTML in the cache. If so, we'll deem it offline-capable.
                // This is more resilient than futzing with offline mode and refreshing pages, which is fraught with race conditions.
                var isHtmlInCache = await detectionResults.Page.EvaluateExpressionAsync<bool>(@"
                    async function isHtmlInCache() {
                        const cacheNames = await caches.keys();
                        for (let cacheName of cacheNames) {
                            const cache = await caches.open(cacheName);
                            const cacheKeys = await cache.keys();
                            for (let key of cacheKeys) {
                                const cachedObject = await cache.match(key);
                                if (cachedObject && cachedObject.headers) {
                                    const contentType = cachedObject.headers.get('Content-Type');
                                    if (contentType && contentType.startsWith('text/html')) {
                                        return true;
                                    }
                                }
                            }
                        }

                        return false;
                    }

                    isHtmlInCache();
                ");
                
                analyticsService.LogOfflineResult(uri, isHtmlInCache);
                return isHtmlInCache;
            }
            catch (Exception evalError)
            {
                logger.LogError(evalError, "Unable to determine offline support due to evaluation error");
                return false;
            }
        }

        private async Task<bool> TryCheckPushRegistration(Page page, Uri uri)
        {
            try
            {
                return await CheckPushRegistration(page);
            }
            catch (Exception pushRegError)
            {
                logger.LogWarning(pushRegError, "Error fetching push registration for {url}", uri);
                return false;
            }
        }

        private Task<bool> CheckPushRegistration(Page page)
        {
            return page.EvaluateExpressionAsync<bool>("navigator.serviceWorker.getRegistration().then(swReg => swReg.pushManager.getSubscription()).then(pushReg => pushReg != null)");
        }

        private async Task<string> TryGetScriptContents(Uri? scriptUri, CancellationToken cancelToken)
        {
            if (scriptUri == null)
            {
                return string.Empty;
            }

            try
            {
                return await http.GetStringAsync(scriptUri, httpTimeout, cancelToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("Cancelled fetch of script {url} because it took too long.");
                return string.Empty;
            }
            catch (Exception fetchError)
            {
                logger.LogWarning(fetchError, "Unable to load script contents due to error. Script was at {src}", scriptUri);
                return string.Empty;
            }
        }

        private async Task<bool> TryCheckBackgroundSync(Page page, Uri uri)
        {
            try
            {
                return await page.EvaluateExpressionAsync<bool>("navigator.serviceWorker.getRegistration().then(swReg => swReg.pushManager.getSubscription()).then(pushReg => pushReg != null)");
            }
            catch (Exception pushRegError)
            {
                logger.LogWarning(pushRegError, "Error fetching push registration for {url}", uri);
                return false;
            }
        }



        private async Task<Uri?> TryGetScope(Page page, Uri uri)
        {
            try
            {
                return await GetScope(page, uri);
            }
            catch (Exception scopeError)
            {
                logger.LogWarning(scopeError, "Error fetching scope of service worker for {url}", uri);
                return null;
            }
        }

        private async Task<Uri?> GetScope(Page page, Uri uri)
        {
            var scopeUrl = await page.EvaluateExpressionAsync<string>("navigator.serviceWorker.getRegistration().then(reg => reg.scope)");
            if (Uri.TryCreate(scopeUrl, UriKind.Absolute, out var scopeUri))
            {
                return scopeUri;
            }

            // Can we construct an absolute URI from the base URI?
            if (Uri.TryCreate(uri, scopeUrl, out var absoluteScopeUri))
            {
                return absoluteScopeUri;
            }

            logger.LogWarning("Scope evaluation succeeded, but scope was not a valid URI: {scopeUrl}", scopeUrl);
            return null;
        }

        private async Task<ServiceWorkerExistsResult> GetServiceWorkerUrl(Browser browser, Page page)
        {
            try
            {

                var serviceWorkerTarget = await WaitForServiceWorkerAsync(browser, page);
                if (serviceWorkerTarget == null)
                {
                    return new ServiceWorkerExistsResult("Couldn't find service worker", false, page, browser);
                }
                if (!Uri.TryCreate(serviceWorkerTarget.Url, UriKind.Absolute, out var serviceWorkerUrl))
                {
                    return new ServiceWorkerExistsResult($"Unable to parse service worker URL into absolute URI. Raw service worker URL was {serviceWorkerTarget.Url}", false, page, browser);
                }

                return new ServiceWorkerExistsResult(new ServiceWorkerDetails(serviceWorkerTarget, serviceWorkerUrl), page, browser);
            }
            catch (Exception timeoutError) when (timeoutError is TimeoutException or Polly.Timeout.TimeoutRejectedException)
            {
                return new ServiceWorkerExistsResult("No service worker detected within alloted timeout of " + TimeSpan.FromMilliseconds(serviceWorkerDetectionTimeoutMs).ToString(), true, page, browser);
            }
            catch (Exception error)
            {
                return new ServiceWorkerExistsResult(error.Message, false, page, browser);
            }
        }

        private async Task<Target> WaitForServiceWorkerAsync(Browser browser, Page page)
        {
            // While Puppeteer does have a timeout (see .WaitForTargetAsync call below), we've seen Chrome hang on this.
            // So, we have an insurance policy: Use Polly to forcefully kill this detector after our timeout period.
            var timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromMilliseconds(serviceWorkerDetectionTimeoutMs), Polly.Timeout.TimeoutStrategy.Pessimistic);

            return await timeoutPolicy.ExecuteAsync(async () => await browser.WaitForTargetAsync(t => t.Type == TargetType.ServiceWorker, new WaitForOptions { Timeout = serviceWorkerDetectionTimeoutMs }));
        }

        private async Task<Page> GoToPage(Uri uri, Browser browser)
        {
            var retryOnNavigationErrorPolicy = Policy
                .Handle<NavigationException>(err => err.Message.Contains("Page failed to process Inspector.targetCrashed")) // We've noticed some sites, like messianicradio.com, will occasionally cause this error. When it happens, try again, because it usually succeeds.
                .Retry();

            return await retryOnNavigationErrorPolicy.Execute(async () =>
            {
                var pages = await browser.PagesAsync();
                var page = pages[0];
                page.DefaultTimeout = serviceWorkerDetectionTimeoutMs;
                page.DefaultNavigationTimeout = serviceWorkerDetectionTimeoutMs;
                await page.SetExtraHttpHeadersAsync(new()
                {
                    { "accept-language", "en-US" }, // Needed, as some sites see us as an unwanted crawler and refuse to render the page
                    { "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.88 Safari/537.36 Edg/87.0.664.66" },
                    //{ "cache-control", "no-cache" }, // COMMENTED OUT: this seems to cause some sites to fail to fetch the service worker, including https://www.pwabuilder.com
                    { "accept-encoding", "gzip, default, br" },
                    { "accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9" }
                });
                var response = await NavigatePageToUriWithTimeout(uri, page);
                if (!response.Ok)
                {
                    page.Dispose();
                    throw new Exception($"{uri} couldn't be fetched, returned status code {response.Status}, {response.StatusText}");
                }

                return page;
            });
        }

        private async Task<Response> NavigatePageToUriWithTimeout(Uri uri, Page page)
        {
            // While we configure the page to have a timeout, we've witnessed scenarios where Chrome hangs even with this timeout.
            // So, insurance policy using Polly's pessimistic strategy, which spins up a thread and monitors the result.
            var timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromMilliseconds(serviceWorkerDetectionTimeoutMs), Polly.Timeout.TimeoutStrategy.Pessimistic);
            return await timeoutPolicy.ExecuteAsync(async () => await page.GoToAsync(uri.ToString(), serviceWorkerDetectionTimeoutMs, new[] { WaitUntilNavigation.Load }));
        }

        private async Task<Browser> CreateBrowser(RevisionInfo chromeInfo)
        {
            var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Timeout = serviceWorkerDetectionTimeoutMs,
                Headless = true,
                ExecutablePath = chromeInfo.ExecutablePath,
                Args = new[] 
                { 
                    " --lang=en-US", // needed, as some sites append culture-specific service workers
                    "about:blank" // otherwise google.com loads by default, slowing things down.
                } 
            });
            browser.DefaultWaitForTimeout = serviceWorkerDetectionTimeoutMs;
            return browser;
        }

        private async Task<RevisionInfo> DownloadChromeRevision()
        {
            var browserFetcher = new BrowserFetcher();
            var browserFetchResult = await browserFetcher.DownloadAsync(chromeRevision);
            if (!browserFetchResult.Downloaded)
            {
                throw new Exception("Unable to download Chrome revision");
            }

            return browserFetchResult;
        }

        private async Task<ServiceWorkerExistsResult> DetectServiceWorker(Uri uri)
        {
            var chromeInfo = await DownloadChromeRevision();
            var browser = await CreateBrowser(chromeInfo);
            Page page;
            try
            {          
                page = await GoToPage(uri, browser);              
            }
            catch (Polly.Timeout.TimeoutRejectedException)
            {
                return new ServiceWorkerExistsResult("Navigation didn't complete within alloted timeout of " + TimeSpan.FromMilliseconds(serviceWorkerDetectionTimeoutMs).ToString(), true, null, browser);
            }
            catch
            {
                browser.Dispose();
                throw;
            }
            ServiceWorkerExistsResult workerDetection;
            try
            {
                workerDetection = await GetServiceWorkerUrl(browser, page);
            }
            catch (Exception)
            {
                page.Dispose();
                browser.Dispose();
                throw;
            }

            if (workerDetection.Worker == null)
            {
                logger.LogWarning("No service worker found for URL {uri}: {details}", uri, workerDetection.NoServiceWorkerFoundDetails);
                page.Dispose();
                browser.Dispose();
            }

            return workerDetection;
        }
    }
}
