using PuppeteerSharp;
using System;
using System.Diagnostics.CodeAnalysis;

namespace PWABuilder.ServiceWorkerDetector.Models
{
    public class ServiceWorkerExistsResult : IDisposable
    {
        public ServiceWorkerExistsResult(ServiceWorkerDetails worker, Page page, Browser browser)
        {
            this.Worker = worker;
            this.Page = page;
            this.Browser = browser;
        }

        public ServiceWorkerExistsResult(string notFoundDetails, bool timedOut, Page? page, Browser browser)
        {
            this.NoServiceWorkerFoundDetails = notFoundDetails;
            this.TimedOut = timedOut;
            this.Page = page;
            this.Browser = browser;
        }

        public ServiceWorkerDetails? Worker { get; }
        public string? NoServiceWorkerFoundDetails { get; }
        public bool TimedOut { get; }
        public Browser Browser { get; }
        public Page? Page { get; }

        [MemberNotNullWhen(true, nameof(Worker), nameof(Page))]
        public bool ServiceWorkerDetected => Worker != null && Page != null;

        public void Dispose()
        {
            Page?.Dispose();
            Browser.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
