# Tasks — Bootstrap Regnroll

## 1. Solution scaffold

- [x] 1.1 Create `Regnroll.slnx` with `src/Regnroll.App` (net10.0, Functions v4 isolated worker, ASP.NET Core integration, `Functions.Worker.Extensions.HttpApi`, Timer extension, `host.json` with `"routePrefix": ""`) and update `.gitignore` for .NET artifacts
- [x] 1.2 Add `tests/Regnroll.App.Tests` (xUnit) with a placeholder test; verify `dotnet build` and `dotnet test` pass
- [x] 1.3 Implement `Options/RegnrollOptions.cs` bound from the `Regnroll` config section with DataAnnotations + `ValidateOnStart`: `RotateBeforeDays=30`, `WarnBeforeDays=7`, `SecretValidityDays=180`, `LinkTtlDays=14`, `GraphMode=OwnedBy`, `TimerSchedule`, `PublicBaseUrl`, `AcsEndpoint`, `SenderAddress`, `DataTablesEndpoint`, `ManagedIdentityPrincipalId`
- [x] 1.4 Write `Program.cs`: `FunctionsApplication.CreateBuilder` + `ConfigureFunctionsWebApplication()` + `AddHttpApi()`, DI registrations (`DefaultAzureCredential`, `TableServiceClient`, `EmailClient`, `GraphServiceClient`, domain services), table `CreateIfNotExists` at startup

## 2. Crypto and storage core

- [x] 2.1 Implement `CryptoService` (AES-256-GCM encrypt/decrypt, 128-bit link id + 256-bit key generation base64url, SHA-256 row-key derivation) with unit tests covering round-trip, wrong-key tag failure, and entropy/length
- [x] 2.2 Implement `MetadataStore` for the `appregs` table (link/unlink, contact emails, rotate/warn overrides, effective-settings resolution) with tests
- [x] 2.3 Implement `LinkStore` for the `links` table (create secret link with ciphertext, create upload token, status transitions Pending→Claimed/Uploaded, `WarnedAt`, ETag-conditional delete for atomic claim, expired-row purge, expired-reads-as-gone) with tests against Azurite

## 3. Graph integration

- [x] 3.1 Implement `GraphService` discovery: owned apps via `servicePrincipals/{principalId}/ownedObjects` (OwnedBy mode) and all apps via `/applications` paged (All mode), selected by `GraphMode`
- [x] 3.2 Implement secret operations: `addPassword` (displayName `regnroll-<date>`, endDateTime from `SecretValidityDays`, one-time `secretText` capture) and `removePassword`
- [x] 3.3 Implement certificate operations: read `keyCredentials` → append/filter → `PATCH` full array, with one read-modify-write retry on conflict
- [x] 3.4 Map Graph authorization/consent failures to actionable admin errors (missing permission, missing ownership → remediation text + docs link) with tests using a mocked request adapter (implemented as a pure `GraphErrorMapper` tested directly — same coverage without Kiota mocking)

## 4. Email notifications

- [x] 4.1 Embed the 4 default templates (`new-secret`, `new-certificate`, `warning`, `expired`) and implement `TemplateService` with `{variable}` substitution (all six documented variables; unknown placeholders left verbatim) with tests
- [x] 4.2 Implement template overrides via the `templates` table (get/save/reset) applied without redeploy
- [x] 4.3 Implement `EmailService` over `Azure.Communication.Email` using `DefaultAzureCredential`, configurable sender, multi-recipient support

## 5. Delivery flows and public endpoints

- [x] 5.1 Implement `DeliveryService` new-secret flow: Graph addPassword → encrypt + store link row → send email, with compensating cleanup (`removePassword` + row delete) when the email send fails; tests for happy path and compensation
- [x] 5.2 Implement new-certificate flow: upload token row → send upload email
- [x] 5.3 Implement `POST /api/claim` (anonymous): id+key validation, GCM verify, atomic ETag delete, plaintext JSON exactly once; wrong key keeps the row; claimed/expired → "gone" response; tests incl. concurrent-claim race
- [x] 5.4 Implement `POST /api/upload` (anonymous): token check, X.509 validation (parseable, not expired/not-yet-valid, reject private key/PFX material), Graph keyCredentials append, consume link, return thumbprint; tests for all rejection cases
- [x] 5.5 Build public pages `wwwroot/s.html` and `wwwroot/c.html`: fragment-key handling, explicit "Reveal secret" POST, upload form, gone/expired states, and the curl automation recipe shown on the page

## 6. Admin portal

- [x] 6.1 Implement `StaticPage` catch-all HttpTrigger serving `wwwroot` via `LocalStaticApp()` plus the fail-closed principal check helper (`X-MS-CLIENT-PRINCIPAL` required for admin surface, 401 otherwise) (implemented with explicit page routes + own `StaticFiles`/`AdminAuth` helpers instead of the HttpApi package — see design deviation note)
- [x] 6.2 Implement `GET /api/admin/apps`: manageable app registrations with linked state, credential expiry overview, and pending-link status (no secret material)
- [x] 6.3 Implement link/unlink endpoints: link with contact emails + optional immediate new-secret/new-certificate trigger; unlink removes metadata and pending links only
- [x] 6.4 Implement per-app override endpoints (set/clear rotate-before, warn-before, contacts)
- [x] 6.5 Implement manual trigger endpoints for new-secret and new-certificate, superseding (invalidating) any pending link of the same type
- [x] 6.6 Implement template admin endpoints and build `wwwroot/index.html` admin UI (app list, link/unlink, overrides, triggers, link status, template editor) in vanilla JS

## 7. Lifecycle timer

- [x] 7.1 Add `DailyLifecycleScan` timer function (schedule from config, default daily 06:00 UTC) delegating to `LifecycleService` iterating all linked apps with effective settings
- [x] 7.2 Implement rotate-before evaluation for secrets and certificates (latest-expiring credential per type, skip when a Pending link exists) with idempotency tests
- [x] 7.3 Implement warn-before evaluation (Pending link + old credential inside warn window + not yet warned → warning email + `WarnedAt`) with once-only tests
- [x] 7.4 Implement expired cleanup: remove only already-expired credentials via Graph, send expired email, purge expired link rows; tests proving valid credentials are never deleted

## 8. Infrastructure (azd)

- [x] 8.1 Create `azure.yaml` and `infra/main.bicep` skeleton: FC1 plan, function app with `functionAppConfig` (runtime `dotnet-isolated|10.0`, `deployment.storage` blob container with managed-identity auth), host storage account, Application Insights (subscription-scope `main.bicep` + `resources.bicep`; validated with `az bicep build`)
- [x] 8.2 Add the dedicated data storage account (shared-key disabled) with `appregs`/`links`/`templates` tables and RBAC assignments (`Storage Table Data Contributor` on data account, blob roles on host account for deployment)
- [x] 8.3 Add ACS resources (`communicationServices` + `emailServices` + `AzureManagedDomain`, linked) with email-send role assignment for the MI, and all `Regnroll__*` app settings (30d/7d defaults, azd-parameterized)
- [x] 8.4 Add `authsettingsV2` bicep config: `requireAuthentication`, `RedirectToLoginPage`, Entra provider, `excludedPaths` for `/s/*`, `/c/*`, `/api/claim`, `/api/upload`, shared assets (implemented in the postprovision hook instead of bicep — the EasyAuth clientId only exists post-provision, so the hook creates the app registration and PUTs authsettingsV2 in one idempotent step)
- [x] 8.5 Add azd postprovision hook creating/wiring the EasyAuth Entra client app, plus `infra/scripts/grant-graph-permissions.ps1` (grants `Application.ReadWrite.OwnedBy` or `.All` to the MI; prints principal id) and azd outputs
- [ ] 8.6 Run `azd up` smoke test: admin routes redirect to Entra sign-in, public pages anonymous, claim/upload endpoints reachable; verify `excludedPaths` wildcard behavior and fall back to AllowAnonymous + code-enforcement if needed

## 9. Documentation site

- [x] 9.1 Scaffold Astro Starlight in `docs/` (coexisting with `docs/regnroll.drawio` and `docs/diagrams/`), `site: 'https://regnroll.github.io'` with configurable `base` (diagram PNGs relocated to `docs/src/assets/diagrams/` so Astro can optimize them; `DOCS_BASE` env overrides the base path for forks)
- [x] 9.2 Write content: introduction & architecture (embedding the exported PNGs), deployment guide (azd + hooks + grant script), Graph permission & ownership setup, admin guide, customer guide (retrieve secret, upload certificate, curl automation recipe), email template customization, configuration reference, security model (encrypted-only storage, one-time links, no Key Vault rationale)
- [x] 9.3 Add `.github/workflows/docs.yml` building and deploying the Starlight site to GitHub Pages via `withastro/action` on pushes to `main`; verify `npm run build` locally (local build: 10 pages, all diagrams optimized; Pages deploy activates once the repo is on GitHub with Pages enabled)

## 10. CI and final verification

- [x] 10.1 Add `.github/workflows/ci.yml`: `dotnet build` + `dotnet test` (and docs build check) on push/PR (Azurite started as a CI step so the storage integration tests run rather than skip)
- [x] 10.2 Update `README.md`: product summary, deploy quickstart (azd), link to regnroll.github.io docs
- [ ] 10.3 Walk every spec scenario against the deployed instance (scanner-safe GET, one-time claim + second-claim gone, wrong-key retention, upload validation, override behavior, warn-once, expired cleanup) and record results in the change
