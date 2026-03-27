# Jira QA Monitor — Azure Function

## The Problem

In teams that rely on Jira for task tracking and Microsoft Teams for communication, critical workflow transitions often go unnoticed. A ticket sitting in **Ready For QA** for hours while the QA engineer is unaware, a **Verified** ticket waiting to be closed while the developer has forgotten about it, or a shipped feature that nobody celebrated — these are everyday friction points that slow down delivery and reduce team morale.

Manually checking Jira boards, setting up watchers, or relying on people to remember to notify the right person at the right time is error-prone and adds cognitive overhead.

**Jira QA Monitor** solves this by automatically watching your Jira board and pushing targeted, beautiful Microsoft Teams notifications at every key transition — so the right person is pinged at the right moment, no manual effort required.

---

An Azure Timer Function that monitors a Jira project for tickets changing status and sends **Microsoft Teams Adaptive Card** notifications via Power Automate webhook.

**Three notification channels out of the box:**
- 🔔 **Ready For QA** — notifies the team when a ticket is ready to be tested
- ✅ **Verified** — notifies the assignee when a ticket is verified and ready to be closed (with a PR merge reminder)
- 🎉 **Closed** — celebrates the team when a ticket is shipped

Each card includes:
- `@mention` of the assignee (real Teams ping)
- **Time spent in previous statuses** (e.g. In Progress: 3d 2h, Ready For QA: 4h 10m)

Tickets are tracked by state so each ticket is only notified **once per status**.

---

## Architecture

```
Azure Timer Function (every 15 min, weekdays 6 AM–6 PM UTC)
    │
    ├─► Jira REST API        →  fetch tickets by status + changelog
    ├─► Azure Blob Storage   →  load / save state.json
    ├─► Power Automate Webhook (Ready For QA)  →  Teams card per new ticket
    ├─► Power Automate Webhook (Verified)      →  Teams card per new ticket
    └─► Power Automate Webhook (Closed)        →  Teams card per new ticket
```

---

## Card Previews

**🔔 Ready For QA** — `accent` blue background, white text
```
┌──────────────────────────────────────────────┐
│  🔔  READY FOR QA                            │  ← blue header
│  Awaiting your testing! 👀                   │
├──────────────────────────────────────────────┤
│  PROJECT-123                                 │
│  Short ticket summary here                   │
├──────────────────────────────────────────────┤
│  👤 Assignee              @John Smith        │  ← real Teams mention
├──────────────────────────────────────────────┤
│  📊 Time in previous statuses                │
│  In Progress              3d 2h              │
│  Code Review              1d 4h              │
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

**✅ Verified** — `good` green background, dark text
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
│  🔀 Please merge all related PRs             │
│     before closing this ticket               │
├──────────────────────────────────────────────┤
│  [ Open in Jira → ]                          │
└──────────────────────────────────────────────┘
```

**🎉 Closed** — `warning` gold background, dark text
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
       "Webhook__ReadyForQaUrl": "https://your-power-automate-webhook-url",
       "Webhook__VerifiedUrl": "https://your-power-automate-webhook-url",
       "Webhook__ClosedUrl": "https://your-power-automate-webhook-url",
       "State__ContainerName": "jira-qa-monitor",
       "State__BlobName": "state.json"
     }
   }
   ```

3. **Start Azurite** (local blob emulator):
   ```bash
   npx azurite --silent --location .azurite
   ```

4. **Run the function**:
   ```bash
   dotnet build
   func start
   ```

5. **Trigger manually** (without waiting 15 min):
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
| `Webhook__ReadyForQaUrl` | Power Automate webhook for Ready For QA channel |
| `Webhook__VerifiedUrl` | Power Automate webhook for Verified channel |
| `Webhook__ClosedUrl` | Power Automate webhook for Closed channel |
| `State__BlobConnectionString` | connection string of your storage account |
| `State__ContainerName` | `jira-qa-monitor` |
| `State__BlobName` | `state.json` |

### 4. Build & Deploy
```bash
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . && cd ..
```

Then in the Azure Portal go to **Function App → Development Tools → Advanced Tools → Kudu → Tools → Zip Push Deploy** and drag & drop `deploy.zip`.

> **Note:** Kudu is available on Consumption and Premium plans. It is not available on Flex Consumption.

### 5. Verify
Go to **Function App → Functions → QaMonitorTimer → Monitor** to see invocation logs.

---

## Configuration Reference

| Setting | Description | Default |
|---------|-------------|---------|
| `Jira__BaseUrl` | Jira instance root URL | — |
| `Jira__Project` | Jira project key | — |
| `Jira__Username` | Jira account email | — |
| `Jira__ApiToken` | Jira API token | — |
| `Webhook__ReadyForQaUrl` | Teams webhook for Ready For QA notifications | — |
| `Webhook__VerifiedUrl` | Teams webhook for Verified notifications | — |
| `Webhook__ClosedUrl` | Teams webhook for Closed notifications | — |
| `State__BlobConnectionString` | Azure Storage connection string | — |
| `State__ContainerName` | Blob container name | `jira-qa-monitor` |
| `State__BlobName` | State file name | `state.json` |

---

## Customization

All monitoring rules live in `Services/JiraService.cs` and can be freely adjusted to match your team's workflow.

**Tracked issue types** — by default: `Bug`, `Improvement`, `Story`, `Spike`:
```csharp
AND issuetype in (Bug, Improvement, Story, Spike)
```

**Monitored statuses** — the three tracked statuses map directly to your Jira workflow names. If your board uses different status names (e.g. `"In Review"` instead of `"Ready For QA"`, or `"Done"` instead of `"Closed"`), just update the strings passed to `QueryTicketsAsync`:
```csharp
public Task<List<JiraTicket>> GetReadyForQaTicketsAsync() => QueryTicketsAsync("Ready For QA", ...);
public Task<List<JiraTicket>> GetVerifiedTicketsAsync()   => QueryTicketsAsync("Verified", ...);
public Task<List<JiraTicket>> GetClosedTicketsAsync()     => QueryTicketsAsync("Closed", ...);
```

**Sprint filter** — only active sprint tickets are tracked by default:
```csharp
AND sprint in openSprints()
```
Remove this clause if you want to track tickets across all sprints.

---

## Schedule

Runs every **15 minutes on weekdays between 6 AM and 6 PM UTC**.

To change the schedule, update the cron expression in `Functions/QaMonitorTimer.cs`:
```csharp
[TimerTrigger("0 */15 6-18 * * 1-5")]
```

---

## State File

Stored in Azure Blob Storage at `{container}/state.json`:
```json
{
  "readyForQaIds": ["PROJECT-123", "PROJECT-456"],
  "verifiedIds":   ["PROJECT-100", "PROJECT-101"],
  "closedIds":     ["PROJECT-090", "PROJECT-091"]
}
```

- `readyForQaIds` and `verifiedIds` are replaced on each run with the current active set — if a ticket leaves and returns to a status it will be notified again.
- `closedIds` only ever grows — closed tickets are never re-notified.

---

## Security Recommendations

- Store `Jira__ApiToken` and webhook URLs in **Azure Key Vault** and reference via `@Microsoft.KeyVault(...)` app setting syntax
- Enable **Managed Identity** on the Function App for Key Vault access
- Never commit `local.settings.json` to source control — it is already in `.gitignore`

---

## License

MIT
