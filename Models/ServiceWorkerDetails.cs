using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Models
{
    public class ServiceWorkerDetails
    {
        public ServiceWorkerDetails(Target target, Uri uri)
        {
            this.Target = target;
            this.Uri = uri;
        }

        public Target Target { get; }
        public Uri Uri { get; }
    }
}
