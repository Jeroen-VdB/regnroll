<#
.SYNOPSIS
  Grants the Regnroll managed identity its app-only Microsoft Graph permission.
  Must be run by a tenant admin (Privileged Role Administrator / Global Admin —
  granting app-only Graph permissions requires admin consent rights, which is why
  this is not part of the bicep deployment).

.PARAMETER PrincipalId
  Object id of the managed identity's service principal.
  Defaults to the azd environment value REGNROLL_MI_PRINCIPAL_ID.

.PARAMETER TenantWide
  Grant Application.ReadWrite.All (tenant-wide mode) instead of the least-privilege
  default Application.ReadWrite.OwnedBy. Only use when you deliberately configured
  Regnroll__GraphMode=All.

.EXAMPLE
  pwsh ./infra/scripts/grant-graph-permissions.ps1
  pwsh ./infra/scripts/grant-graph-permissions.ps1 -TenantWide
#>
param(
    [string]$PrincipalId,
    [switch]$TenantWide
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PrincipalId)) {
    $PrincipalId = (azd env get-value REGNROLL_MI_PRINCIPAL_ID 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($PrincipalId)) {
        throw 'Pass -PrincipalId <managed identity object id> or run inside the azd environment.'
    }
    $PrincipalId = $PrincipalId.Trim()
}

# Well-known ids from the Microsoft Graph permissions reference:
#   Application.ReadWrite.OwnedBy = 18a4783c-866b-4cc7-a460-3d5e5662c884
#   Application.ReadWrite.All     = 1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9
$appRoleId = $TenantWide ? '1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9' : '18a4783c-866b-4cc7-a460-3d5e5662c884'
$roleName  = $TenantWide ? 'Application.ReadWrite.All' : 'Application.ReadWrite.OwnedBy'

Write-Host "==> Resolving the Microsoft Graph service principal"
$graphSpId = az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv

Write-Host "==> Granting $roleName to principal $PrincipalId"
$body = @{ principalId = $PrincipalId; resourceId = $graphSpId; appRoleId = $appRoleId } | ConvertTo-Json -Compress
$result = az rest --method post `
    --url "https://graph.microsoft.com/v1.0/servicePrincipals/$PrincipalId/appRoleAssignments" `
    --headers 'Content-Type=application/json' `
    --body $body 2>&1
if ($LASTEXITCODE -ne 0) {
    if ("$result" -match 'Permission being assigned already exists') {
        Write-Host '    already granted — nothing to do.'
    } else {
        throw "Granting failed: $result"
    }
} else {
    Write-Host '    granted.'
}

Write-Host ''
if ($TenantWide) {
    Write-Host 'Tenant-wide mode: also set the app setting Regnroll__GraphMode=All on the function app.'
} else {
    Write-Host 'OwnedBy mode: the identity can only manage app registrations it OWNS.'
    Write-Host 'Add it as owner per app registration (repeat per app):'
    Write-Host ''
    Write-Host "  az ad app owner add --id <app registration OBJECT id> --owner-object-id $PrincipalId"
    Write-Host ''
    Write-Host 'Or in Bicep (Graph extension), include the principal id in the app''s owners.'
}
