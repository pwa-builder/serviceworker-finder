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
        private readonly Detector serviceWorkerDetector;

        public ServiceWorkerController(Detector serviceWorkerDetector)
        {
            this.serviceWorkerDetector = serviceWorkerDetector;
        }

        [HttpGet]
        public ActionResult Index()
        {
            return File("~/index.html", "text/html");
        }

        [HttpGet]
        public Task<AllChecksResult> RunAllChecks(Uri url)
        {
            return this.serviceWorkerDetector.RunAll(url);
        }

        public async Task<AllChecksResult> EnsureServiceWorkerFound(Uri url)
        {
            var result = await this.serviceWorkerDetector.RunAll(url, cacheSuccessfulResults: false);
            if (!result.HasSW)
            {
                throw new InvalidOperationException($"No service worker found for {url}. Timed out: {result.ServiceWorkerDetectionTimedOut}, Details: {result.NoServiceWorkerFoundDetails}");
            }

            return result;
        }

        [HttpGet]
        public Task<Uri?> GetServiceWorkerUrl(Uri url)
        {
            return this.serviceWorkerDetector.GetServiceWorkerUrl(url);
        }

        [HttpGet]
        public Task<Uri?> GetScope(Uri url)
        {
            return this.serviceWorkerDetector.GetScope(url);
        }

        [HttpGet]
        public Task<bool> GetPushRegistrationStatus(Uri url)
        {
            return this.serviceWorkerDetector.GetPushRegistrationStatus(url);
        }

        [HttpGet]
        public Task<bool> GetPeriodicSyncStatus(Uri url)
        {
            return this.serviceWorkerDetector.GetPeriodicSyncStatus(url);
        }
    }
}
