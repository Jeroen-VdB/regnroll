# certificate-rotation

## ADDED Requirements

### Requirement: Certificate renewal via customer upload
When the new-certificate flow is triggered (manually, at link time, or by the lifecycle engine), the system SHALL create a secure upload link and email it to the customer contact(s) using the "new certificate upload" template. The customer generates their own key pair and uploads only the public part; Regnroll never receives or stores private key material.

#### Scenario: Upload link emailed
- **WHEN** the new-certificate flow runs for a linked app registration
- **THEN** the customer contact receives an email containing a unique certificate upload URL

### Requirement: Accept and apply uploaded certificate
When a customer submits a valid public certificate (PEM or DER/CER, Base64 or binary) through the upload page or API with a valid upload token, the system SHALL add it to the app registration's `keyCredentials` via Graph while preserving all existing certificates ("does not remove old").

#### Scenario: Certificate added preserving existing
- **WHEN** a customer uploads a valid certificate via a valid upload link
- **THEN** the certificate appears as a new key credential on the app registration
- **AND** all previously present certificates remain in place
- **AND** the customer sees a confirmation including the certificate thumbprint

### Requirement: Uploaded certificate validation
The system MUST validate uploads before touching Graph and reject: content that is not a parseable X.509 certificate, certificates that are already expired (or not yet valid), and any content containing private key material (e.g. PFX/PKCS#12 or PEM private keys). Rejections MUST explain the problem and MUST NOT consume the upload link.

#### Scenario: Garbage upload rejected
- **WHEN** a customer uploads content that cannot be parsed as an X.509 certificate
- **THEN** the system responds with a clear validation error
- **AND** no Graph write occurs and the upload link remains usable

#### Scenario: Private key material rejected
- **WHEN** a customer uploads a PFX file or a PEM containing a private key
- **THEN** the upload is rejected with an explanation that only the public part must be submitted
- **AND** the submitted content is not persisted

#### Scenario: Expired certificate rejected
- **WHEN** a customer uploads a certificate whose NotAfter date is in the past
- **THEN** the upload is rejected and the upload link remains usable

### Requirement: Upload link lifecycle
An upload link SHALL remain usable until a certificate is successfully applied or the link expires, whichever comes first. A successful upload consumes the link; subsequent uses are rejected. Link status ("not-opened"/pending, uploaded, expired) SHALL be tracked for the warning flow and admin visibility.

#### Scenario: Link consumed after successful upload
- **WHEN** a certificate has been successfully applied via an upload link
- **THEN** subsequent requests with that link are rejected with a "link already used" response
