# graph-integration

## ADDED Requirements

### Requirement: Managed identity Graph access
All Microsoft Graph calls SHALL authenticate with the function app's managed identity. The product MUST NOT require any client secret or certificate for its own Graph access.

#### Scenario: Token acquisition
- **WHEN** the system calls Microsoft Graph in Azure
- **THEN** the token is acquired via the managed identity with no stored credential involved

### Requirement: Owner-scoped mode is the default
By default the system SHALL operate in owner mode, designed for the app-only permission `Application.ReadWrite.OwnedBy`: it manages only app registrations the managed identity's service principal owns, and it discovers manageable app registrations by listing the service principal's owned objects. Making the identity an owner of a customer app registration is an out-of-band prerequisite performed by the organization (Regnroll cannot add owners under least privilege) and MUST be documented.

#### Scenario: Owned apps discoverable
- **WHEN** the portal loads the manageable app list in owner mode
- **THEN** the list contains exactly the app registrations owned by the managed identity's service principal

#### Scenario: Non-owned app not manageable
- **WHEN** an operation targets an app registration the identity does not own in owner mode
- **THEN** the operation fails and the admin sees an actionable message explaining the ownership prerequisite

### Requirement: Opt-in tenant-wide mode
An explicit configuration flag (`REGNROLL_GRAPH_MODE=All`, default `OwnedBy`) SHALL switch discovery to all app registrations in the tenant, for organizations that grant `Application.ReadWrite.All` instead of managing ownership. The default MUST remain owner mode (least privilege), and documentation MUST state the elevated blast radius of tenant-wide mode.

#### Scenario: Default stays least-privilege
- **WHEN** no mode configuration is provided
- **THEN** the system behaves in owner mode

#### Scenario: Tenant-wide discovery when opted in
- **WHEN** `REGNROLL_GRAPH_MODE=All` is configured and the required permission is granted
- **THEN** the portal lists all app registrations in the tenant as manageable

### Requirement: Certificate writes preserve existing key credentials
Because app-only callers cannot use `addKey` without proof-of-possession, certificate additions SHALL be performed by updating the application's `keyCredentials` collection with the complete existing list plus the new certificate, never replacing or dropping existing entries.

#### Scenario: Concurrent-safe append semantics
- **WHEN** a certificate is added to an app registration that already has two certificates
- **THEN** after the write the app registration has all three certificates

### Requirement: Graph failures surface actionable errors
Graph authorization and consent failures (e.g. missing permission grant, missing ownership) SHALL be reported to the admin with the failing operation, the likely cause, and the documented remediation — never as a silent failure or a bare 500.

#### Scenario: Missing permission grant
- **WHEN** Graph rejects a call because the app-only permission has not been consented
- **THEN** the admin-facing error names the missing permission and links the setup documentation
