using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace semanticKernelSample1.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("MyAppSettings.json", optional: false, reloadOnChange: true)
                .Build();            services.AddSingleton<IConfiguration>(configuration);
            services.AddHttpClient<Plugins.ApiAlphaPlugin>();
            services.AddTransient<Plugins.ApiAlphaPlugin>();
            services.AddLogging(config =>
            {
                config.AddConsole();
            });
            return services;
        }
    }
}
