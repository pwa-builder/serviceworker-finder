using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            services.AddControllers();
            services.AddTransient<Detector>();
            services.AddHostedService<ZombieChromiumKiller>();
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
                "http://localhost:3000"
            };
            return allowedOrigins.Any(o => origin.Contains(o, StringComparison.OrdinalIgnoreCase));
        }
    }
}
