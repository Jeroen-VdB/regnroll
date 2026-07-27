# azure-deployment

## ADDED Requirements

### Requirement: azd-deployable infrastructure
The repository SHALL be deployable with the Azure Developer CLI: `azure.yaml` at the root plus Bicep under `infra/` provisioning, in one `azd up`: a Flex Consumption plan (FC1) and function app (.NET isolated), the host storage account with the Flex deployment blob container, the separate data storage account with the required tables, Azure Communication Services (communication service + email service + Azure managed domain, linked), Application Insights, the managed identity, and all required RBAC role assignments (blob access for deployment, `Storage Table Data Contributor` on the data account, ACS email send rights).

#### Scenario: One-command provision and deploy
- **WHEN** an operator runs `azd up` in a fresh environment
- **THEN** all resources above are created and the function app code is deployed and running

#### Scenario: Identity-based service connections
- **WHEN** the deployment completes
- **THEN** the app reaches storage tables and ACS via its managed identity, with no secret-bearing connection string for the data plane in app settings

### Requirement: No Key Vault
The infrastructure MUST NOT include or require an Azure Key Vault. One-time payloads live encrypted in the data storage account by design.

#### Scenario: Template inventory
- **WHEN** the Bicep templates are reviewed
- **THEN** no Key Vault resource is declared

### Requirement: Configuration defaults as app settings
The deployment SHALL set environment-variable defaults: rotate-before 30 days, warn-before 7 days, plus settings for secret validity, link TTL, timer schedule, sender address, Graph mode, and the public base URL. All SHALL be overridable per environment via azd parameters.

#### Scenario: Fresh deployment defaults
- **WHEN** a fresh deployment finishes without custom parameters
- **THEN** the app settings contain rotate-before=30d and warn-before=7d defaults

### Requirement: EasyAuth configured for split access
Deployment SHALL configure App Service built-in authentication (authsettingsV2) so that all routes require Entra ID sign-in except the public customer paths (retrieval page, upload page, their POST endpoints and static assets), which are excluded from authentication. The Entra app registration used for sign-in SHALL be created/wired by an azd post-provision hook, or, where that is not possible, by a single documented manual step.

#### Scenario: Admin protected, customer pages open after deploy
- **WHEN** deployment (including any documented post-provision step) completes
- **THEN** requesting the admin portal unauthenticated redirects to Entra sign-in
- **AND** requesting a retrieval or upload URL unauthenticated serves the page

### Requirement: Graph permission bootstrap is scripted and documented
Because granting app-only Graph permissions requires tenant-admin consent, the deployment SHALL output the managed identity's principal id and provide a script plus documentation to grant `Application.ReadWrite.OwnedBy` (or `Application.ReadWrite.All` for tenant-wide mode) and to add the identity as owner of app registrations.

#### Scenario: Post-deploy permission grant
- **WHEN** a tenant admin runs the provided grant script after `azd up`
- **THEN** the managed identity can list and manage its owned app registrations
