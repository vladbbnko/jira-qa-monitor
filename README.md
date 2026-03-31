# Jira QA Monitor — Azure Function

## The Problem

In teams that rely on Jira for task tracking and Microsoft Teams for communication, critical workflow transitions often go unnoticed. A ticket sitting in **Ready For QA** for hours while the QA engineer is unaware, a **Verified** ticket waiting to be closed while the developer has forgotten about it, or a shipped feature that nobody celebrated — these are everyday friction points that slow down delivery and reduce team morale.

Manually checking Jira boards, setting up watchers, or relying on people to remember to notify the right person at the right time is error-prone and adds cognitive overhead.

**Jira QA Monitor** solves this by automatically watching your Jira board and pushing targeted Microsoft Teams notifications at every key transition — routing each card to the right team's channel and pinging the right person at the right moment, with no manual effort required.

---

An Azure Timer Function that monitors a Jira project for ticket status changes and sends **Microsoft Teams Adaptive Card** notifications via Power Automate webhooks.

**Four notification types out of the box:**
- 👀 **Resolved / In Review** — notifies the team when a ticket is in code review, with repeat reminders if it sits too long
- 🔔 **Ready For QA** — notifies the QA team when a ticket is ready to be tested
- ✅ **Verified** — notifies the assignee when a ticket is verified and ready to be closed
- 🎉 **Closed** — celebrates the team when a ticket is shipped

Each card includes:
- `@mention` of the assignee (real Teams ping)
- **Time spent in previous statuses** — weekend hours excluded, so only business time is counted
- **Pull Requests** — extracted from Jira comments (Azure DevOps URLs), shown on In Review, Still In Review, and Verified cards
- **Reviewer group tag** — optional `@TeamTag` shown on In Review and Still In Review cards (configured per team in `teams.json`)
- **Story Points** (Closed card only, shown if set)

Tickets are tracked by state so each ticket is only notified **once per status transition**.

---

## Architecture

```
Azure Timer Function (every 15 min, weekdays 6 AM–6 PM UTC)
    │
    ├─► Jira REST API          →  fetch tickets by status + full changelog
    ├─► Azure Blob Storage
    │       ├─ state.json      →  tracks seen ticket IDs + review entry times
    │       ├─ teams.json      →  maps assignee emails to team webhook URLs
    │       └─ settings.json   →  configures review reminder timing
    │
    ├─► Team routing           →  resolve webhook URL per assignee → team
    ├─► Power Automate Webhook (Resolved / In Review)  →  Teams card per ticket + reminders
    ├─► Power Automate Webhook (Ready For QA)          →  Teams card per ticket
    ├─► Power Automate Webhook (Verified)              →  Teams card per ticket
    └─► Power Automate Webhook (Closed)                →  Teams card per ticket
```

---

## Card Previews

**👀 In Review** — `emphasis` dark gray
```
┌──────────────────────────────────────────────┐
│  👀  READY FOR REVIEW — YOUR TURN!           │  ← gray header
│  This ticket is waiting for your review! 🔍  │
├──────────────────────────────────────────────┤
│  PROJECT-123                                 │
│  Short ticket summary here                   │
├──────────────────────────────────────────────┤
│  👤 Assignee              @John Smith        │
│  👥 Reviewers                 @CoreBE        │  ← only if resolvedTag.name set in teams.json
├──────────────────────────────────────────────┤
│  📊 Time in previous statuses                │
│  In Progress              3d 2h              │
├──────────────────────────────────────────────┤
│  🔀 Pull Requests                            │  ← extracted from Jira comments
│  repo-name                      PR #123      │
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

**⏰ Still In Review (reminder)** — `attention` red, fires after threshold + repeats on interval
```
┌──────────────────────────────────────────────┐
│  ⏰  STILL IN REVIEW — HURRY UP!             │  ← red header
│  This ticket has been in review for 6h 30m ⏳│  ← actual business hours elapsed
├──────────────────────────────────────────────┤
│  PROJECT-123                                 │
│  Short ticket summary here                   │
├──────────────────────────────────────────────┤
│  👤 Assignee              @John Smith        │
│  👥 Reviewers                 @CoreBE        │  ← only if resolvedTag.name set in teams.json
├──────────────────────────────────────────────┤
│  🔀 Pull Requests                            │
│  repo-name                      PR #123      │
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

**🔔 Ready For QA** — `accent` blue
```
┌──────────────────────────────────────────────┐
│  🔔  READY FOR QA — DO YOUR BEST!            │  ← blue header
│  Awaiting your testing! 👀                   │
├──────────────────────────────────────────────┤
│  PROJECT-123                                 │
│  Short ticket summary here                   │
├──────────────────────────────────────────────┤
│  👤 Assignee              @John Smith        │
├──────────────────────────────────────────────┤
│  📊 Time in previous statuses                │
│  In Progress              3d 2h              │
│  Resolved.                1d 1h              │
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

**✅ Verified** — `good` green
```
┌──────────────────────────────────────────────┐
│  ✅  VERIFIED — READY TO CLOSE               │  ← green header
│  Almost there! 💪                            │
├──────────────────────────────────────────────┤
│  PROJECT-123                                 │
│  Short ticket summary here                   │
├──────────────────────────────────────────────┤
│  👤 Assignee              @John Smith        │
├──────────────────────────────────────────────┤
│  📊 Time in previous statuses                │
│  In Progress              3d 2h              │
│  Ready For QA             4h 10m             │
├──────────────────────────────────────────────┤
│  🔀 Pull Requests                            │  ← extracted from Jira comments
│  repo-name                      PR #123      │
├──────────────────────────────────────────────┤
│  🔀 Please merge all related PRs             │
│     before closing this ticket               │
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

**🎉 Closed** — `warning` gold
```
┌──────────────────────────────────────────────┐
│  🎉  CLOSED — GREAT WORK!                    │  ← gold header
│  Another one bites the dust! 🚀              │
├──────────────────────────────────────────────┤
│  PROJECT-123                                 │
│  Short ticket summary here                   │
├──────────────────────────────────────────────┤
│  👤 Closed by             @John Smith        │
├──────────────────────────────────────────────┤
│  📊 Time in previous statuses                │
│  In Progress              3d 2h              │
│  Ready For QA             4h 10m             │
│  Verified                 2h 26m             │
├──────────────────────────────────────────────┤
│  🏆 Story Points                           5 │  ← shown only if set
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

---

## Team Routing

Notifications are routed to **per-team channels** based on the ticket's assignee email. Upload a `teams.json` file to your blob container:

```json
{
  "teams": [
    {
      "name": "FE",
      "members": ["john@company.com", "anna@company.com"],
      "resolvedTag": {
        "name": "CoreFE",
        "id": "tag:YOUR_TAG_ID"
      },
      "webhooks": {
        "resolved":   "https://power-automate-url-for-fe-channel",
        "readyForQa": "https://power-automate-url-for-fe-channel",
        "verified":   "https://power-automate-url-for-fe-channel",
        "closed":     "https://power-automate-url-for-fe-channel"
      }
    },
    {
      "name": "BE",
      "members": ["roman@company.com"],
      "resolvedTag": {
        "name": "CoreBE",
        "id": "tag:YOUR_TAG_ID"
      },
      "webhooks": {
        "resolved":   "https://power-automate-url-for-be-channel",
        "readyForQa": "https://power-automate-url-for-be-channel",
        "verified":   "https://power-automate-url-for-be-channel",
        "closed":     "https://power-automate-url-for-be-channel"
      }
    },
    {
      "name": "Mobile",
      "members": ["heorhii@company.com"],
      "webhooks": {
        "resolved":   "https://power-automate-url-for-mobile-channel",
        "readyForQa": "https://power-automate-url-for-mobile-channel",
        "verified":   "https://power-automate-url-for-mobile-channel",
        "closed":     "https://power-automate-url-for-mobile-channel"
      }
    },
    {
      "name": "QA",
      "members": ["anastasiia@company.com"],
      "webhooks": {
        "resolved":   "https://power-automate-url-for-qa-channel",
        "readyForQa": "https://power-automate-url-for-qa-channel",
        "verified":   "https://power-automate-url-for-qa-channel",
        "closed":     "https://power-automate-url-for-qa-channel"
      }
    }
  ],
  "fallbackWebhooks": {
    "resolved":   "https://power-automate-url-for-general-channel",
    "readyForQa": "https://power-automate-url-for-general-channel",
    "verified":   "https://power-automate-url-for-general-channel",
    "closed":     "https://power-automate-url-for-general-channel"
  }
}
```

- If the assignee matches a team → notification goes to that team's channel
- If no match → `fallbackWebhooks` is used
- `resolvedTag` is optional — omit it or leave `name` empty to skip the Reviewers row
- No redeploy needed to add/change teams — just update `teams.json` in the blob

### Pull Requests

Azure DevOps PR links are automatically extracted from Jira ticket comments and displayed on **In Review**, **Still In Review**, and **Verified** cards. No configuration needed — the function scans all comments for `dev.azure.com/.../pullrequest/{id}` URLs and deduplicates them by URL.

---

## Review Reminder

Tickets that stay in `Resolved.` (In Review) too long trigger automatic follow-up cards. Configure via `settings.json` in your blob container:

```json
{
  "reviewReminder": {
    "thresholdHours": 4,
    "intervalHours": 2
  }
}
```

| Setting | Description |
|---------|-------------|
| `thresholdHours` | Business hours in review before the first reminder fires |
| `intervalHours` | Business hours between subsequent reminders |

> Both values count **business hours only** — weekends are excluded. Defaults to 4h threshold / 2h interval if `settings.json` is not present.

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.0+ |
| Azure Functions Core Tools | v4 |
| Azure subscription | — |
| Jira account with API token | — |
| Power Automate webhook(s) | — |

---

## Local Development

1. **Copy settings**
   ```bash
   cp local.settings.json.example local.settings.json
   ```

2. **Fill in your values** in `local.settings.json`:
   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "Jira__BaseUrl": "https://your-org.atlassian.net",
       "Jira__Project": "YOUR_PROJECT_KEY",
       "Jira__Username": "your-email@example.com",
       "Jira__ApiToken": "your-jira-api-token",
       "State__BlobConnectionString": "UseDevelopmentStorage=true",
       "State__ContainerName": "jira-qa-monitor"
     }
   }
   ```

3. **Start Azurite** (local blob emulator):
   ```bash
   npx azurite --silent --location .azurite
   ```

4. **Upload `teams.json` and `settings.json`** to the local Azurite blob container

5. **Run the function**:
   ```bash
   dotnet build
   func start
   ```

6. **Trigger manually** (without waiting 15 min):
   ```bash
   curl -X POST http://localhost:7071/admin/functions/QaMonitorTimer \
     -H "Content-Type: application/json" \
     -d '{}'
   ```

---

## Deploy to Azure (Portal UI)

### 1. Create a Storage Account
- Create a **Storage Account** in your resource group (LRS is enough)
- Inside it, create a **Blob Container** named `jira-qa-monitor`
- Upload `teams.json` and `settings.json` to the container

### 2. Create a Function App
- **Runtime:** .NET 8 (isolated worker)
- **OS:** Linux
- **Plan:** Consumption (pay-as-you-go, effectively free for this workload)
- Link it to the storage account above

### 3. Configure Environment Variables
Go to **Function App → Settings → Environment variables** and add:

| Name | Value |
|------|-------|
| `Jira__BaseUrl` | `https://your-org.atlassian.net` |
| `Jira__Project` | your Jira project key |
| `Jira__Username` | your Jira email |
| `Jira__ApiToken` | your Jira API token |
| `State__BlobConnectionString` | connection string of your storage account |
| `State__ContainerName` | `jira-qa-monitor` |

> Webhook URLs are configured in `teams.json` — no webhook env vars needed.

### 4. Build & Deploy
```bash
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . && cd ..
```

Then in the Azure Portal go to **Function App → Development Tools → Advanced Tools → Kudu → Tools → Zip Push Deploy** and drag & drop `deploy.zip`.

> **Note:** Kudu is available on Consumption and Premium plans. It is **not** available on Flex Consumption.

### 5. Verify
Go to **Function App → Functions → QaMonitorTimer → Monitor** to see invocation logs.

---

## Configuration Reference

### Environment Variables

| Setting | Description |
|---------|-------------|
| `Jira__BaseUrl` | Jira instance root URL |
| `Jira__Project` | Jira project key to monitor |
| `Jira__Username` | Jira account email |
| `Jira__ApiToken` | Jira API token |
| `State__BlobConnectionString` | Azure Storage connection string |
| `State__ContainerName` | Blob container name (default: `jira-qa-monitor`) |

### Blob Files

| File | Purpose |
|------|---------|
| `state.json` | Auto-managed — tracks seen ticket IDs and review entry times |
| `teams.json` | Team definitions, member emails, and per-team webhook URLs |
| `settings.json` | Review reminder threshold and interval (business hours) |

---

## Customization

All monitoring rules live in `Services/JiraService.cs`.

**Tracked issue types** — by default: `Bug`, `Improvement`, `Story`, `Spike`:
```csharp
AND issuetype in (Bug, Improvement, Story, Spike)
```

**Monitored statuses** — map directly to your Jira workflow names. Update if your board uses different names:
```csharp
public Task<List<JiraTicket>> GetResolvedTicketsAsync()   => QueryTicketsAsync("Resolved.", ...);
public Task<List<JiraTicket>> GetReadyForQaTicketsAsync() => QueryTicketsAsync("Ready For QA", ...);
public Task<List<JiraTicket>> GetVerifiedTicketsAsync()   => QueryTicketsAsync("Verified", ...);
public Task<List<JiraTicket>> GetClosedTicketsAsync()     => QueryTicketsAsync("Closed", ...);
```

**Sprint filter** — only active sprint tickets are tracked by default:
```csharp
AND sprint in openSprints()
```
Remove this clause to track tickets across all sprints.

---

## Schedule

Runs every **15 minutes on weekdays between 6 AM and 6 PM UTC**.

To change the schedule, update the cron expression in `Functions/QaMonitorTimer.cs`:
```csharp
[TimerTrigger("0 */15 6-18 * * 1-5")]
```

---

## State File

Auto-managed in Azure Blob Storage at `{container}/state.json`:
```json
{
  "readyForQaIds": ["PROJECT-123", "PROJECT-456"],
  "resolvedEntries": [
    {
      "key": "PROJECT-789",
      "enteredAt": "2026-03-31T09:00:00Z",
      "initialNotified": true,
      "lastReminderAt": "2026-03-31T13:00:00Z"
    }
  ],
  "verifiedIds": ["PROJECT-100"],
  "closedIds":   ["PROJECT-090", "PROJECT-091"]
}
```

- `readyForQaIds` and `verifiedIds` — replaced on each run. If a ticket leaves and returns to the status it will be notified again.
- `resolvedEntries` — replaced on each run. Tracks entry time and last reminder timestamp per ticket.
- `closedIds` — only ever grows. Closed tickets are never re-notified.

---

## Security Recommendations

- Store `Jira__ApiToken` in **Azure Key Vault** and reference via `@Microsoft.KeyVault(...)` app setting syntax
- Enable **Managed Identity** on the Function App for Key Vault access
- Never commit `local.settings.json` to source control — it is already in `.gitignore`

---

## License

MIT
