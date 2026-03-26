# Jira QA Monitor — Azure Function

An Azure Timer Function that monitors a Jira project for tickets changing status and sends **Microsoft Teams Adaptive Card** notifications via Power Automate webhook.

**Two notification channels out of the box:**
- 🔔 **Ready For QA** — notifies the team when a ticket is ready to be tested
- ✅ **Verified** — notifies the assignee when a ticket is verified and ready to be closed (with a PR merge reminder)

Tickets are tracked by state so each ticket is only reported **once per status**. Supports `@mention` of the assignee directly in the card.

---

## Architecture

```
Azure Timer Function (every 15 min, weekdays 6 AM–6 PM UTC)
    │
    ├─► Jira REST API        →  fetch tickets by status
    ├─► Azure Blob Storage   →  load / save state.json
    ├─► Power Automate Webhook (Ready For QA)  →  Teams card per new ticket
    └─► Power Automate Webhook (Verified)      →  Teams card per new ticket
```

---

## Card Previews

**Ready For QA**
```
┌─────────────────────────────────────────┐
│  🔔  READY FOR QA                       │  ← blue header
├─────────────────────────────────────────┤
│  PROJECT-123                            │
│  Short ticket summary here              │
├─────────────────────────────────────────┤
│  👤 Assignee              @John Smith   │  ← real Teams mention
├─────────────────────────────────────────┤
│  [ Open in Jira → ]                     │
└─────────────────────────────────────────┘
```

**Verified — Ready To Close**
```
┌─────────────────────────────────────────┐
│  ✅  VERIFIED — READY TO CLOSE          │  ← green header
├─────────────────────────────────────────┤
│  PROJECT-123                            │
│  Short ticket summary here              │
├─────────────────────────────────────────┤
│  👤 Assignee              @John Smith   │
├─────────────────────────────────────────┤
│  🔀 Please merge all related PRs        │
│     before closing this ticket          │
├─────────────────────────────────────────┤
│  [ Open in Jira → ]                     │
└─────────────────────────────────────────┘
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
- Create a **Storage Account** in your resource group
- Inside it, create a **Blob Container** named `jira-qa-monitor`

### 2. Create a Function App
- **Runtime:** .NET 8 (isolated worker)
- **OS:** Linux
- **Plan:** Consumption (pay-as-you-go)
- Link it to the storage account above

### 3. Configure Environment Variables
Go to **Function App → Settings → Environment variables** and add:

| Name | Value |
|------|-------|
| `Jira__BaseUrl` | `https://your-org.atlassian.net` |
| `Jira__Project` | your Jira project key |
| `Jira__Username` | your Jira email |
| `Jira__ApiToken` | your Jira API token |
| `Webhook__ReadyForQaUrl` | Power Automate webhook URL for Ready For QA channel |
| `Webhook__VerifiedUrl` | Power Automate webhook URL for Verified channel |
| `State__BlobConnectionString` | connection string of your storage account |
| `State__ContainerName` | `jira-qa-monitor` |
| `State__BlobName` | `state.json` |

### 4. Build & Deploy
```bash
dotnet publish -c Release -o ./publish
cd publish && zip -r ../deploy.zip . && cd ..
```

Then in the Azure Portal go to **Function App → Development Tools → Advanced Tools → Kudu → Tools → Zip Push Deploy** and drag & drop `deploy.zip`.

### 5. Verify
Go to **Function App → Functions → QaMonitorTimer → Monitor** to see invocation logs and confirm it's running.

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
| `State__BlobConnectionString` | Azure Storage connection string | — |
| `State__ContainerName` | Blob container name | `jira-qa-monitor` |
| `State__BlobName` | State file name | `state.json` |

---

## Tracked Issue Types

By default the function tracks: `Bug`, `Improvement`, `Story`, `Sub-bug`.

To change this, update the JQL in `Services/JiraService.cs`:
```csharp
AND issuetype in (Bug, Improvement, Story, Sub-bug)
```

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
  "verifiedIds":   ["PROJECT-100", "PROJECT-101"]
}
```

Each list is updated on every run. A ticket is only notified **once** — if it moves out of the status and comes back, it will be notified again.

---

## Security Recommendations

- Store `Jira__ApiToken` and webhook URLs in **Azure Key Vault** and reference via `@Microsoft.KeyVault(...)` app setting syntax
- Enable **Managed Identity** on the Function App for Key Vault access
- Never commit `local.settings.json` to source control — it is already in `.gitignore`

---

## License

MIT
