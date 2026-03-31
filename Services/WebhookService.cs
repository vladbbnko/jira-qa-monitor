using System.Text;
using System.Text.Json;
using JiraQaMonitor.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Services;

public class WebhookService(HttpClient httpClient, ILogger<WebhookService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<bool> SendAsync(JiraTicket ticket, string webhookUrl)
    {
        var (mentionText, mentionEntry) = BuildMention(ticket);
        var entities = mentionEntry is not null ? new[] { mentionEntry } : [];

        var body = new List<object>
        {
            BuildHeader("🔔  READY FOR QA — DO YOUR BEST!", "Awaiting your testing! 👀", "accent"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText)
        };
        body.AddRange(BuildHistorySection(ticket.StatusHistory));

        var payload = BuildPayload(entities, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendResolvedAsync(JiraTicket ticket, string webhookUrl, TeamTag? teamTag = null)
    {
        var (mentionText, mentionEntry) = BuildMention(ticket);
        var entities = BuildEntities(mentionEntry, teamTag);

        var body = new List<object>
        {
            BuildHeader("👀  READY FOR REVIEW — YOUR TURN!", "This ticket is waiting for your review! 🔍", "emphasis"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText)
        };
        if (teamTag is not null)
            body.Add(BuildTeamTagRow($"<at>{teamTag.Name}</at>"));
        body.AddRange(BuildHistorySection(ticket.StatusHistory));

        var payload = BuildPayload(entities, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendReviewReminderAsync(JiraTicket ticket, string webhookUrl, TimeSpan elapsed, TeamTag? teamTag = null)
    {
        var (mentionText, mentionEntry) = BuildMention(ticket);
        var entities = BuildEntities(mentionEntry, teamTag);

        var body = new List<object>
        {
            BuildHeader("⏰  STILL IN REVIEW — HURRY UP!", $"This ticket has been in review for {FormatDuration(elapsed)} ⏳", "attention"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText)
        };
        if (teamTag is not null)
            body.Add(BuildTeamTagRow($"<at>{teamTag.Name}</at>"));

        var payload = BuildPayload(entities, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendVerifiedAsync(JiraTicket ticket, string webhookUrl)
    {
        var (mentionText, mentionEntry) = BuildMention(ticket);
        var entities = mentionEntry is not null ? new[] { mentionEntry } : [];

        var body = new List<object>
        {
            BuildHeader("✅  VERIFIED — READY TO CLOSE", "Almost there! 💪", "good"),
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

        var payload = BuildPayload(entities, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendClosedAsync(JiraTicket ticket, string webhookUrl)
    {
        var (mentionText, mentionEntry) = BuildMention(ticket);
        var entities = mentionEntry is not null ? new[] { mentionEntry } : [];

        var body = new List<object>
        {
            BuildHeader("🎉  CLOSED — GREAT WORK!", "Another one bites the dust! 🚀", "warning"),
            BuildTicketBlock(ticket),
            BuildAssigneeRow(mentionText, label: "👤 Closed by")
        };
        body.AddRange(BuildHistorySection(ticket.StatusHistory));
        if (ticket.StoryPoints.HasValue && ticket.StoryPoints.Value > 0)
            body.Add(BuildStoryPoints(ticket.StoryPoints.Value));

        var payload = BuildPayload(entities, body, ticket.Url);
        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    // ── Shared card builders ──────────────────────────────────────────────────

    private static (string mentionText, object? mentionEntry) BuildMention(JiraTicket ticket)
    {
        var has   = !string.IsNullOrEmpty(ticket.AssigneeEmail);
        var text  = has ? $"<at>{ticket.Assignee}</at>" : ticket.Assignee;
        var entry = has
            ? (object?)new { type = "mention", text = $"<at>{ticket.Assignee}</at>", mentioned = new { id = ticket.AssigneeEmail, name = ticket.Assignee } }
            : null;
        return (text, entry);
    }

    private static object[] BuildEntities(object? assigneeMention, TeamTag? teamTag)
    {
        var list = new List<object>();
        if (assigneeMention is not null) list.Add(assigneeMention);
        if (teamTag is not null)
            list.Add(new { type = "mention", text = $"<at>{teamTag.Name}</at>", mentioned = new { id = teamTag.Id, name = teamTag.Name } });
        return list.ToArray();
    }

    private static object BuildHeader(string title, string subtitle, string style) => new
    {
        type    = "Container",
        style   = style,
        bleed   = true,
        spacing = "None",
        items   = new object[]
        {
            new { type = "TextBlock", text = title,    weight = "Bolder", size = "Large", wrap = true },
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

    private static object BuildTeamTagRow(string tagMentionText) => new
    {
        type    = "ColumnSet",
        spacing = "Small",
        columns = new object[]
        {
            new { type = "Column", width = "auto",    items = new object[] { new { type = "TextBlock", text = "👥 Reviewers", weight = "Bolder", wrap = false } } },
            new { type = "Column", width = "stretch", items = new object[] { new { type = "TextBlock", text = tagMentionText, wrap = false, horizontalAlignment = "Right", color = "Accent" } } }
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

    private static object BuildStoryPoints(double points) => new
    {
        type      = "ColumnSet",
        spacing   = "Medium",
        separator = true,
        columns   = new object[]
        {
            new { type = "Column", width = "auto",    items = new object[] { new { type = "TextBlock", text = "🏆 Story Points",                       weight = "Bolder", wrap = false } } },
            new { type = "Column", width = "stretch", items = new object[] { new { type = "TextBlock", text = ((double)points).ToString(), wrap = false, horizontalAlignment = "Right", weight = "Bolder", color = "Good" } } }
        }
    };

    private static object BuildPayload(object[] mentionEntities, List<object> body, string url) => new
    {
        type    = "AdaptiveCard",
        schema  = "http://adaptivecards.io/schemas/adaptive-card.json",
        version = "1.2",
        msteams = new { entities = mentionEntities },
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
