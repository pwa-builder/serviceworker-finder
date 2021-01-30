using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PWABuilder.ServiceWorkerDetector.Models;
using PWABuilder.ServiceWorkerDetector.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ServiceWorkerController : ControllerBase
    {
        private readonly PuppeteerDetector puppeteerSwDetector;
        private readonly AllDetectors allDetectors;

        public ServiceWorkerController(
            PuppeteerDetector puppeteerSwDetector,
            AllDetectors allDetectors)
        {
            this.puppeteerSwDetector = puppeteerSwDetector;
            this.allDetectors = allDetectors;
        }

        [HttpGet]
        public ActionResult Index()
        {
            return File("~/index.html", "text/html");
        }

        [HttpGet]
        public Task<AllChecksResult> RunAllChecks(Uri url)
        {
            return this.allDetectors.Run(url);
        }

        public async Task<AllChecksResult> EnsureServiceWorkerFound(Uri url)
        {
            var result = await this.puppeteerSwDetector.Run(url, cacheSuccessfulResults: false);
            if (!result.HasSW)
            {
                throw new InvalidOperationException($"No service worker found for {url}. Timed out: {result.ServiceWorkerDetectionTimedOut}, Details: {result.NoServiceWorkerFoundDetails}");
            }

            return result;
        }

        [HttpGet]
        public Task<Uri?> GetServiceWorkerUrl(Uri url)
        {
            return this.puppeteerSwDetector.GetServiceWorkerUrl(url);
        }

        [HttpGet]
        public Task<Uri?> GetScope(Uri url)
        {
            return this.puppeteerSwDetector.GetScope(url);
        }

        [HttpGet]
        public Task<bool> GetPushRegistrationStatus(Uri url)
        {
            return this.puppeteerSwDetector.GetPushRegistrationStatus(url);
        }

        [HttpGet]
        public Task<bool> GetPeriodicSyncStatus(Uri url)
        {
            return this.puppeteerSwDetector.GetPeriodicSyncStatus(url);
        }
    }
}
