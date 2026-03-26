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

        var hasMention   = !string.IsNullOrEmpty(ticket.AssigneeEmail);
        var mentionText  = hasMention ? $"<at>{ticket.Assignee}</at>" : ticket.Assignee;
        var mentionEntry = new
        {
            type = "mention",
            text = $"<at>{ticket.Assignee}</at>",
            mentioned = new { id = ticket.AssigneeEmail, name = ticket.Assignee }
        };

        var payload = new
        {
            type    = "AdaptiveCard",
            schema  = "http://adaptivecards.io/schemas/adaptive-card.json",
            version = "1.2",
            msteams = hasMention
                ? (object)new { entities = new[] { mentionEntry } }
                : new { entities = Array.Empty<object>() },
            body = new object[]
            {
                // Header
                new
                {
                    type  = "Container",
                    style = "accent",
                    bleed = true,
                    items = new object[]
                    {
                        new
                        {
                            type   = "TextBlock",
                            text   = "🔔  READY FOR QA",
                            weight = "Bolder",
                            size   = "Medium",
                            wrap   = true
                        }
                    }
                },
                // Ticket key + summary
                new
                {
                    type    = "Container",
                    spacing = "Medium",
                    items   = new object[]
                    {
                        new
                        {
                            type   = "TextBlock",
                            text   = ticket.Key,
                            weight = "Bolder",
                            size   = "Large",
                            color  = "Accent",
                            wrap   = false
                        },
                        new
                        {
                            type    = "TextBlock",
                            text    = ticket.Summary,
                            wrap    = true,
                            spacing = "Small"
                        }
                    }
                },
                // Assignee row
                new
                {
                    type      = "ColumnSet",
                    spacing   = "Medium",
                    separator = true,
                    columns   = new object[]
                    {
                        new
                        {
                            type  = "Column",
                            width = "auto",
                            items = new object[]
                            {
                                new
                                {
                                    type   = "TextBlock",
                                    text   = "👤 Assignee",
                                    weight = "Bolder",
                                    wrap   = false
                                }
                            }
                        },
                        new
                        {
                            type  = "Column",
                            width = "stretch",
                            items = new object[]
                            {
                                new
                                {
                                    type                = "TextBlock",
                                    text                = mentionText,
                                    wrap                = false,
                                    horizontalAlignment = "Right",
                                    color               = "Good"
                                }
                            }
                        }
                    }
                }
            },
            actions = new object[]
            {
                new
                {
                    type  = "Action.OpenUrl",
                    title = "Open in Jira →",
                    url   = ticket.Url,
                    style = "positive"
                }
            }
        };

        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

    public async Task<bool> SendVerifiedAsync(JiraTicket ticket)
    {
        var webhookUrl = config["Webhook__VerifiedUrl"]
            ?? Environment.GetEnvironmentVariable("Webhook__VerifiedUrl")
            ?? throw new InvalidOperationException("Webhook__VerifiedUrl is not configured");

        var hasMention   = !string.IsNullOrEmpty(ticket.AssigneeEmail);
        var mentionText  = hasMention ? $"<at>{ticket.Assignee}</at>" : ticket.Assignee;
        var mentionEntry = new
        {
            type      = "mention",
            text      = $"<at>{ticket.Assignee}</at>",
            mentioned = new { id = ticket.AssigneeEmail, name = ticket.Assignee }
        };

        var payload = new
        {
            type    = "AdaptiveCard",
            schema  = "http://adaptivecards.io/schemas/adaptive-card.json",
            version = "1.2",
            msteams = hasMention
                ? (object)new { entities = new[] { mentionEntry } }
                : new { entities = Array.Empty<object>() },
            body = new object[]
            {
                // Header — green "good" style
                new
                {
                    type  = "Container",
                    style = "good",
                    bleed = true,
                    items = new object[]
                    {
                        new
                        {
                            type   = "TextBlock",
                            text   = "✅  VERIFIED — READY TO CLOSE",
                            weight = "Bolder",
                            size   = "Medium",
                            wrap   = true
                        }
                    }
                },
                // Ticket key + summary
                new
                {
                    type    = "Container",
                    spacing = "Medium",
                    items   = new object[]
                    {
                        new
                        {
                            type   = "TextBlock",
                            text   = ticket.Key,
                            weight = "Bolder",
                            size   = "Large",
                            color  = "Accent",
                            wrap   = false
                        },
                        new
                        {
                            type    = "TextBlock",
                            text    = ticket.Summary,
                            wrap    = true,
                            spacing = "Small"
                        }
                    }
                },
                // Assignee row
                new
                {
                    type      = "ColumnSet",
                    spacing   = "Medium",
                    separator = true,
                    columns   = new object[]
                    {
                        new
                        {
                            type  = "Column",
                            width = "auto",
                            items = new object[]
                            {
                                new
                                {
                                    type   = "TextBlock",
                                    text   = "👤 Assignee",
                                    weight = "Bolder",
                                    wrap   = false
                                }
                            }
                        },
                        new
                        {
                            type  = "Column",
                            width = "stretch",
                            items = new object[]
                            {
                                new
                                {
                                    type                = "TextBlock",
                                    text                = mentionText,
                                    wrap                = false,
                                    horizontalAlignment = "Right",
                                    color               = "Good"
                                }
                            }
                        }
                    }
                },
                // PR reminder
                new
                {
                    type      = "TextBlock",
                    text      = "🔀 Please merge all related PRs before closing this ticket",
                    wrap      = true,
                    spacing   = "Medium",
                    separator = true,
                    color     = "Warning",
                    weight    = "Bolder"
                }
            },
            actions = new object[]
            {
                new
                {
                    type  = "Action.OpenUrl",
                    title = "Open in Jira →",
                    url   = ticket.Url,
                    style = "positive"
                }
            }
        };

        return await PostCardAsync(webhookUrl, payload, ticket.Key);
    }

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
