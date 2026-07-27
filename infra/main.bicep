targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment; used for resource naming and tagging.')
param environmentName string

@minLength(1)
@description('Primary location for all resources.')
param location string

@description('Data location for Azure Communication Services (e.g. Europe, United States).')
param acsDataLocation string = 'Europe'

@description('Default: create a replacement credential this many days before expiry.')
param rotateBeforeDays int = 30

@description('Default: warn about unactioned links this many days before expiry.')
param warnBeforeDays int = 7

@description('Validity in days of client secrets created by Regnroll.')
param secretValidityDays int = 180

@description('Maximum lifetime in days of delivery/upload links.')
param linkTtlDays int = 14

@description('Graph mode: OwnedBy (least privilege, default) or All (tenant-wide).')
@allowed(['OwnedBy', 'All'])
param graphMode string = 'OwnedBy'

@description('NCRONTAB schedule of the daily lifecycle scan.')
param timerSchedule string = '0 0 6 * * *'

var tags = { 'azd-env-name': environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    tags: tags
    acsDataLocation: acsDataLocation
    rotateBeforeDays: rotateBeforeDays
    warnBeforeDays: warnBeforeDays
    secretValidityDays: secretValidityDays
    linkTtlDays: linkTtlDays
    graphMode: graphMode
    timerSchedule: timerSchedule
  }
}

output AZURE_RESOURCE_GROUP string = rg.name
output SERVICE_APP_NAME string = resources.outputs.functionAppName
output SERVICE_APP_URI string = resources.outputs.functionAppUri
output REGNROLL_MI_PRINCIPAL_ID string = resources.outputs.managedIdentityPrincipalId
output REGNROLL_ACS_ENDPOINT string = resources.outputs.acsEndpoint
output REGNROLL_SENDER_ADDRESS string = resources.outputs.senderAddress
output REGNROLL_DATA_STORAGE string = resources.outputs.dataStorageAccountName
