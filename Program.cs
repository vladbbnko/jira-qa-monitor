using JiraQaMonitor.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        // Read from environment variables (works on Azure)
        config.AddEnvironmentVariables();

        // Also read local.settings.json for local dev
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: false);
        var built = config.Build();
        var values = built.GetSection("Values")
                          .GetChildren()
                          .ToDictionary(x => x.Key, x => x.Value);
        config.AddInMemoryCollection(values!);
    })
    .ConfigureServices(services =>
    {
        services.AddHttpClient<JiraService>();
        services.AddHttpClient<WebhookService>();
        services.AddSingleton<StateService>();
        services.AddSingleton<TeamConfigService>();
        services.AddSingleton<SettingsService>();
    })
    .Build();

host.Run();