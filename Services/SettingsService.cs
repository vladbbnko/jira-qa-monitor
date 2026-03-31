using System.Text.Json;
using Azure.Storage.Blobs;
using JiraQaMonitor.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Services;

public class SettingsService(IConfiguration config, ILogger<SettingsService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var connectionString = config["State__BlobConnectionString"]
                                   ?? Environment.GetEnvironmentVariable("State__BlobConnectionString")
                                   ?? throw new InvalidOperationException("State__BlobConnectionString is not configured");
            var containerName    = config["State__ContainerName"]
                                   ?? Environment.GetEnvironmentVariable("State__ContainerName")
                                   ?? "jira-qa-monitor";

            var blob = new BlobContainerClient(connectionString, containerName).GetBlobClient("settings.json");

            if (!await blob.ExistsAsync())
            {
                logger.LogInformation("No settings.json found — using defaults (threshold: 4h, interval: 2h)");
                return new AppSettings();
            }

            var download = await blob.DownloadContentAsync();
            var settings = JsonSerializer.Deserialize<AppSettings>(download.Value.Content.ToString(), JsonOpts)
                           ?? new AppSettings();

            logger.LogInformation("Loaded settings: review reminder threshold={Threshold}h, interval={Interval}h",
                settings.ReviewReminder.ThresholdHours, settings.ReviewReminder.IntervalHours);

            return settings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load settings.json — using defaults");
            return new AppSettings();
        }
    }
}
