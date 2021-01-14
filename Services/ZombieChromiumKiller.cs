using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector.Services
{
    /// <summary>
    /// Background process that runs periodically and kills any zombie Chromium puppeteer processes.
    /// </summary>
    /// <remarks>
    /// Our service worker detection code spins up Puppeteer, which is a headless Chromium instance.
    /// While our code disposes these instances properly, occasionally we've seen zombie Chrome processes: they just won't die.
    /// This service kills those zombie processes.
    /// </remarks>
    public class ZombieChromiumKiller : IHostedService, IDisposable
    {
        private readonly ILogger<ZombieChromiumKiller> logger;
        private Timer? timer;

        private static readonly TimeSpan dueTime = TimeSpan.FromMinutes(10);

        public ZombieChromiumKiller(ILogger<ZombieChromiumKiller> logger)
        {
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            this.timer = new Timer(_ => KillZombieChromiums(), null, dueTime, dueTime);

            return Task.CompletedTask;
        }

        private void KillZombieChromiums()
        {
            var tenMinsAgo = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
            Process[] chromeProcesses;
            try
            {
                chromeProcesses = Process.GetProcessesByName("chrome")
                    .Where(p => p.MainModule?.FileName?.Contains(".local-chromium", StringComparison.InvariantCultureIgnoreCase) == true) // It should be Puppeteer Chrome instance, not just any old Chrome browser instance.
                    .Where(p => p.StartTime.ToUniversalTime() < tenMinsAgo) // Is it a zombie process? We consider any chromium processes older than 10 minutes to be a zombie process.
                    .ToArray();
            }
            catch (Exception getChromeProcsError)
            {
                logger.LogWarning(getChromeProcsError, "Unable to kill zombie chrome processes due to an error when fetching list of chromium processes.");
                chromeProcesses = Array.Empty<Process>();
            }

            if (chromeProcesses.Length > 0)
            {                
                var zombies = chromeProcesses
                    .Where(p => p.MainModule?.FileName?.Contains(".local-chromium", StringComparison.InvariantCultureIgnoreCase) == true)
                    .Where(p => p.StartTime.ToUniversalTime() < tenMinsAgo)
                    .ToList();
                logger.LogInformation("Found {count} zombie chromiums", zombies.Count);
                foreach (var zombie in zombies)
                {
                    try
                    {
                        zombie.Kill(true);
                        logger.LogInformation("Killed zombie chromium process {id}", zombie.Id);
                    }
                    catch (Exception failureToKillZombie)
                    {
                        logger.LogWarning(failureToKillZombie, "Unable to kill zombie chrome process {id} due to exception", zombie.Id);
                    }
                }
            }
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Zombie Chromium Killer is stopping");
            this.timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            this.timer?.Dispose();
        }
    }
}
