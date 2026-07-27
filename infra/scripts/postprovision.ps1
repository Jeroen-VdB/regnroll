<#
.SYNOPSIS
  azd postprovision hook:
  1. Writes the managed identity principal id into app settings (the bicep template
     cannot reference the app's own identity from within the site resource).
  2. Creates/wires the Entra ID app registration used by App Service built-in
     authentication (EasyAuth) and applies authsettingsV2 with excludedPaths so the
     customer-facing pages stay anonymous while everything else requires sign-in.

  Requires: Azure CLI signed in with permissions to create an app registration.
  Safe to re-run (idempotent).
#>

$ErrorActionPreference = 'Stop'

function Get-AzdValue([string]$name) {
    $value = azd env get-value $name 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "azd environment value '$name' not found. Run this from 'azd up'/'azd provision' or an azd-enabled shell."
    }
    return $value.Trim()
}

$resourceGroup = Get-AzdValue 'AZURE_RESOURCE_GROUP'
$appName       = Get-AzdValue 'SERVICE_APP_NAME'
$appUri        = Get-AzdValue 'SERVICE_APP_URI'
$principalId   = Get-AzdValue 'REGNROLL_MI_PRINCIPAL_ID'
$subscription  = az account show --query id -o tsv
$tenantId      = az account show --query tenantId -o tsv

Write-Host "==> Setting Regnroll__ManagedIdentityPrincipalId on $appName"
az functionapp config appsettings set `
    --name $appName --resource-group $resourceGroup `
    --settings "Regnroll__ManagedIdentityPrincipalId=$principalId" `
    --output none

# ---------------------------------------------------------------------------
# EasyAuth app registration
# ---------------------------------------------------------------------------

$authAppName = "regnroll-easyauth-$appName"
$redirectUri = "$appUri/.auth/login/aad/callback"

Write-Host "==> Ensuring Entra app registration '$authAppName'"
$existing = az ad app list --display-name $authAppName --query '[0].appId' -o tsv
if ([string]::IsNullOrWhiteSpace($existing)) {
    $clientId = az ad app create `
        --display-name $authAppName `
        --sign-in-audience AzureADMyOrg `
        --web-redirect-uris $redirectUri `
        --enable-id-token-issuance true `
        --query appId -o tsv
    Write-Host "    created app registration $clientId"
} else {
    $clientId = $existing
    az ad app update --id $clientId --web-redirect-uris $redirectUri --enable-id-token-issuance true
    Write-Host "    reusing app registration $clientId"
}

Write-Host "==> Rotating the EasyAuth client secret"
$clientSecret = az ad app credential reset --id $clientId --display-name easyauth --years 2 --query password -o tsv

az functionapp config appsettings set `
    --name $appName --resource-group $resourceGroup `
    --settings "MICROSOFT_PROVIDER_AUTHENTICATION_SECRET=$clientSecret" `
    --output none

# ---------------------------------------------------------------------------
# authsettingsV2: require auth everywhere except the public customer surface
# ---------------------------------------------------------------------------

Write-Host "==> Applying authsettingsV2 (excludedPaths keep customer pages anonymous)"
$authSettings = @{
    properties = @{
        platform = @{ enabled = $true }
        globalValidation = @{
            requireAuthentication       = $true
            unauthenticatedClientAction = 'RedirectToLoginPage'
            redirectToProvider          = 'azureactivedirectory'
            excludedPaths               = @(
                '/s/*'
                '/c/*'
                '/api/claim'
                '/api/upload'
                '/assets/*'
                '/api/health'
                '/favicon.ico'
            )
        }
        identityProviders = @{
            azureActiveDirectory = @{
                enabled = $true
                registration = @{
                    clientId                = $clientId
                    clientSecretSettingName = 'MICROSOFT_PROVIDER_AUTHENTICATION_SECRET'
                    openIdIssuer            = "https://login.microsoftonline.com/$tenantId/v2.0"
                }
                validation = @{
                    allowedAudiences = @("api://$clientId")
                }
            }
        }
        login = @{
            tokenStore = @{ enabled = $true }
        }
    }
}

$payloadPath = Join-Path ([System.IO.Path]::GetTempPath()) "regnroll-authsettings-$([guid]::NewGuid()).json"
$authSettings | ConvertTo-Json -Depth 10 | Set-Content -Path $payloadPath -Encoding utf8
try {
    az rest --method put `
        --url "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Web/sites/$appName/config/authsettingsV2?api-version=2022-09-01" `
        --body "@$payloadPath" --output none
} finally {
    Remove-Item $payloadPath -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '=========================================================================='
Write-Host " Regnroll is provisioned: $appUri"
Write-Host ''
Write-Host ' NEXT STEP (requires a tenant admin):'
Write-Host '   grant the managed identity its Microsoft Graph permission and make it'
Write-Host '   owner of the app registrations it should manage:'
Write-Host ''
Write-Host '     pwsh ./infra/scripts/grant-graph-permissions.ps1'
Write-Host ''
Write-Host " Managed identity principal id: $principalId"
Write-Host '=========================================================================='
