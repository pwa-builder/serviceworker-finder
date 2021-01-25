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
    public class UrlLogger
    {
        private readonly IOptions<UrlLoggerApiSettings> settings;
        private readonly ILogger<UrlLogger> logger;
        private readonly HttpClient http;

        public UrlLogger(
            IOptions<UrlLoggerApiSettings> settings, 
            HttpClient http,
            ILogger<UrlLogger> logger)
        {
            this.settings = settings;
            this.http = http;
            this.logger = logger;
        }

        public void LogUrlResult(Uri url, bool success, string? error, TimeSpan elapsed)
        {
            if (this.settings.Value.Url == null)
            {
                this.logger.LogWarning("Skipping URL recording due to no url log service API");
                return;
            }

            var args = System.Text.Json.JsonSerializer.Serialize(new
            {
                Url = url,
                ServiceWorkerDetected = success,
                ServiceWorkerDetectionError = error,
                ServiceWorkerDetectionTimeInMs = elapsed.TotalMilliseconds
            });
            this.http.PostAsync(this.settings.Value.Url, new StringContent(args))
                .ContinueWith(_ => logger.LogInformation("Successfully sent {url} to URL logging service. Success = {success}, Error = {error}, Elapsed = {elapsed}", url, success, error, elapsed), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(task => logger.LogError(task.Exception ?? new Exception("Unable to send URL to logging service"), "Unable to send {url} to logging service due to an error", url), TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
