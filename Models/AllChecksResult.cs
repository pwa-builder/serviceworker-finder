using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Models
{
    public class AllChecksResult
    {
        public bool HasSW => Url != null;
        public Uri? Url { get; set; }
        public Uri? Scope { get; set; }
        public bool HasPushRegistration { get; set; }
        public string? NoServiceWorkerFoundDetails { get; set; }
        public bool ServiceWorkerDetectionTimedOut { get; set; }
    }
}
