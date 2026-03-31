using System.Text.Json;
using Azure.Storage.Blobs;
using JiraQaMonitor.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Services;

public class StateService(IConfiguration config, ILogger<StateService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private BlobClient GetBlobClient()
    {
        var connectionString = config["State__BlobConnectionString"]
                               ?? Environment.GetEnvironmentVariable("State__BlobConnectionString")
                               ?? throw new InvalidOperationException("State__BlobConnectionString is not configured");
        var containerName    = config["State__ContainerName"]
                               ?? Environment.GetEnvironmentVariable("State__ContainerName")
                               ?? "jira-qa-monitor";
        const string blobName = "state.json";

        var containerClient = new BlobContainerClient(connectionString, containerName);
        containerClient.CreateIfNotExists();

        return containerClient.GetBlobClient(blobName);
    }

    public async Task<MonitorState> LoadStateAsync()
    {
        try
        {
            var blob = GetBlobClient();

            if (!await blob.ExistsAsync())
            {
                logger.LogInformation("State blob not found — starting with empty state");
                return new MonitorState();
            }

            var download = await blob.DownloadContentAsync();
            var json     = download.Value.Content.ToString();
            var state    = JsonSerializer.Deserialize<MonitorState>(json, JsonOpts) ?? new MonitorState();

            logger.LogInformation("Loaded state: {RfqCount} Ready For QA, {VerCount} Verified ticket(s)",
                state.ReadyForQaIds.Count, state.VerifiedIds.Count);
            return state;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load state from blob — using empty state");
            return new MonitorState();
        }
    }

    public async Task SaveStateAsync(MonitorState state)
    {
        try
        {
            var blob = GetBlobClient();
            var json = JsonSerializer.Serialize(state, JsonOpts);

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await blob.UploadAsync(stream, overwrite: true);

            logger.LogInformation("State saved: {RfqCount} Ready For QA, {VerCount} Verified ticket(s)",
                state.ReadyForQaIds.Count, state.VerifiedIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save state to blob — next run will re-process all current tickets");
        }
    }
}
