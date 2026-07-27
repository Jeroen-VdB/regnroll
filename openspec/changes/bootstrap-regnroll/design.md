# Design — Bootstrap Regnroll

## Context

Regnroll automates Entra ID app registration secret/certificate lifecycle: rotate early, deliver via one-time encrypted links, warn stragglers, clean up expired credentials. The architecture template is **acmebot** (`polymind-inc/acmebot` v5: .NET 10 isolated worker on Flex Consumption, minimal UI served from the function app itself, EasyAuth, one-class-per-endpoint HTTP API, IOptions config). The functional template for delivery is **yopass** (encrypted-only storage, ~128-bit link ids, one-time delete-before-serve, key kept in the URL fragment).

Regnroll's threat model differs from yopass in one key way: the **server generates the secret** (via Graph `addPassword`), so it inevitably knows the plaintext transiently. The goal of the delivery design is therefore not zero-knowledge, but: (1) nothing recoverable at rest, (2) links that email-security scanners cannot burn, (3) zero-effort automation for the receiving customer.

Verified platform facts this design relies on (Microsoft Learn, checked 2026-07):
- Flex Consumption supports all triggers (timer included), .NET 8/9/10 isolated only, no deployment slots, Linux only; azd is the documented deployment path; code deploys through a blob "deployment container" + OneDeploy (`functionAppConfig.deployment.storage` in bicep).
- EasyAuth works on Flex; `authsettingsV2.globalValidation` supports `unauthenticatedClientAction` + `excludedPaths` (ARM/bicep or file-based config only — not exposed in the portal UI). Code reads the user from the platform-injected `X-MS-CLIENT-PRINCIPAL` header.
- `Application.ReadWrite.OwnedBy` allows `addPassword`/`removePassword`/`PATCH keyCredentials` **only on owned apps**, allows listing, and **cannot add owners** (that needs `Application.ReadWrite.All`). Owned apps are discovered via `GET /servicePrincipals/{id}/ownedObjects`. `addKey` requires proof-of-possession signed with an existing certificate → unusable app-only for first certs; `PATCH` of the full `keyCredentials` array is the supported alternative. `addPassword` returns `secretText` exactly once.
- ACS Email (`Azure.Communication.Email`) and Table Storage (`Azure.Data.Tables`) both work with `DefaultAzureCredential`/managed identity; tables need the `Storage Table Data Contributor` role; ACS managed domains only allow the `DoNotReply@<guid>.azurecomm.net` sender.

## Goals / Non-Goals

**Goals:**
- One cheap, azd-deployable Azure Function app hosting: admin portal (EasyAuth), public retrieval page, public certificate upload page, JSON APIs, daily lifecycle timer.
- Encrypted-only at-rest delivery payloads in a dedicated data storage account; no Key Vault.
- Least-privilege Graph by default (owner mode), tenant-wide as explicit opt-in.
- Scanner-proof one-time links that a customer can also consume from a script with zero setup by the operator.
- Configurable email templates (4 flows) over ACS.
- Starlight docs on GitHub Pages.

**Non-Goals:**
- Creating/deleting app registrations (customers' IaC does that; see concept diagram).
- Zero-knowledge encryption (server generates the secrets; see Context).
- Multi-tenant SaaS, RBAC roles inside the admin portal (any authenticated tenant user is an admin in v1; acmebot-style app roles are a future enhancement).
- Key Vault integration, split-URL delivery (see Decision 5), SLA-grade mail volume (ACS managed domain limits are fine for credential mail).
- Durable Functions orchestration (see Decision 2).

## Decisions

### 1. Runtime and project shape: .NET 10 isolated worker, acmebot layout
`src/Regnroll.App/` (Functions v4, `net10.0`, isolated worker, ASP.NET Core integration via `ConfigureFunctionsWebApplication()`), `tests/Regnroll.App.Tests/`, `infra/` + `azure.yaml`, `docs/` (Starlight), `.github/workflows/`. HTTP endpoints one-class-per-endpoint under `Functions/Http/`, timer under `Functions/Timer/`, domain logic in `Services/` (GraphService, DeliveryService, CryptoService, TemplateService, EmailService, MetadataStore, LifecycleService), config in `Options/RegnrollOptions.cs` bound from the `Regnroll` section (`Regnroll__*` app settings) with DataAnnotations + `ValidateOnStart` — all acmebot patterns. *(Implementation note: instead of taking the `Functions.Worker.Extensions.HttpApi` package as a dependency, the same two mechanisms are implemented directly — `AdminAuth` parses `X-MS-CLIENT-PRINCIPAL` into a `ClaimsPrincipal`, `StaticFiles` serves `wwwroot` — ~80 lines, no third-party API surface to track.)*
*Alternative considered:* .NET 8 LTS — rejected: support ends 2026-11; acmebot v5 is already on net10.

### 2. No Durable Functions — a plain daily timer with idempotent scans
acmebot needs Durable (multi-step ACME polling with waits). Regnroll's flows are single Graph calls + a table write + one email; the daily timer re-derives all state from Graph + the metadata tables, and idempotency comes from "a pending link exists" checks. Simpler to test, no orchestration state to manage. Durable is supported on Flex if ever needed later.
*Alternative:* Durable eternal orchestrations per app (acmebot's scheduler pattern) — rejected as overkill.

### 3. UI: hand-written static files in `wwwroot`, served by explicit-route HttpTriggers
`host.json` sets `"http": { "routePrefix": "" }`; explicit anonymous routes (`/`, `s/{id}`, `c/{token}`, `assets/{*path}`) serve files from `wwwroot/` (a catch-all `{*path}` acmebot-style was considered, but explicit routes make the public/authenticated split and the EasyAuth `excludedPaths` list exact). Pages: `index.html` (admin portal, vanilla JS + `fetch` against `/api/admin/*`), `s.html` (secret retrieval), `c.html` (certificate upload), shared CSS. **No frontend build step** (no npm in the app build) — the admin UI is a few tables and forms; the public pages must be tiny anyway.
*Alternative:* Vue 3 + Vite like acmebot — rejected for v1: adds a CI build stage and toolchain for little gain at this UI size; the serving architecture (which is what we're copying) is identical either way.

### 4. Delivery crypto: AES-256-GCM, key in URL fragment, POST-only claim, server-side decrypt
- On secret creation: generate 128-bit link id + 256-bit key (CSPRNG, base64url). Encrypt the secret with AES-256-GCM. Store **only** `SHA-256(id)` as RowKey plus ciphertext+nonce+tag — never the key, never the raw id (a storage dump can neither decrypt nor even reconstruct valid URLs).
- URL shape: `https://<host>/s/{id}#{key}`. The fragment never leaves the browser on page load; server logs never see the key.
- `GET /s/{id}` returns only the static page shell — it cannot burn, reveal, or change status. **The only consuming operation is `POST /api/claim` `{id, key}`** (the page's "Reveal secret" button posts the fragment key; scripts post the same two values parsed from the URL). Server verifies the GCM tag, returns plaintext JSON once, deletes the row in the same operation. Wrong key → tag verification fails → 4xx, row kept.
- Upload links are the same minus crypto: `https://<host>/c/{token}`, consumed by `POST /api/upload`.

Why server-side decrypt instead of yopass's client-side OpenPGP: the server knew the plaintext at generation time anyway, so client-side crypto adds no confidentiality against the server; server-side GCM verification means a wrong key can't burn the one-time payload (yopass's one-time flow loses the secret if the browser reloads after fetch), and automation becomes a single POST returning plaintext — no OpenPGP tooling on the customer side. This is strictly stronger than yopass against scanners: yopass relies on scanners not executing JS; Regnroll's GET simply has nothing to burn.

### 5. Single self-contained URL — no split links (resolves the open question)
yopass supports separated id/key links. Regnroll defaults to one full URL because: (a) the burn-risk that split links mitigate for yopass (scanner GETs) is eliminated by the POST-only claim; (b) a second channel per rotation is exactly the manual IT effort the product exists to remove; (c) automation with zero operator effort requires everything in one place. Residual risk — a compromised customer mailbox exposes the link — is mitigated by one-time semantics, link TTL, and the "claimed" status being visible to admins. A per-app "split" option (short link + manual key entry, yopass-style) is an easy later add because the key already travels in the fragment.

### 6. Data model: two-plus-one tables in a dedicated storage account
Data account (separate from the Functions host account; both `allowSharedKeyAccess: false`, RBAC only):
- **`appregs`** — PK `"appreg"`, RK = app registration **client id**; fields: object id, display name cache, contact emails (semicolon-separated), `RotateBeforeDaysOverride?`, `WarnBeforeDaysOverride?`, linked-at.
- **`links`** — PK `"link"`, RK = `SHA-256(link id)`; fields: type (`secret`|`certificate`), client id, ciphertext/nonce (secret links only), created/expires timestamps, status (`Pending`→`Claimed`|`Uploaded`), `WarnedAt?`, Graph `keyId` of the new credential, old-credential expiry.
- **`templates`** — RK = template key (`new-secret`, `new-certificate`, `warning`, `expired`); subject + HTML body overrides; absence = embedded default.

Azure Tables have no native TTL (unlike yopass's memcached/redis) → the daily timer purges expired rows, and every read treats past-expiry rows as gone (fail-closed). Tables are created with `CreateIfNotExists` at startup.
*Alternative:* blobs for payloads — rejected: payloads are <1KB, tables give point reads + conditional updates (ETag) for atomic claim.

### 7. Atomic one-time claim
Claim = read row → verify not expired/claimed → GCM-decrypt → **ETag-conditional replace that strips the ciphertext and marks the row `Claimed`** → return plaintext. If the conditional write loses a race, the loser returns "gone" (yopass's delete-before-serve semantics via Table ETags). The stripped row survives as a claim receipt so the admin portal can show Claimed/Uploaded status; it is purged with the link's expiry. The ciphertext itself is deleted in the same atomic operation, satisfying the spec.

### 8. Graph integration modes
- `Regnroll__GraphMode=OwnedBy` (default): discovery via `GET /servicePrincipals/{principalId}/ownedObjects/microsoft.graph.application`; all writes work with `Application.ReadWrite.OwnedBy` only. Making the MI an owner of each customer app reg is an out-of-band prerequisite (Graph forbids self-serve owner adds under OwnedBy) — documented, with a helper script; the concept diagram's "Bicep Graph extension" example lands in docs.
- `Regnroll__GraphMode=All` (opt-in): discovery via `GET /applications` (paged); requires `Application.ReadWrite.All`; documentation states the blast radius plainly.
- Secrets: `addPassword` (displayName `regnroll-<date>`, endDateTime = now + `SecretValidityDays`), `removePassword` for expired cleanup.
- Certificates: read application → append to `keyCredentials` → `PATCH` full array (addKey's proof-of-possession is unusable app-only). Expired-cert removal: PATCH with expired entries filtered out. Read-modify-write retried once on conflict/412.
- Failure ordering for new-secret: create secret → write link row → send email. If email send fails: delete link row **and** `removePassword` the just-created secret, surface the error — no orphaned live-but-undelivered credentials from failed flows.
- SDK: `Microsoft.Graph` v5 with `DefaultAzureCredential` (locally: az/VS credentials against a dev app reg; in Azure: MI).

### 9. AuthN/AuthZ: EasyAuth at the edge + fail-closed code checks
- `authsettingsV2` deployed by bicep: `requireAuthentication: true`, `unauthenticatedClientAction: 'RedirectToLoginPage'`, Entra provider, `excludedPaths` = public surface (`/s/*`, `/c/*`, `/api/claim`, `/api/upload`, shared static assets, `/api/health`).
- Every `/api/admin/*` function and the admin page additionally require an authenticated principal from `X-MS-CLIENT-PRINCIPAL` (acmebot's `HttpFunctionBase.User` pattern) — admin fails closed even if platform auth is off or a path exclusion is broader than intended.
- The Entra app registration EasyAuth signs in with is created by an **azd postprovision hook** (`az ad app create` + client secret + `az webapp auth microsoft update`); the portal "Add identity provider" flow is the documented manual fallback (acmebot leaves it fully manual; we automate because azd is our path).

### 10. Lifecycle engine semantics (the daily scan, default `0 0 6 * * *`)
Per linked app, per credential type, with effective settings = per-app override else env default (rotate 30d / warn 7d):
1. **Rotate**: latest-expiring credential of that type expires within rotate-before AND no `Pending` link of that type → trigger new-secret / upload-link flow. (Latest-expiring, so pre-existing customer credentials also drive rotation; after a successful rotation the new credential pushes the horizon out — naturally idempotent.)
2. **Warn**: `Pending` link, old credential expires within warn-before, `WarnedAt` empty → send warning email, stamp `WarnedAt`. Once per link.
3. **Expire**: credential past expiry → remove via Graph (`removePassword` / filtered `keyCredentials` PATCH), send expired email. Only ever deletes already-expired (cryptographically dead) credentials. Purge expired link rows.
Link TTL: `min(LinkTtlDays default 14, old credential expiry)`; links without an old credential (first secret) use the flat TTL.

### 11. Email: ACS with managed identity, template overrides in-table
`EmailClient(new Uri(acsEndpoint), DefaultAzureCredential)`; sender configurable, default = the managed domain's `DoNotReply@…azurecomm.net`. Templates: embedded defaults (resources) + `templates` table overrides, edited in the admin portal; rendering is plain `{variable}` string substitution (no Razor/Liquid dependency), unknown placeholders left verbatim. `{token_endpoint}` = `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`.

### 12. Infrastructure (azd)
`azure.yaml` (service `app` → `src/Regnroll.App`, host `function`) + `infra/main.bicep`: FC1 plan; function app `functionAppConfig` with runtime `dotnet-isolated|10.0` and `deployment.storage` (blob container, managed-identity auth); host storage account; **data storage account** with the three tables; ACS trio (`communicationServices` + `emailServices` + `AzureManagedDomain`, linked); Application Insights; system-assigned MI; role assignments (blob on host account for deploy, `Storage Table Data Contributor` on data account, ACS email-send role); `authsettingsV2`; app settings for all `Regnroll__*` defaults incl. `RotateBeforeDays=30`, `WarnBeforeDays=7`. Outputs: function URL, MI principal id. `infra/scripts/grant-graph-permissions.ps1` grants `Application.ReadWrite.OwnedBy` (or `.All`) to the MI via Graph appRoleAssignments — separate because it needs tenant-admin consent, not subscription rights.
*Alternative:* acmebot's deploy-button ARM + release-zip OneDeploy — rejected: user asked for azd; deploy-button can be added later from the same bicep.

### 13. Documentation site
Starlight project in `docs/` (`npm create astro -- --template starlight` layout), `site: 'https://regnroll.github.io'` with `base` overridable via env for project-page forks; diagrams from `docs/diagrams/` embedded in the architecture page; deploy via `withastro/action` → GitHub Pages workflow on pushes to `main`. Content per the documentation-site spec.

### 14. Observability
Standard Functions Application Insights integration (worker defaults). acmebot's OpenTelemetry stack is nice-to-have, not v1. Structured logging via `ILogger` source-generated messages; never log ids, keys, or secret material — log the SHA-256 row key when correlation is needed.

## Risks / Trade-offs

- [EasyAuth on Flex has no explicit "supported" doc statement; `excludedPaths` wildcard syntax is under-documented] → smoke-test in the first deployment task; admin routes are safe regardless (fail-closed code checks); worst case fallback is `unauthenticatedClientAction: 'AllowAnonymous'` + code-only enforcement, which the code path already supports.
- [PATCH replaces the whole `keyCredentials` array — concurrent writers could clobber] → read-modify-write with single retry; writes are per-app and rare; document "don't edit certs in the portal at the exact moment of upload".
- [Email send failing after `addPassword` would strand a live secret] → compensating `removePassword` + link-row delete in the failure path (Decision 8).
- [Tables have no TTL — expired ciphertext lingers up to one timer period] → reads treat expired as gone (fail-closed); daily purge; payloads are ciphertext-only anyway.
- [Compromised customer mailbox can claim the secret] → inherent to email delivery; mitigated by one-time claim visibility (admin sees Claimed/not), TTL, and rotation cadence; split-link mode is a documented future option.
- [ACS managed domain has low sending limits and a fixed DoNotReply sender] → volumes here are tiny (credential mail); custom-domain setup documented for orgs that need branding.
- [Exact minimal ACS RBAC role for MI email send is thinly documented] → try `Communication and Email Service Owner` role assignment in bicep; verify during implementation; fallback documented (connection-string app setting).
- [Flex cold starts on public pages] → pages are static shells (<20KB); acceptable.
- [Undelivered new secret when a link expires unclaimed] → visible in portal as expired delivery; credential dies at its own expiry via cleanup; admin can re-trigger.

## Migration Plan

Greenfield — no migration. Rollout: `azd up` → postprovision hook wires EasyAuth → tenant admin runs `grant-graph-permissions.ps1` → org adds the MI as owner of (or grants tenant-wide, if opted) the target app regs → link apps in the portal. Rollback: `azd down` (nothing persists outside the resource group; unclaimed links die with the data account). Docs deploy is an independent GitHub Pages workflow.

## Open Questions

- Minimal ACS RBAC role for email send with MI (verify at implementation; non-blocking, fallback exists).
- `excludedPaths` wildcard behavior on Flex (verify empirically in the deployment task; non-blocking, fail-closed either way).
- Will the GitHub org/repo be `regnroll/regnroll.github.io` (org root page) or a project page? Starlight `site`/`base` are parameterized so either works; default assumes org root per the product requirement.
