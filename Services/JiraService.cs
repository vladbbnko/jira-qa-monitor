using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraQaMonitor.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Services;

public class JiraService(HttpClient httpClient, IConfiguration config, ILogger<JiraService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<JiraTicket>> GetReadyForQaTicketsAsync()
        => await QueryTicketsAsync("Ready For QA", "Ready For QA");

    public async Task<List<JiraTicket>> GetVerifiedTicketsAsync()
        => await QueryTicketsAsync("Verified", "Verified");

    private async Task<List<JiraTicket>> QueryTicketsAsync(string status, string logLabel)
    {
        var baseUrl  = config["Jira__BaseUrl"]  ?? Environment.GetEnvironmentVariable("Jira__BaseUrl")  ?? throw new InvalidOperationException("Jira__BaseUrl is not configured");
        var project  = config["Jira__Project"]  ?? Environment.GetEnvironmentVariable("Jira__Project")  ?? throw new InvalidOperationException("Jira__Project is not configured");
        var username = config["Jira__Username"] ?? Environment.GetEnvironmentVariable("Jira__Username") ?? throw new InvalidOperationException("Jira__Username is not configured");
        var apiToken = config["Jira__ApiToken"] ?? Environment.GetEnvironmentVariable("Jira__ApiToken") ?? throw new InvalidOperationException("Jira__ApiToken is not configured");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var jql = Uri.EscapeDataString($"project = {project} AND status = \"{status}\" AND issuetype in (Bug, Improvement, Story, Sub-bug) ORDER BY created DESC");
        var url = $"{baseUrl}/rest/api/2/search?jql={jql}&maxResults=50&fields=summary,assignee";

        logger.LogInformation("Querying JIRA [{Label}]: {Url}", logLabel, url);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HTTP error contacting JIRA [{Label}]", logLabel);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("JIRA [{Label}] returned {StatusCode}: {Body}", logLabel, (int)response.StatusCode, body);
            throw new HttpRequestException($"JIRA error {(int)response.StatusCode}: {body}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<JiraSearchResponse>(content, JsonOpts)
                      ?? throw new InvalidOperationException("Could not deserialize JIRA response");

        var tickets = result.Issues.Select(issue => new JiraTicket(
            Key:           issue.Key,
            Summary:       issue.Fields.Summary,
            Assignee:      issue.Fields.Assignee?.DisplayName ?? "Unassigned",
            AssigneeEmail: issue.Fields.Assignee?.EmailAddress ?? string.Empty,
            Url:           $"{baseUrl}/browse/{issue.Key}"
        )).ToList();

        logger.LogInformation("JIRA returned {Count} tickets in [{Label}]", tickets.Count, logLabel);
        return tickets;
    }
}
