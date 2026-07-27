# secret-rotation

## ADDED Requirements

### Requirement: Secret creation via Microsoft Graph
When the new-secret flow is triggered (manually, at link time, or by the lifecycle engine), the system SHALL create a new client secret on the target app registration via Graph `addPassword`, capture the returned `secretText` exactly once in memory, and MUST never persist the plaintext value anywhere.

#### Scenario: Secret created and captured once
- **WHEN** the new-secret flow runs for a linked app registration
- **THEN** a new password credential exists on the app registration
- **AND** the plaintext secret exists only transiently in process memory before being encrypted for delivery

### Requirement: Identifiable secret naming and validity
Secrets created by Regnroll SHALL carry an identifiable display name (containing "regnroll" and a creation marker) and SHALL use a validity period from configuration (`REGNROLL_SECRET_VALIDITY_DAYS`, default 180 days).

#### Scenario: Default validity applied
- **WHEN** a new secret is created without custom configuration
- **THEN** its expiry is approximately 180 days from creation
- **AND** its display name identifies it as Regnroll-managed

### Requirement: Handoff to secure delivery
Immediately after creating a secret, the system SHALL encrypt it, store only the ciphertext as a one-time payload, generate the secure retrieval URL, and send the "new client secret retrieval" email to the customer contact(s).

#### Scenario: Delivery email sent
- **WHEN** the new-secret flow completes
- **THEN** the customer contact receives an email rendered from the new-secret template containing the one-time retrieval URL

### Requirement: Old secrets are not removed on rotation
Creating a replacement secret SHALL NOT remove or invalidate existing secrets on the app registration. Old secrets remain valid until their own expiry (cleanup of expired secrets is handled by `lifecycle-automation`), giving customers an overlap window to switch.

#### Scenario: Overlap window after rotation
- **WHEN** the rotation flow creates a new secret while the old secret has not yet expired
- **THEN** both the old and the new secret are present and valid on the app registration

### Requirement: Rotation decision based on latest-expiring secret
For rotation purposes the system SHALL evaluate the latest-expiring client secret on the app registration (whether or not Regnroll created it), so pre-existing customer secrets are also rotated before they expire.

#### Scenario: Pre-existing secret drives rotation
- **WHEN** a linked app registration only has a manually created secret expiring within the rotate-before window
- **THEN** the lifecycle engine triggers the new-secret flow for it
