using PWABuilder.ServiceWorkerDetector.Common;
using PWABuilder.ServiceWorkerDetector.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Services
{
    /// <summary>
    /// Service that races both the HtmlParseDetector and PuppeteerDetector and returns the first successful result.
    /// </summary>
    public class AllDetectors
    {
        private readonly HtmlParseDetector htmlParseDetector;
        private readonly PuppeteerDetector puppeteerDetector;
        private readonly UrlLogger urlLogService;

        public AllDetectors(
            HtmlParseDetector htmlParseDetector, 
            PuppeteerDetector puppeteerDetector,
            UrlLogger urlLogService)
        {
            this.htmlParseDetector = htmlParseDetector;
            this.puppeteerDetector = puppeteerDetector;
            this.urlLogService = urlLogService;
        }

        /// <summary>
        /// Runs both Puppeteer and HTML parsing to find the service worker, returning the first successful result.
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public async Task<AllChecksResult> Run(Uri uri)
        {
            // If it's a localhost, don't spin it up because it ain't gonna work.
            // This is in response to users attempting to run PWABuilder on localhost.
            if (uri.IsLoopback)
            {
                throw new ArgumentException("URIs must not be local");
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var htmlParseTask = htmlParseDetector.Run(uri);
            var puppeteerTask = puppeteerDetector.Run(uri, cacheSuccessfulResults: true);
            var detectionTasks = new[] { htmlParseTask, puppeteerTask };
            var successfulResult = await detectionTasks.FirstResult(r => r.HasSW, TimeSpan.FromSeconds(10), CancellationToken.None);

            // If we have a successful result, use that.
            // Otherwise, wait for the Puppeteer task.
            var finalResult = successfulResult ?? await puppeteerTask;

            urlLogService.LogUrlResult(uri, finalResult.HasSW, finalResult.NoServiceWorkerFoundDetails, stopwatch.Elapsed);
            stopwatch.Stop();

            return finalResult;
        }
    }
}
