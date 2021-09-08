using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PWABuilder.ServiceWorkerDetector.Models;
using PWABuilder.ServiceWorkerDetector.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PWABuilder.ServiceWorkerDetector
{
    public class Startup
    {
        readonly string AllowedOriginsPolicyName = "allowedOrigins";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AnalyticsSettings>(Configuration.GetSection("AnalyticsSettings"));

            services.AddControllers();
            services.AddTransient<PuppeteerDetector>();
            services.AddTransient<HtmlParseDetector>();
            services.AddTransient<AllDetectors>();
            services.AddTransient<AnalyticsService>();
            services.AddTransient<ServiceWorkerCodeAnalyzer>();
            services.AddHostedService<ZombieChromiumKiller>();
            services.AddHttpClient();
            services.AddMemoryCache(); // 50MB max size
            services.AddCors(options =>
            {
                options.AddPolicy(name: AllowedOriginsPolicyName, builder => builder
                    .SetIsOriginAllowed(CheckAllowedOriginCors)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseDeveloperExceptionPage();
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors(AllowedOriginsPolicyName);
            app.UseAuthorization();
            app.UseStaticFiles();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        private bool CheckAllowedOriginCors(string origin)
        {
            var allowedOrigins = new[]
            {
                "https://www.pwabuilder.com",
                "https://pwabuilder.com",
                "https://preview.pwabuilder.com",
                "https://localhost:3000",
                "http://localhost:3000",
                "http://localhost:8000",
                "https://localhost:8000",
                "https://pwabuilder-ag.com"
            };
            var allowedWildcardOrigins = new[]
            {
                ".azurestaticapps.net"
            };
            return allowedOrigins.Any(o => origin.Contains(o, StringComparison.OrdinalIgnoreCase)) ||
                allowedWildcardOrigins.Any(o => origin.Contains(o, StringComparison.OrdinalIgnoreCase));
        }
    }
}
