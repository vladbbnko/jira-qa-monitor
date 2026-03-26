@description('Base name for all resources (e.g. jiraqamonitor)')
param appName string = 'jiraqamonitor'

@description('Azure region')
param location string = resourceGroup().location

@description('JIRA base URL')
param jiraBaseUrl string = 'https://jiraeu.epam.com'

@description('JIRA project key')
param jiraProject string = 'LHHPCOAC'

@description('JIRA username (email)')
param jiraUsername string

@description('JIRA API token — store in Key Vault in production')
@secure()
param jiraApiToken string

@description('Power Automate webhook URL')
@secure()
param webhookUrl string

// ── Names ────────────────────────────────────────────────────────────────────
var storageAccountName  = toLower(take(replace(appName, '-', ''), 24))
var functionAppName     = '${appName}-func'
var hostingPlanName     = '${appName}-plan'
var appInsightsName     = '${appName}-ai'
var logWorkspaceName    = '${appName}-logs'

// ── Log Analytics Workspace ──────────────────────────────────────────────────
resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name:     logWorkspaceName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Application Insights ─────────────────────────────────────────────────────
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name:     appInsightsName
  location: location
  kind:     'web'
  properties: {
    Application_Type:             'web'
    WorkspaceResourceId:          logWorkspace.id
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery:     'Enabled'
  }
}

// ── Storage Account ──────────────────────────────────────────────────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name:     storageAccountName
  location: location
  sku:      { name: 'Standard_LRS' }
  kind:     'StorageV2'
  properties: {
    accessTier:              'Hot'
    allowBlobPublicAccess:   false
    minimumTlsVersion:       'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// ── Blob Container for state ─────────────────────────────────────────────────
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name:   'default'
}

resource stateContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name:   'jira-qa-monitor'
  properties: {
    publicAccess: 'None'
  }
}

// ── Consumption Hosting Plan ─────────────────────────────────────────────────
resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name:     hostingPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false   // set true for Linux
  }
}

// ── Function App ─────────────────────────────────────────────────────────────
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name:     functionAppName
  location: location
  kind:     'functionapp'
  properties: {
    serverFarmId: hostingPlan.id
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage',              value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net' }
        { name: 'WEBSITE_CONTENTSHARE',             value: toLower(functionAppName) }
        { name: 'FUNCTIONS_EXTENSION_VERSION',      value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME',         value: 'dotnet-isolated' }
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY',   value: appInsights.properties.InstrumentationKey }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'Jira__BaseUrl',                    value: jiraBaseUrl }
        { name: 'Jira__Project',                    value: jiraProject }
        { name: 'Jira__Username',                   value: jiraUsername }
        { name: 'Jira__ApiToken',                   value: jiraApiToken }
        { name: 'Webhook__Url',                     value: webhookUrl }
        { name: 'State__BlobConnectionString',      value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net' }
        { name: 'State__ContainerName',             value: 'jira-qa-monitor' }
        { name: 'State__BlobName',                  value: 'state.json' }
      ]
      netFrameworkVersion: 'v8.0'
      ftpsState:           'Disabled'
      minTlsVersion:       '1.2'
    }
    httpsOnly: true
  }
}

// ── Outputs ──────────────────────────────────────────────────────────────────
output functionAppName    string = functionApp.name
output storageAccountName string = storageAccount.name
output appInsightsName    string = appInsights.name
