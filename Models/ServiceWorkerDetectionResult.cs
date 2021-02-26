using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Models
{
    /// <summary>
    /// The results of a service worker scan.
    /// </summary>
    public class ServiceWorkerDetectionResult
    {
        /// <summary>
        /// Whether the service worker exists.
        /// </summary>
        /// <remarks>
        /// This property exists for backward compat reasons to support the PWABuilder web app.
        /// </remarks>
        public bool HasSW => Url != null;

        /// <summary>
        /// The URL of the service worker.
        /// </summary>
        public Uri? Url { get; set; }

        /// <summary>
        /// The URI of the scope of the service worker.
        /// </summary>
        public Uri? Scope { get; set; }

        /// <summary>
        /// Whether the service workers supports push notifications.
        /// </summary>
        public bool HasPushRegistration { get; set; }

        /// <summary>
        /// Whether the service worker supports Background Sync, which can resend data to the server when a previous request has failed. 
        /// A common scenario: the user performs actions while the PWA is offline (e.g. on a plane), these actions will fail, but can be resent via Background Sync.
        /// </summary>
        /// <remarks>
        /// https://developers.google.com/web/updates/2015/12/background-sync
        /// </remarks>
        public bool HasBackgroundSync { get; set; }

        /// <summary>
        /// Whether the service workers supports Periodic Background Sync, which enables data to be fetched from the server periodically.
        /// A common scenario is a News app that fetches the latest news every morning at a certain time before the user boards the subway. Then, when the user is on the subway with limited connectivity, the PWA still shows the news from that morning.
        /// </summary>
        /// <remarks>
        /// https://web.dev/periodic-background-sync/
        /// </remarks>
        public bool HasPeriodicBackgroundSync { get; set; }

        /// <summary>
        /// Detalis about why the service worker was not found.
        /// </summary>
        public string? NoServiceWorkerFoundDetails { get; set; }

        /// <summary>
        /// Whether service worker detection timed out, which typically happens when there is no service worker present.
        /// </summary>
        public bool ServiceWorkerDetectionTimedOut { get; set; }
    }
}
