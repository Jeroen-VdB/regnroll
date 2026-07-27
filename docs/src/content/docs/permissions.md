---
title: Permissions & ownership
description: How Regnroll's managed identity gets access to app registrations — least privilege by default.
---

Regnroll talks to Microsoft Graph exclusively with the function app's **system-assigned managed identity**. It never stores a credential for its own access.

## Owner mode (default, least privilege)

The default Graph permission is **`Application.ReadWrite.OwnedBy`**:

- the identity can create, read and update credentials **only on app registrations it owns**;
- it discovers manageable apps by listing its own owned objects;
- it *cannot* touch anything else in the tenant, and it cannot add owners (not even itself).

Grant it once (tenant admin):

```shell
pwsh ./infra/scripts/grant-graph-permissions.ps1
```

Then, **per app registration** you want Regnroll to manage, add the identity as owner:

```shell
az ad app owner add \
  --id <app registration OBJECT id> \
  --owner-object-id <managed identity principal id>   # printed by azd up / the grant script
```

Ownership can also live in your IaC. If your app registrations are declared with the Bicep Graph extension, include the principal id in the application's owners — then registering a new app for Regnroll is part of the same pull request that creates it.

:::note
Because `Application.ReadWrite.OwnedBy` cannot add owners, this step is inherently out-of-band — it is the deliberate consent boundary of owner mode. Regnroll surfaces a clear, actionable error in the admin portal when it hits an app it does not own.
:::

## Tenant-wide mode (opt-in)

Organizations that do not want to manage ownership can instead grant **`Application.ReadWrite.All`**:

```shell
pwsh ./infra/scripts/grant-graph-permissions.ps1 -TenantWide
az functionapp config appsettings set -g <rg> -n <app> --settings Regnroll__GraphMode=All
```

The portal then lists **every** app registration in the tenant as manageable.

:::caution
`Application.ReadWrite.All` lets the identity modify credentials of *any* application — including privileged ones — which effectively makes the function app tenant-admin-adjacent. Prefer owner mode unless you have a strong reason and compensating controls.
:::

## What Regnroll does with the permission

| Operation | Graph call |
| --- | --- |
| Discover manageable apps | `GET /servicePrincipals/{id}/ownedObjects` (owner mode) / `GET /applications` (tenant-wide) |
| Create a client secret | `POST /applications/{id}/addPassword` — the secret text is returned exactly once and is immediately encrypted |
| Remove an **expired** secret | `POST /applications/{id}/removePassword` |
| Add an uploaded certificate | `PATCH /applications/{id}` with the full `keyCredentials` array (append; `addKey` requires proof-of-possession and is unusable app-only) |
| Remove **expired** certificates | `PATCH /applications/{id}` with expired entries filtered out |

Regnroll never deletes an app registration, never removes a still-valid credential, and never changes anything other than `passwordCredentials`/`keyCredentials`.

## EasyAuth (who can open the admin portal)

Admin access is a separate concern: App Service built-in authentication, wired by the postprovision hook, requires an Entra sign-in from your tenant for every route except the public customer pages. Additionally, the app itself rejects any admin API call that arrives without a platform-injected `X-MS-CLIENT-PRINCIPAL` header — so the admin surface fails closed even if platform auth were accidentally disabled. In v1 any signed-in tenant user is an admin; app-role-based authorization is a planned enhancement.
