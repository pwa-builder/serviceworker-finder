Please use our [main repository for any issues/bugs/features suggestion](https://github.com/pwa-builder/PWABuilder/issues/new/choose).

# PWABuilder Service Worker Finder
.NET solution that uses Puppeteer (headless Chrome) to find a service worker from a given URL

## Running locally

Open the .sln file in Visual Studio and F5 to start.

## Testing

Individual URLs can be tested via /serviceworker/runallchecks?url={URL GOES HERE}

The app also has a test page, published to /serviceworker/index

## Deploy

This web service requires permission to kill processes on the server, used in terminating zombie headless Chrome instances. When deployed to IIS, ensure the application pool has the necessary permissions.
