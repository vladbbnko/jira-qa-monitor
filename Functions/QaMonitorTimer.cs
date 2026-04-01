using JiraQaMonitor.Models;
using JiraQaMonitor.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Functions;

public class QaMonitorTimer(
    JiraService             jiraService,
    StateService            stateService,
    WebhookService          webhookService,
    TeamConfigService       teamConfigService,
    SettingsService         settingsService,
    IConfiguration          config,
    ILogger<QaMonitorTimer> logger)
{
    [Function(nameof(QaMonitorTimer))]
    public async Task Run([TimerTrigger("0 */15 9-19 * * 1-5")] TimerInfo timerInfo)
    {
        logger.LogInformation("QA Monitor started at {Time}", DateTimeOffset.UtcNow);

        var state      = await stateService.LoadStateAsync();
        var teamConfig = await teamConfigService.LoadAsync();
        var settings   = await settingsService.LoadAsync();

        await ProcessReadyForQaAsync(state, teamConfig, settings);
        await ProcessResolvedAsync(state, teamConfig, settings);
        await ProcessVerifiedAsync(state, teamConfig);
        await ProcessClosedAsync(state, teamConfig);

        await stateService.SaveStateAsync(state);
    }

    // ── Ready For QA ──────────────────────────────────────────────────────────

    private async Task ProcessReadyForQaAsync(MonitorState state, TeamConfig? teamConfig, AppSettings settings)
    {
        List<Models.JiraTicket> tickets;
        try   { tickets = await jiraService.GetReadyForQaTicketsAsync(); }
        catch (Exception ex) { logger.LogError(ex, "Jira [Ready For QA] search failed"); return; }

        if (settings.ExcludedSummaryKeywords.Count > 0)
            tickets = tickets
                .Where(t => !settings.ExcludedSummaryKeywords
                    .Any(kw => t.Summary.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        var currentKeys = tickets.Select(t => t.Key).ToHashSet();
        var newTickets  = tickets.Where(t => !state.ReadyForQaIds.Contains(t.Key)).ToList();

        int sent = 0, failed = 0;
        foreach (var ticket in newTickets)
        {
            var url = ResolveWebhook(teamConfig, ticket.AssigneeEmail, w => w.ReadyForQa, "Webhook__ReadyForQaUrl");
            if (url is null) { logger.LogWarning("No ReadyForQa webhook for {Key} — skipping", ticket.Key); failed++; continue; }

            if (await webhookService.SendAsync(ticket, url)) sent++; else failed++;
        }

        state.ReadyForQaIds = currentKeys.ToList();
        LogSummary("Ready For QA", tickets.Count, newTickets, sent, failed);
    }

    // ── Resolved ──────────────────────────────────────────────────────────────

    private async Task ProcessResolvedAsync(MonitorState state, TeamConfig? teamConfig, AppSettings settings)
    {
        List<Models.JiraTicket> tickets;
        try   { tickets = await jiraService.GetResolvedTicketsAsync(); }
        catch (Exception ex) { logger.LogError(ex, "Jira [Resolved] search failed"); return; }

        var currentKeys = tickets.Select(t => t.Key).ToHashSet();
        var now         = DateTime.UtcNow;

        // Remove entries for tickets no longer in Resolved.
        state.ResolvedEntries.RemoveAll(e => !currentKeys.Contains(e.Key));

        int sent = 0, failed = 0;
        var newTickets = new List<Models.JiraTicket>();

        foreach (var ticket in tickets)
        {
            var url = ResolveWebhook(teamConfig, ticket.AssigneeEmail, w => w.Resolved, "Webhook__ResolvedUrl");
            if (url is null) { logger.LogWarning("No Resolved webhook for {Key} — skipping", ticket.Key); failed++; continue; }

            var entry = state.ResolvedEntries.FirstOrDefault(e => e.Key == ticket.Key);

            var otherMembers = teamConfig is not null
                ? teamConfigService.GetOtherMembers(teamConfig, ticket.AssigneeEmail)
                : [];

            if (entry is null)
            {
                // First time seeing this ticket in Resolved. — send initial card
                var enteredAt = ResolvedEntryTime(ticket);
                entry = new ResolvedEntry { Key = ticket.Key, EnteredAt = enteredAt };
                state.ResolvedEntries.Add(entry);
                newTickets.Add(ticket);

                if (await webhookService.SendResolvedAsync(ticket, url, otherMembers)) { sent++; entry.InitialNotified = true; }
                else failed++;
                continue;
            }

            if (!entry.InitialNotified) continue;

            // Check if a reminder is due
            var elapsed          = BusinessDuration(entry.EnteredAt, now);
            var threshold        = TimeSpan.FromHours(settings.ReviewReminder.ThresholdHours);
            var interval         = TimeSpan.FromHours(settings.ReviewReminder.IntervalHours);
            var sinceLastReminder = entry.LastReminderAt.HasValue
                ? BusinessDuration(entry.LastReminderAt.Value, now)
                : TimeSpan.MaxValue;

            var firstReminderDue      = elapsed >= threshold && entry.LastReminderAt is null;
            var subsequentReminderDue = entry.LastReminderAt is not null && sinceLastReminder >= interval;

            if (firstReminderDue || subsequentReminderDue)
            {
                if (await webhookService.SendReviewReminderAsync(ticket, url, elapsed, otherMembers))
                {
                    sent++;
                    entry.LastReminderAt = now;
                }
                else failed++;
            }
        }

        LogSummary("Resolved", tickets.Count, newTickets, sent, failed);
    }

    private static DateTime ResolvedEntryTime(Models.JiraTicket ticket)
        => ticket.CurrentStatusEnteredAt ?? DateTime.UtcNow;

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

    // ── Verified ──────────────────────────────────────────────────────────────

    private async Task ProcessVerifiedAsync(MonitorState state, TeamConfig? teamConfig)
    {
        List<Models.JiraTicket> tickets;
        try   { tickets = await jiraService.GetVerifiedTicketsAsync(); }
        catch (Exception ex) { logger.LogError(ex, "Jira [Verified] search failed"); return; }

        var currentKeys = tickets.Select(t => t.Key).ToHashSet();
        var newTickets  = tickets.Where(t => !state.VerifiedIds.Contains(t.Key)).ToList();

        int sent = 0, failed = 0;
        foreach (var ticket in newTickets)
        {
            var url = ResolveWebhook(teamConfig, ticket.AssigneeEmail, w => w.Verified, "Webhook__VerifiedUrl");
            if (url is null) { logger.LogWarning("No Verified webhook for {Key} — skipping", ticket.Key); failed++; continue; }

            if (await webhookService.SendVerifiedAsync(ticket, url)) sent++; else failed++;
        }

        state.VerifiedIds = currentKeys.ToList();
        LogSummary("Verified", tickets.Count, newTickets, sent, failed);
    }

    // ── Closed ────────────────────────────────────────────────────────────────

    private async Task ProcessClosedAsync(MonitorState state, TeamConfig? teamConfig)
    {
        List<Models.JiraTicket> tickets;
        try   { tickets = await jiraService.GetClosedTicketsAsync(); }
        catch (Exception ex) { logger.LogError(ex, "Jira [Closed] search failed"); return; }

        var newTickets = tickets.Where(t => !state.ClosedIds.Contains(t.Key)).ToList();

        int sent = 0, failed = 0;
        foreach (var ticket in newTickets)
        {
            var url = ResolveWebhook(teamConfig, ticket.AssigneeEmail, w => w.Closed, "Webhook__ClosedUrl");
            if (url is null) { logger.LogWarning("No Closed webhook for {Key} — skipping", ticket.Key); failed++; continue; }

            if (await webhookService.SendClosedAsync(ticket, url)) sent++; else failed++;
        }

        state.ClosedIds.AddRange(newTickets.Select(t => t.Key));
        LogSummary("Closed", tickets.Count, newTickets, sent, failed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string? ResolveWebhook(TeamConfig? teamConfig, string assigneeEmail, Func<TeamWebhooks, string?> selector, string envVarKey)
    {
        if (teamConfig is not null)
            return teamConfigService.ResolveWebhook(teamConfig, assigneeEmail, selector);

        return config[envVarKey] ?? Environment.GetEnvironmentVariable(envVarKey);
    }

    private void LogSummary(string label, int total, List<Models.JiraTicket> newTickets, int sent, int failed)
    {
        if (newTickets.Count == 0)
            logger.LogInformation("{Label}: {Total} total, 0 new.", label, total);
        else
            logger.LogInformation("{Label}: {Total} total, {New} new: {Keys}. Sent: {Sent}, failed: {Failed}.",
                label, total, newTickets.Count, string.Join(", ", newTickets.Select(t => t.Key)), sent, failed);
    }
}
