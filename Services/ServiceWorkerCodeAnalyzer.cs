using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Services
{
    /// <summary>
    /// Analyzes service worker code to find patterns suggestion support of some feature.
    /// </summary>
    /// <remarks>
    /// Why can't we use puppeteer for these? Because there is no reliable way for puppeteer to see if a PWA
    /// supports periodic sync or background sync. While we could check swReg.periodicSync.tags or swReg.sycn.tags in Puppeteer,
    /// this will return an empty array for apps that do dynamic registration of sync or periodic sync.
    /// 
    /// Thus, the only reliable way we've found is to analyze the service worker code and look for event handlers.
    /// </remarks>
    public class ServiceWorkerCodeAnalyzer
    {
        private static readonly Regex[] pushRegexes = new[]
        {
            new Regex("\\.addEventListener\\(['|\"]push['|\"]"), // .addEventListener('push') or .addEventListener("push")
            new Regex("\n\\s*addEventListener\\(['|\"]push['|\"]"), // [new line] addEventListener('push')
            new Regex("\\.onpush\\s*="), // self.onpush = ...
            new Regex("\n\\s*onpush\\s*=") // [new line] onpush = ...
        };
        private static readonly Regex[] periodicSyncRegexes = new[]
        {
            new Regex("\\.addEventListener\\(['|\"]periodicsync['|\"]"), // .addEventListener("periodicsync") and .addEventListener('periodicsync'),
            new Regex("\n\\s*addEventListener\\(['|\"]periodicsync['|\"]"), // addEventListener('periodicsync') at the beginning of a line (or at the beginning of a line after zero or more whitespace). See https://github.com/pwa-builder/PWABuilder/issues/1863
            new Regex("\\.onperiodicsync\\s*="), // self.onperiodicsync = ...
            new Regex("\n\\s*onperiodicsync\\s*=") // onperiodicsync = ...
        };
        private static readonly Regex[] backgroundSyncRegexes = new[]
        {
            new Regex("\\.addEventListener\\(['|\"]sync['|\"]"), // .addEventListener("sync") and .addEventListener('sync')
            new Regex("\n\\s*addEventListener\\(['|\"]sync['|\"]"), // [new line] addEventListener('sync')
            new Regex("\\.onsync\\s*="), // self.onsync = function(...)
            new Regex("\n\\s*onsync\\s*="), // [new line] onsync = function(...)
            new Regex("BackgroundSyncPlugin") // new workbox.backgroundSync.BackgroundSyncPlugin(...)
        };

        /// <summary>
        /// Checks whether the service worker appears to have periodic sync support.
        /// </summary>
        /// <param name="serviceWorkerContents"></param>
        /// <returns></returns>
        public bool CheckPeriodicSync(string serviceWorkerContents)
        {
            return periodicSyncRegexes.Any(r => r.IsMatch(serviceWorkerContents));
        }

        /// <summary>
        /// Checks whether the service worker appears to have background sync support.
        /// </summary>
        /// <param name="serviceWorkerContents"></param>
        /// <returns></returns>
        public bool CheckBackgroundSync(string serviceWorkerContents)
        {
            return backgroundSyncRegexes.Any(r => r.IsMatch(serviceWorkerContents));
        }

        /// <summary>
        /// Checks whether the service worker appears to have push notification support.
        /// </summary>
        /// <param name="serviceWorkerContents"></param>
        /// <returns></returns>
        public bool CheckPushNotification(string serviceWorkerContents)
        {
            return pushRegexes.Any(r => r.IsMatch(serviceWorkerContents));
        }
    }
}
