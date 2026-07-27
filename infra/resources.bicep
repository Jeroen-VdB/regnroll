@description('Location for all resources.')
param location string

param tags object

param acsDataLocation string
param rotateBeforeDays int
param warnBeforeDays int
param secretValidityDays int
param linkTtlDays int
param graphMode string
param timerSchedule string

var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id))
var functionAppName = 'func-regnroll-${resourceToken}'
var deploymentContainerName = 'app-package-regnroll'

// ---------------------------------------------------------------------------
// Monitoring
// ---------------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------
// Storage: host account (Functions runtime + Flex deployment container)
// ---------------------------------------------------------------------------

resource hostStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource hostBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: hostStorage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: hostBlobService
  name: deploymentContainerName
}

// ---------------------------------------------------------------------------
// Storage: dedicated data account (Regnroll tables — separate from hosting)
// ---------------------------------------------------------------------------

resource dataStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'std${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource dataTableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: dataStorage
  name: 'default'
}

resource tableAppRegs 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: dataTableService
  name: 'appregs'
}

resource tableLinks 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: dataTableService
  name: 'links'
}

resource tableTemplates 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: dataTableService
  name: 'templates'
}

// ---------------------------------------------------------------------------
// Azure Communication Services email (managed domain, DoNotReply sender)
// ---------------------------------------------------------------------------

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'email-${resourceToken}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: acsDataLocation
  }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-${resourceToken}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: acsDataLocation
    linkedDomains: [emailDomain.id]
  }
}

var senderAddress = 'DoNotReply@${emailDomain.properties.fromSenderDomain}'

// ---------------------------------------------------------------------------
// Flex Consumption function app
// ---------------------------------------------------------------------------

resource flexPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${resourceToken}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'app' })
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: flexPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${hostStorage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: hostStorage.name }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'REGNROLL_TIMER_SCHEDULE', value: timerSchedule }
        { name: 'Regnroll__PublicBaseUrl', value: 'https://${functionAppName}.azurewebsites.net' }
        { name: 'Regnroll__DataTablesEndpoint', value: dataStorage.properties.primaryEndpoints.table }
        { name: 'Regnroll__AcsEndpoint', value: 'https://${communicationService.properties.hostName}' }
        { name: 'Regnroll__SenderAddress', value: senderAddress }
        { name: 'Regnroll__RotateBeforeDays', value: string(rotateBeforeDays) }
        { name: 'Regnroll__WarnBeforeDays', value: string(warnBeforeDays) }
        { name: 'Regnroll__SecretValidityDays', value: string(secretValidityDays) }
        { name: 'Regnroll__LinkTtlDays', value: string(linkTtlDays) }
        { name: 'Regnroll__GraphMode', value: graphMode }
        { name: 'Regnroll__TenantId', value: tenant().tenantId }
        // Regnroll__ManagedIdentityPrincipalId is set by the postprovision hook —
        // it cannot reference the app's own identity from within this resource.
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// RBAC for the function's managed identity
// ---------------------------------------------------------------------------

var roleStorageBlobDataOwner = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
var roleStorageQueueDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
var roleStorageTableDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
var roleContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')

resource miBlobOnHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(hostStorage.id, functionApp.id, roleStorageBlobDataOwner)
  scope: hostStorage
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: roleStorageBlobDataOwner
    principalType: 'ServicePrincipal'
  }
}

resource miQueueOnHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(hostStorage.id, functionApp.id, roleStorageQueueDataContributor)
  scope: hostStorage
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: roleStorageQueueDataContributor
    principalType: 'ServicePrincipal'
  }
}

resource miTableOnHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(hostStorage.id, functionApp.id, roleStorageTableDataContributor)
  scope: hostStorage
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: roleStorageTableDataContributor
    principalType: 'ServicePrincipal'
  }
}

resource miTableOnData 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataStorage.id, functionApp.id, roleStorageTableDataContributor)
  scope: dataStorage
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: roleStorageTableDataContributor
    principalType: 'ServicePrincipal'
  }
}

// Contributor on the ACS resource; if identity-based email send is rejected in your
// tenant, set Regnroll__AcsConnectionString instead (documented fallback).
resource miOnAcs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, functionApp.id, roleContributor)
  scope: communicationService
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: roleContributor
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------

output functionAppName string = functionApp.name
output functionAppUri string = 'https://${functionApp.properties.defaultHostName}'
output managedIdentityPrincipalId string = functionApp.identity.principalId
output acsEndpoint string = 'https://${communicationService.properties.hostName}'
output senderAddress string = senderAddress
output dataStorageAccountName string = dataStorage.name
