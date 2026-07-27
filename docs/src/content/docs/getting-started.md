---
title: Getting started
description: Deploy Regnroll with the Azure Developer CLI in minutes.
---

Regnroll deploys with the [Azure Developer CLI](https://aka.ms/azd) — one command provisions everything and pushes the code.

## Prerequisites

- **Azure Developer CLI** (`azd`) and **Azure CLI** (`az`), signed in to the right subscription and tenant.
- Rights to create an Entra ID app registration (used by the sign-in flow of the admin portal).
- A **tenant admin** for one single post-deploy step: granting the managed identity its Microsoft Graph permission.

## Deploy

```shell
azd init   # first time only: pick an environment name and region
azd up
```

`azd up` provisions, per environment:

| Resource | Purpose |
| --- | --- |
| Function app (Flex Consumption, FC1, .NET 10 isolated) | Hosts the admin portal, public pages, APIs and the daily timer |
| Host storage account | Functions runtime + Flex deployment container |
| **Data storage account** (separate) | `appregs`, `links`, `templates` tables — Regnroll's only state |
| Azure Communication Services + Email service + managed domain | Outbound notification emails (`DoNotReply@…azurecomm.net`) |
| Application Insights + Log Analytics | Telemetry |

A **postprovision hook** then automatically:

1. writes the managed identity's principal id into the app settings,
2. creates (or reuses) the Entra app registration for App Service built-in authentication (EasyAuth),
3. applies `authsettingsV2`: every route requires Entra sign-in **except** the public customer surface (`/s/*`, `/c/*`, `/api/claim`, `/api/upload`, `/assets/*`, `/api/health`).

## Grant the Graph permission (tenant admin, once)

Creating secrets on app registrations is an app-only Graph permission, which only a tenant admin can consent to — that is deliberately not part of the template:

```shell
pwsh ./infra/scripts/grant-graph-permissions.ps1
```

This grants **`Application.ReadWrite.OwnedBy`** (least privilege — the identity can only manage app registrations it *owns*). See [Permissions & ownership](/permissions/) for the ownership step and the tenant-wide alternative.

## Link your first app registration

1. Make the managed identity an owner of the app registration (see [Permissions & ownership](/permissions/)).
2. Open the URL printed by `azd up` and sign in.
3. The app registration appears in the list — press **Link…**, enter the customer contact email, optionally tick *create secret now*.

Done. The daily scan (default `0 0 6 * * *` UTC) takes it from here.

## Verify the deployment

```shell
curl -s https://<your-app>.azurewebsites.net/api/health          # → {"status":"ok"} (anonymous)
curl -sI https://<your-app>.azurewebsites.net/                   # → 302 redirect to Entra sign-in
```

## Local development

```shell
azurite --silent &
cp src/Regnroll.App/local.settings.sample.json src/Regnroll.App/local.settings.json
func start --project src/Regnroll.App
dotnet test
```

Outside Azure the admin surface is open (no EasyAuth locally); Graph and email calls use your `az login` credentials via `DefaultAzureCredential`.
