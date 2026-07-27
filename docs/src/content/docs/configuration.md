---
title: Configuration reference
description: Every Regnroll environment variable and its default.
---

All product settings live in the `Regnroll` configuration section (`Regnroll__*` app settings). The deployment sets sensible values; everything is overridable per environment (azd parameters or app settings).

## Rotation behavior

| Setting | Default | Meaning |
| --- | --- | --- |
| `Regnroll__RotateBeforeDays` | `30` | Create/request the replacement credential this many days before the old one expires |
| `Regnroll__WarnBeforeDays` | `7` | Remind about unused links this many days before the old credential expires |
| `Regnroll__SecretValidityDays` | `180` | Validity of client secrets created by Regnroll |
| `Regnroll__LinkTtlDays` | `14` | Maximum link lifetime; when rotating, links additionally never outlive the old credential |
| `REGNROLL_TIMER_SCHEDULE` | `0 0 6 * * *` | NCRONTAB schedule of the daily scan (flat name — consumed by the trigger binding) |

Per-app overrides of rotate-before and warn-before are set in the admin portal and win over the environment defaults.

## Platform wiring

| Setting | Set by | Meaning |
| --- | --- | --- |
| `Regnroll__PublicBaseUrl` | bicep | Base URL used in generated links |
| `Regnroll__DataTablesEndpoint` | bicep | Table endpoint of the data storage account (managed identity auth) |
| `Regnroll__DataTablesConnectionString` | you (local dev) | Alternative for Azurite: `UseDevelopmentStorage=true` |
| `Regnroll__AcsEndpoint` | bicep | Azure Communication Services endpoint (managed identity auth) |
| `Regnroll__AcsConnectionString` | you (optional) | Fallback if identity-based email send is unavailable in your tenant |
| `Regnroll__SenderAddress` | bicep | Defaults to the managed-domain `DoNotReply@…azurecomm.net` address |
| `Regnroll__TenantId` | bicep | Renders the `{token_endpoint}` template variable |
| `Regnroll__ManagedIdentityPrincipalId` | postprovision hook | Object id of the identity's service principal (needed for owned-apps discovery) |

## Graph mode

| Setting | Default | Meaning |
| --- | --- | --- |
| `Regnroll__GraphMode` | `OwnedBy` | `OwnedBy` = manage only owned app registrations (least privilege). `All` = tenant-wide, requires `Application.ReadWrite.All` |

See [Permissions & ownership](/permissions/).

## Validation

Options are validated at startup (`ValidateOnStart`): a missing `PublicBaseUrl`, an invalid `GraphMode`, out-of-range day values, or missing table configuration fail fast with a clear message instead of misbehaving at 6 AM.
