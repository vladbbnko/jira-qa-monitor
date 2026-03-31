using System.Globalization;
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
        => await QueryTicketsAsync("Ready For QA", includeHistory: true);

    public async Task<List<JiraTicket>> GetVerifiedTicketsAsync()
        => await QueryTicketsAsync("Verified", includeHistory: true);

    public async Task<List<JiraTicket>> GetResolvedTicketsAsync()
        => await QueryTicketsAsync("Resolved.", includeHistory: true);

    public async Task<List<JiraTicket>> GetClosedTicketsAsync()
        => await QueryTicketsAsync("Closed", includeHistory: true);

    private async Task<List<JiraTicket>> QueryTicketsAsync(string status, bool includeHistory)
    {
        var baseUrl  = config["Jira__BaseUrl"]  ?? Environment.GetEnvironmentVariable("Jira__BaseUrl")  ?? throw new InvalidOperationException("Jira__BaseUrl is not configured");
        var project  = config["Jira__Project"]  ?? Environment.GetEnvironmentVariable("Jira__Project")  ?? throw new InvalidOperationException("Jira__Project is not configured");
        var username = config["Jira__Username"] ?? Environment.GetEnvironmentVariable("Jira__Username") ?? throw new InvalidOperationException("Jira__Username is not configured");
        var apiToken = config["Jira__ApiToken"] ?? Environment.GetEnvironmentVariable("Jira__ApiToken") ?? throw new InvalidOperationException("Jira__ApiToken is not configured");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var jql    = Uri.EscapeDataString($"project = {project} AND status = \"{status}\" AND issuetype in (Bug, Improvement, Story, Spike) AND sprint in openSprints() ORDER BY created DESC");
        var expand = includeHistory ? "&expand=changelog" : string.Empty;
        var url    = $"{baseUrl}/rest/api/2/search?jql={jql}&maxResults=50&fields=summary,assignee,customfield_10004{expand}";

        logger.LogInformation("Querying Jira [{Status}]: {Url}", status, url);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HTTP error contacting Jira [{Status}]", status);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("Jira [{Status}] returned {StatusCode}: {Body}", status, (int)response.StatusCode, body);
            throw new HttpRequestException($"Jira error {(int)response.StatusCode}: {body}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<JiraSearchResponse>(content, JsonOpts)
                      ?? throw new InvalidOperationException("Could not deserialize Jira response");

        var tickets = result.Issues.Select(issue => new JiraTicket(
            Key:                    issue.Key,
            Summary:                issue.Fields.Summary,
            Assignee:               issue.Fields.Assignee?.DisplayName ?? "Unassigned",
            AssigneeEmail:          issue.Fields.Assignee?.EmailAddress ?? string.Empty,
            Url:                    $"{baseUrl}/browse/{issue.Key}",
            StatusHistory:          includeHistory ? ExtractLastStints(issue.Changelog, status) : [],
            StoryPoints:            issue.Fields.StoryPoints,
            CurrentStatusEnteredAt: includeHistory ? ExtractStatusEnteredAt(issue.Changelog, status) : null
        )).ToList();

        logger.LogInformation("Jira returned {Count} tickets in [{Status}]", tickets.Count, status);
        return tickets;
    }

    // Returns the last continuous stint in each status, in chronological order of exit,
    // excluding the current status (e.g. "Ready For QA" itself).
    private static IReadOnlyList<StatusDuration> ExtractLastStints(JiraChangelog? changelog, string currentStatus)
    {
        if (changelog is null) return [];

        var transitions = changelog.Histories
            .SelectMany(h => h.Items
                .Where(i => i.Field == "status" && i.FromStatus != null && i.ToStatus != null)
                .Select(i => new
                {
                    At   = DateTime.Parse(h.Created, null, DateTimeStyles.RoundtripKind),
                    From = i.FromStatus!,
                    To   = i.ToStatus!
                }))
            .OrderBy(t => t.At)
            .ToList();

        if (transitions.Count == 0) return [];

        // Track the last time each status was entered.
        // When we see a transition away from a status, record (exitedAt, duration).
        // Overwriting ensures we keep only the last stint.
        var lastEnteredAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var lastStint     = new Dictionary<string, (DateTime ExitedAt, TimeSpan Duration)>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in transitions)
        {
            if (lastEnteredAt.TryGetValue(t.From, out var enteredAt))
                lastStint[t.From] = (t.At, BusinessDuration(enteredAt, t.At));

            lastEnteredAt[t.To] = t.At;
        }

        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentStatus, "Reopened" };


        return lastStint
            .Where(kvp => !ignored.Contains(kvp.Key))
            .OrderBy(kvp => kvp.Value.ExitedAt)
            .Select(kvp => new StatusDuration(kvp.Key, kvp.Value.Duration))
            .ToList();
    }

    // Returns the last time the ticket transitioned INTO the given status.
    private static DateTime? ExtractStatusEnteredAt(JiraChangelog? changelog, string status)
    {
        if (changelog is null) return null;

        return changelog.Histories
            .SelectMany(h => h.Items
                .Where(i => i.Field == "status" && string.Equals(i.ToStatus, status, StringComparison.OrdinalIgnoreCase))
                .Select(_ => DateTime.Parse(h.Created, null, DateTimeStyles.RoundtripKind)))
            .OrderByDescending(d => d)
            .FirstOrDefault() is DateTime dt ? dt : null;
    }

    // Calculates duration between two timestamps counting only weekday hours.
    private static TimeSpan BusinessDuration(DateTime start, DateTime end)
    {
        if (start >= end) return TimeSpan.Zero;

        var total   = TimeSpan.Zero;
        var current = start;

        while (current < end)
        {
            var nextMidnight = current.Date.AddDays(1);
            var periodEnd    = nextMidnight < end ? nextMidnight : end;

            if (current.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                total += periodEnd - current;

            current = nextMidnight;
        }

        return total;
    }
}
