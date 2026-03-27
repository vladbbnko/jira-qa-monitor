using System.Text;
using System.Text.Json;
using JiraQaMonitor.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Services;

public class WebhookService(HttpClient httpClient, IConfiguration config, ILogger<WebhookService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<bool> SendAsync(JiraTicket ticket)
    {
        var webhookUrl = config["Webhook__ReadyForQaUrl"]
            ?? Environment.GetEnvironmentVariable("Webhook__ReadyForQaUrl")
            ?? throw new InvalidOperationException("Webhook__ReadyForQaUrl is not configured");

        var (hasMention, mentionText, mentionEntry) = BuildMention(ticket);

        var body = new List<object>
        {
            BuildHeader("🔔  READY FOR QA", "👀 Awaiting your testing", "Accent"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText)
        };
        body.AddRange(BuildHistorySection(ticket.StatusHistory));

        var payload = BuildPayload(hasMention, mentionEntry, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendVerifiedAsync(JiraTicket ticket)
    {
        var webhookUrl = config["Webhook__VerifiedUrl"]
            ?? Environment.GetEnvironmentVariable("Webhook__VerifiedUrl")
            ?? throw new InvalidOperationException("Webhook__VerifiedUrl is not configured");

        var (hasMention, mentionText, mentionEntry) = BuildMention(ticket);

        var body = new List<object>
        {
            BuildHeader("✅  VERIFIED — READY TO CLOSE", "Almost there! 💪", "Good"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText)
        };
        body.AddRange(BuildHistorySection(ticket.StatusHistory));
        body.Add(new
        {
            type      = "TextBlock",
            text      = "🔀 Please merge all related PRs before closing this ticket",
            wrap      = true,
            spacing   = "Medium",
            separator = true,
            color     = "Warning",
            weight    = "Bolder"
        });

        var payload = BuildPayload(hasMention, mentionEntry, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendClosedAsync(JiraTicket ticket)
    {
        var webhookUrl = config["Webhook__ClosedUrl"]
            ?? Environment.GetEnvironmentVariable("Webhook__ClosedUrl")
            ?? throw new InvalidOperationException("Webhook__ClosedUrl is not configured");

        var (hasMention, mentionText, mentionEntry) = BuildMention(ticket);

        var body = new List<object>
        {
            BuildHeader("🎉  CLOSED — GREAT WORK!", "Another one bites the dust! 🚀", "Attention"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText, label: "👤 Closed by")
        };
        body.AddRange(BuildHistorySection(ticket.StatusHistory));

        var payload = BuildPayload(hasMention, mentionEntry, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    // ── Shared card builders ──────────────────────────────────────────────────

    private static (bool hasMention, string mentionText, object mentionEntry) BuildMention(JiraTicket ticket)
    {
        var has  = !string.IsNullOrEmpty(ticket.AssigneeEmail);
        var text = has ? $"<at>{ticket.Assignee}</at>" : ticket.Assignee;
        var entry = new { type = "mention", text = $"<at>{ticket.Assignee}</at>", mentioned = new { id = ticket.AssigneeEmail, name = ticket.Assignee } };
        return (has, text, entry);
    }

    private static object BuildHeader(string title, string subtitle, string color) => new
    {
        type  = "Container",
        items = new object[]
        {
            new { type = "TextBlock", text = title,    weight = "Bolder", size = "Large", color, wrap = true },
            new { type = "TextBlock", text = subtitle, size   = "Small",  wrap = true,    spacing = "None", isSubtle = true }
        }
    };

    private static object BuildTicketBlock(JiraTicket ticket) => new
    {
        type    = "Container",
        spacing = "Medium",
        items   = new object[]
        {
            new { type = "TextBlock", text = ticket.Key,     weight = "Bolder", size = "Large", color = "Accent", wrap = false },
            new { type = "TextBlock", text = ticket.Summary, wrap   = true,      spacing = "Small" }
        }
    };

    private static object BuildAssigneeRow(string mentionText, string label = "👤 Assignee") => new
    {
        type      = "ColumnSet",
        spacing   = "Medium",
        separator = true,
        columns   = new object[]
        {
            new { type = "Column", width = "auto",    items = new object[] { new { type = "TextBlock", text = label,       weight = "Bolder", wrap = false } } },
            new { type = "Column", width = "stretch", items = new object[] { new { type = "TextBlock", text = mentionText, wrap   = false, horizontalAlignment = "Right", color = "Good" } } }
        }
    };

    private static IEnumerable<object> BuildHistorySection(IReadOnlyList<StatusDuration> history)
    {
        var filtered = history
            .Where(s => s.Duration.TotalMinutes >= 1)
            .ToList();

        if (filtered.Count == 0) yield break;

        yield return new { type = "TextBlock", text = "📊 Time in previous statuses", weight = "Bolder", spacing = "Medium", separator = true };
        yield return new
        {
            type  = "FactSet",
            facts = filtered.Select(s => new { title = s.Status, value = FormatDuration(s.Duration) }).ToArray()
        };
    }

    private static object BuildPayload(bool hasMention, object mentionEntry, List<object> body, string url) => new
    {
        type    = "AdaptiveCard",
        schema  = "http://adaptivecards.io/schemas/adaptive-card.json",
        version = "1.2",
        msteams = hasMention ? (object)new { entities = new[] { mentionEntry } } : new { entities = Array.Empty<object>() },
        body    = body.ToArray(),
        actions = new object[] { new { type = "Action.OpenUrl", title = "Open in Jira →", url, style = "positive" } }
    };

    private static string FormatDuration(TimeSpan d) =>
        d.TotalDays  >= 1 ? $"{(int)d.TotalDays}d {d.Hours}h"    :
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes}m" :
                            $"{d.Minutes}m";

    private async Task<bool> PostCardAsync(string webhookUrl, object payload, string ticketKey)
    {
        var json    = JsonSerializer.Serialize(payload, SerializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(webhookUrl, content);
            var code     = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Webhook sent for {Key} — HTTP {Code}", ticketKey, code);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("Webhook failed for {Key} — HTTP {Code}: {Body}", ticketKey, code, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook exception for {Key}", ticketKey);
            return false;
        }
    }
}
