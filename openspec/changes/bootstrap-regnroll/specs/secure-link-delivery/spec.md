# secure-link-delivery

## ADDED Requirements

### Requirement: Encrypted-only payload storage
Secret payloads SHALL be stored exclusively as AES-256-GCM ciphertext in the dedicated data storage account. The random 256-bit encryption key SHALL exist only inside the generated URL and MUST never be persisted or logged server-side. Compromise of the storage account alone MUST NOT reveal any secret.

#### Scenario: Storage contains no plaintext
- **WHEN** a secret delivery payload is at rest awaiting claim
- **THEN** the stored record contains only ciphertext, nonce/tag, and non-sensitive metadata
- **AND** the decryption key appears nowhere in storage, configuration, or logs

### Requirement: Single self-contained link
Generated URLs SHALL be single, self-contained links (zero-effort model): the link carries both the payload identifier and, for secret retrieval, the decryption key in the URL fragment, so the receiving customer needs no second channel. The decryption key MUST be placed in the URL fragment so browsers never transmit it on page load.

#### Scenario: One link is enough
- **WHEN** a customer receives a secret delivery email
- **THEN** the single contained URL is sufficient to retrieve the secret with no additional key or password from another channel

### Requirement: Unguessable identifiers
Link identifiers and upload tokens MUST be generated from a cryptographically secure random source with at least 128 bits of entropy.

#### Scenario: Identifier entropy
- **WHEN** any delivery link or upload token is generated
- **THEN** its identifier contains at least 128 bits of cryptographically secure randomness

### Requirement: Scanner-safe retrieval (GET is harmless)
An HTTP GET of a retrieval or upload URL SHALL only return the static page shell. GET MUST NOT return secret material, MUST NOT consume/burn the payload, and MUST NOT change link status. Consumption happens only via an explicit POST. This guarantees corporate email-security scanners that prefetch links cannot expire the secret.

#### Scenario: Email scanner prefetches the link
- **WHEN** an email protection tool issues GET requests against a retrieval URL
- **THEN** the payload remains intact and the link status remains "not-opened"

### Requirement: One-time POST claim
A POST claim request carrying a valid payload identifier and decryption key SHALL return the plaintext secret exactly once and MUST delete the ciphertext record in the same operation. Any subsequent claim attempt SHALL receive a "gone" response instructing the customer to contact their IT support.

#### Scenario: First claim succeeds and burns
- **WHEN** a customer clicks "Reveal secret" on the retrieval page (issuing the POST with id and key)
- **THEN** the plaintext secret is returned once and the stored ciphertext is deleted immediately

#### Scenario: Second claim fails
- **WHEN** a claim is attempted for an already-claimed identifier
- **THEN** the response indicates the secret is no longer available and suggests contacting IT support

#### Scenario: Wrong key does not burn the payload
- **WHEN** a claim is attempted with a valid identifier but an incorrect decryption key
- **THEN** an error is returned and the ciphertext record is NOT deleted

### Requirement: Automation-friendly claim API
The claim endpoint SHALL accept a plain HTTPS POST (form or JSON) with the identifier and key and respond with machine-readable JSON, so customers can automate retrieval (e.g. a single curl command parsing the emailed URL) without any extra effort from the operating IT support team. The retrieval page and documentation SHALL show the automation recipe.

#### Scenario: Headless claim
- **WHEN** a customer's automation POSTs the identifier and key extracted from the emailed URL to the claim endpoint
- **THEN** the plaintext secret is returned as JSON exactly once, identical in behavior to the browser flow

### Requirement: Link expiry
Every link SHALL have an expiry: by default `REGNROLL_LINK_TTL_DAYS` (default 14 days), and when the link was created to rotate an expiring credential, no later than that old credential's expiry time. Expired links SHALL present an "expired — contact your IT support" page, and expired payload records SHALL be purged by the daily cleanup.

#### Scenario: Expired link rejected
- **WHEN** a claim or upload is attempted after the link expiry
- **THEN** the request is rejected with an expired-link response and no payload is returned

### Requirement: Public endpoints are anonymous
The retrieval page, upload page, and their POST endpoints SHALL be reachable without any authentication, while all admin routes remain protected (see `admin-portal`). This split MUST survive the EasyAuth configuration (excluded paths).

#### Scenario: Customer without Entra account
- **WHEN** an external customer with no account in the hosting tenant opens a delivery link
- **THEN** the page loads without any sign-in prompt

### Requirement: Dedicated data storage account
One-time payloads and Regnroll metadata SHALL be stored in a dedicated storage account, separate from the storage account backing the Azure Functions host, so runtime hosting concerns and customer data are isolated.

#### Scenario: Separate accounts
- **WHEN** the infrastructure is deployed
- **THEN** the Functions host storage account and the Regnroll data storage account are two distinct resources, and payload/metadata tables exist only in the data account
