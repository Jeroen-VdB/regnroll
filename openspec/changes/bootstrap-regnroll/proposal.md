# Bootstrap Regnroll

## Why

Entra ID app registration client secrets and certificates expire silently, and rotating them is manual toil: IT support must create new credentials in the portal, deliver them to the consuming party over insecure channels (plaintext email/chat), and chase customers before expiry breaks their integrations. Existing options are either heavyweight or do not cover secure third-party delivery at all. Regnroll fills that gap: a cheap, simple, self-hostable Azure Function that automates the whole lifecycle — rotate early, deliver through one-time encrypted links, warn stragglers, clean up expired credentials.

This change bootstraps the entire product: architecture modeled on acmebot (function-hosted minimal UI, EasyAuth, IaC deploy button ergonomics), functional secret-delivery logic modeled on yopass (encrypted-only storage, one-time links).

## What Changes

- New **C# Azure Functions app** (.NET isolated worker, Flex Consumption plan) hosting a minimal admin UI + JSON API from the function app itself, protected by App Service built-in authentication (EasyAuth).
- **Admin portal actions**: link/unlink app registrations the function's managed identity can manage, per-app overrides of `rotate-before` (default 30d) and `warn-before` (default 7d) and customer contact email, manual "new secret" / "new certificate" triggers. Unlinking removes metadata only — never the app registration itself.
- **Two unauthenticated customer pages** reached via emailed secure links: a one-time secret retrieval page and a certificate upload page. Retrieval/burn happens only on an explicit POST, so email-security scanners' GET prefetches cannot consume the secret; the same POST endpoint doubles as a customer automation API.
- **Encrypted-only at-rest storage** in a dedicated data storage account (separate from the function's hosting storage account): one table for app registration metadata (linked client_id, rotate-before, warn-before, contact, link status) and one for encrypted one-time payloads whose decryption key travels only inside the emailed URL. Key Vault deliberately not used.
- **Daily timer lifecycle engine**: creates replacement secrets `rotate-before` expiry, emails certificate upload links, sends warning emails for delivery links still "not-opened" within `warn-before` of expiry, deletes expired credentials and notifies of removal.
- **Microsoft Graph via managed identity**, least-privilege by default: `Application.ReadWrite.OwnedBy` (manage only app registrations the identity owns), with an opt-in tenant-wide mode (`Application.ReadWrite.All`) for organizations that prefer not to manage ownership.
- **Azure Communication Services email** with a configurable template system: 4 templates (new-secret retrieval, new-certificate upload, warning not-opened, expired/removed) and variables `{regnroll_url}`, `{credential_type}`, `{expiry_date}`, `{client_id}`, `{client_name}`, `{token_endpoint}`.
- **azd-deployable infrastructure**: `azure.yaml` + bicep provisioning the Flex Consumption function app, both storage accounts, ACS email, Application Insights and the EasyAuth configuration.
- **Astro Starlight documentation site** under `docs/`, deployed to GitHub Pages so it is reachable at regnroll.github.io.
- drawio use-case diagrams exported to PNG under `docs/diagrams/`.

## Capabilities

### New Capabilities

- `admin-portal`: EasyAuth-protected minimal UI + API to list manageable app registrations, link/unlink them, edit per-app overrides, trigger manual secret/certificate flows, and view delivery-link status.
- `secret-rotation`: creating new client secrets via Graph (manual and automated), capturing the generated secret value exactly once, and handing it to secure delivery.
- `certificate-rotation`: requesting new certificates — emailed upload link, customer uploads the public part, the function adds it to the app registration via Graph without removing the old certificate.
- `secure-link-delivery`: yopass-style one-time links — encrypted payload storage, POST-gated claim that burns the secret, upload tokens, link expiry, "not-opened" tracking, scanner-proofing, automation-friendly API.
- `lifecycle-automation`: daily timer engine implementing rotate-before, warn-before ("not-opened" reminder), and expired-cleanup + removal-notification flows.
- `email-notifications`: Azure Communication Services email sending with the configurable 4-template system and template variables.
- `graph-integration`: managed-identity Microsoft Graph access layer with owner-scoped default mode and opt-in tenant-wide mode; listing the app registrations the identity can manage.
- `azure-deployment`: azd + bicep infrastructure — Flex Consumption plan, dual storage accounts, ACS, EasyAuth, environment-variable defaults for rotate-before/warn-before.
- `documentation-site`: Astro Starlight docs project published to GitHub Pages at regnroll.github.io via GitHub Actions.

### Modified Capabilities

None — greenfield repository with no existing specs.

## Impact

- New top-level trees: `src/` (function app), `infra/` + `azure.yaml` (azd), `docs/` (Starlight site + diagrams), `.github/workflows/` (docs deploy, CI).
- External dependencies: Microsoft Graph API, Azure Communication Services Email, Azure Table Storage, App Service EasyAuth, Azure Developer CLI. Key NuGet packages: `Microsoft.Graph`, `Azure.Data.Tables`, `Azure.Communication.Email`, `Azure.Identity`.
- Security posture: plaintext credentials are never persisted — only AES-GCM ciphertext whose key is carried in the emailed link; the admin surface requires an Entra login via EasyAuth; the managed identity is owner-scoped unless an organization explicitly opts into tenant-wide mode.
- Cost: Flex Consumption + two storage accounts + pay-per-email ACS keeps idle cost near zero, matching the "cheap and simple" product goal.
