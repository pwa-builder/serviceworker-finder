using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PWABuilder.ServiceWorkerDetector.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Services
{
    public class AnalyticsService
    {
        private readonly IOptions<AnalyticsSettings> settings;
        private readonly ILogger<AnalyticsService> logger;
        private readonly HttpClient http;

        public AnalyticsService(
            IOptions<AnalyticsSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<AnalyticsService> logger)
        {
            this.settings = settings;
            this.http = httpClientFactory.CreateClient();
            this.logger = logger;
        }

        public void LogUrlResult(Uri url, ServiceWorkerDetectionResult result, TimeSpan elapsed)
        {
            if (this.settings.Value.Url == null)
            {
                this.logger.LogWarning("Skipping URL recording due to no url log service API");
                return;
            }

            var args = System.Text.Json.JsonSerializer.Serialize(new
            {
                Url = url,
                ServiceWorkerDetected = result.HasSW,
                ServiceWorkerDetectionError = result.ServiceWorkerDetectionTimedOut,
                ServiceWorkerDetectionTimedOut = result.ServiceWorkerDetectionTimedOut,
                ServiceWorkerScoreExcludingOffline = result.ServiceWorkerScore.Select(a => a.Value).Sum(),
                ServiceWorkerDetectionTimeInMs = elapsed.TotalMilliseconds
            });
            this.http.PostAsync(this.settings.Value.Url, new StringContent(args))
                .ContinueWith(_ => logger.LogInformation("Successfully sent {url} to URL logging service. Success = {success}, Error = {error}, Elapsed = {elapsed}", url, result.HasSW, result.NoServiceWorkerFoundDetails, elapsed), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(task => logger.LogError(task.Exception ?? new Exception("Unable to send URL to logging service"), "Unable to send {url} to logging service due to an error", url), TaskContinuationOptions.OnlyOnFaulted);
        }

        public void LogOfflineResult(Uri url, bool offlineSupported)
        {
            if (this.settings.Value.Url == null)
            {
                this.logger.LogWarning("Skipping URL recording due to no url log service API");
                return;
            }

            var args = System.Text.Json.JsonSerializer.Serialize(new
            {
                Url = url,
                ServiceWorkerOfflineScore = offlineSupported ? 2 : 0
            });
            this.http.PostAsync(this.settings.Value.Url, new StringContent(args))
                .ContinueWith(_ => logger.LogInformation("Successfully sent {url} offline status to URL logging service. Offline status = {status}", url, offlineSupported), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(task => logger.LogError(task.Exception ?? new Exception("Unable to send offline status to logging service"), "Unable to send {url} to logging service due to an error", url), TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
