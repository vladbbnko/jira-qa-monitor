using JiraQaMonitor.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JiraQaMonitor.Functions;

public class QaMonitorTimer(
    JiraService     jiraService,
    StateService    stateService,
    WebhookService  webhookService,
    ILogger<QaMonitorTimer> logger)
{
    [Function(nameof(QaMonitorTimer))]
    public async Task Run([TimerTrigger("0 */15 6-18 * * 1-5")] TimerInfo timerInfo)
    {
        logger.LogInformation("QA Monitor started at {Time}", DateTimeOffset.UtcNow);

        var state = await stateService.LoadStateAsync();

        await ProcessReadyForQaAsync(state);
        await ProcessVerifiedAsync(state);

        await stateService.SaveStateAsync(state);
    }

    // ── Ready For QA ──────────────────────────────────────────────────────────

    private async Task ProcessReadyForQaAsync(Models.MonitorState state)
    {
        List<Models.JiraTicket> tickets;
        try
        {
            tickets = await jiraService.GetReadyForQaTicketsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JIRA [Ready For QA] search failed — state not updated");
            return;
        }

        var currentKeys = tickets.Select(t => t.Key).ToHashSet();
        var newTickets  = tickets.Where(t => !state.ReadyForQaIds.Contains(t.Key)).ToList();

        int sent = 0, failed = 0;
        foreach (var ticket in newTickets)
        {
            var success = await webhookService.SendAsync(ticket);
            if (success) sent++;
            else         failed++;
        }

        state.ReadyForQaIds = currentKeys.ToList();

        if (newTickets.Count == 0)
            logger.LogInformation("Ready For QA: {Total} total, 0 new.", tickets.Count);
        else
            logger.LogInformation("Ready For QA: {Total} total, {New} new: {Keys}. Sent: {Sent}, failed: {Failed}.",
                tickets.Count, newTickets.Count, string.Join(", ", newTickets.Select(t => t.Key)), sent, failed);
    }

    // ── Verified ──────────────────────────────────────────────────────────────

    private async Task ProcessVerifiedAsync(Models.MonitorState state)
    {
        List<Models.JiraTicket> tickets;
        try
        {
            tickets = await jiraService.GetVerifiedTicketsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JIRA [Verified] search failed — state not updated");
            return;
        }

        var currentKeys = tickets.Select(t => t.Key).ToHashSet();
        var newTickets  = tickets.Where(t => !state.VerifiedIds.Contains(t.Key)).ToList();

        int sent = 0, failed = 0;
        foreach (var ticket in newTickets)
        {
            var success = await webhookService.SendVerifiedAsync(ticket);
            if (success) sent++;
            else         failed++;
        }

        state.VerifiedIds = currentKeys.ToList();

        if (newTickets.Count == 0)
            logger.LogInformation("Verified: {Total} total, 0 new.", tickets.Count);
        else
            logger.LogInformation("Verified: {Total} total, {New} new: {Keys}. Sent: {Sent}, failed: {Failed}.",
                tickets.Count, newTickets.Count, string.Join(", ", newTickets.Select(t => t.Key)), sent, failed);
    }
}
