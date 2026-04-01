using System.Text.Json;
using Azure.Storage.Blobs;
using JiraQaMonitor.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Services;

public class TeamConfigService(IConfiguration config, ILogger<TeamConfigService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<TeamConfig?> LoadAsync()
    {
        try
        {
            var connectionString = config["State__BlobConnectionString"]
                                   ?? Environment.GetEnvironmentVariable("State__BlobConnectionString")
                                   ?? throw new InvalidOperationException("State__BlobConnectionString is not configured");
            var containerName    = config["State__ContainerName"]
                                   ?? Environment.GetEnvironmentVariable("State__ContainerName")
                                   ?? "jira-qa-monitor";

            var blob = new BlobContainerClient(connectionString, containerName).GetBlobClient("teams.json");

            if (!await blob.ExistsAsync())
            {
                logger.LogInformation("No teams.json found — falling back to env var webhooks");
                return null;
            }

            var download = await blob.DownloadContentAsync();
            var config_  = JsonSerializer.Deserialize<TeamConfig>(download.Value.Content.ToString(), JsonOpts);
            logger.LogInformation("Loaded team config: {Count} team(s)", config_?.Teams.Count ?? 0);
            return config_;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load teams.json — falling back to env var webhooks");
            return null;
        }
    }

    // Resolves a webhook URL for a given assignee email and status type.
    // Priority: matching team webhook → fallback webhook → null
    public string? ResolveWebhook(TeamConfig teamConfig, string assigneeEmail, Func<TeamWebhooks, string?> selector)
    {
        var team = FindTeam(teamConfig, assigneeEmail);
        return (team is not null ? selector(team.Webhooks) : null)
               ?? selector(teamConfig.FallbackWebhooks);
    }

    // Returns all team members except the assignee, with a non-empty aadObjectId.
    public List<TeamMember> GetOtherMembers(TeamConfig teamConfig, string assigneeEmail)
    {
        var team = FindTeam(teamConfig, assigneeEmail);
        if (team is null) return [];

        return team.Members
            .Where(m => !string.Equals(m.Email, assigneeEmail, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(m.AadObjectId))
            .ToList();
    }

    private static TeamDefinition? FindTeam(TeamConfig teamConfig, string assigneeEmail)
        => teamConfig.Teams.FirstOrDefault(t =>
            t.Members.Any(m => string.Equals(m.Email, assigneeEmail, StringComparison.OrdinalIgnoreCase)));
}
