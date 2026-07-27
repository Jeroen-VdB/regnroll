# lifecycle-automation

## ADDED Requirements

### Requirement: Daily lifecycle scan
A timer-triggered function SHALL run on a configurable schedule (default: once daily) and evaluate every linked app registration's secrets and certificates against that app's effective rotate-before and warn-before settings (per-app override, else environment default: rotate-before 30 days, warn-before 7 days).

#### Scenario: Timer evaluates all linked apps
- **WHEN** the daily timer fires
- **THEN** every linked app registration is evaluated using its effective settings

### Requirement: Automatic secret rotation (rotate-before)
When the latest-expiring client secret of a linked app registration expires within its rotate-before window and no unclaimed secret delivery link is pending for it, the engine SHALL trigger the new-secret flow. The check MUST be idempotent: consecutive runs while a delivery is pending or after a successful rotation MUST NOT create additional secrets or links.

#### Scenario: Rotation triggered inside window
- **WHEN** the daily scan finds a linked app registration whose latest secret expires in fewer days than rotate-before and no pending delivery exists
- **THEN** a new secret is created and a delivery link is emailed

#### Scenario: No duplicate on next run
- **WHEN** the daily scan runs again while that delivery link is still unclaimed
- **THEN** no additional secret and no additional email are produced

### Requirement: Automatic certificate renewal request (rotate-before)
When the latest-expiring certificate of a linked app registration expires within its rotate-before window and no pending upload link exists, the engine SHALL trigger the new-certificate flow (emailed upload link). Idempotency rules match the secret flow.

#### Scenario: Upload link requested inside window
- **WHEN** the daily scan finds a linked app registration whose latest certificate expires within rotate-before and no pending upload link exists
- **THEN** a certificate upload link is created and emailed

### Requirement: Warning for unactioned links (warn-before)
When a delivery or upload link is still pending ("not-opened") and the old credential's expiry is within the warn-before window, the engine SHALL send the warning email (explaining that an earlier notification should have been received and that action is required within the remaining time). Each link SHALL be warned at most once.

#### Scenario: Warning sent once
- **WHEN** the daily scan finds a pending link whose related credential expires in fewer days than warn-before and no warning was sent yet
- **THEN** the warning email is sent and the link is marked as warned

#### Scenario: No repeated warning
- **WHEN** the daily scan runs again for a link already marked as warned
- **THEN** no additional warning email is sent

### Requirement: Expired credential cleanup and notification
When a secret or certificate on a linked app registration is past its expiry, the engine SHALL remove that credential via Graph and send the "expired/removed" notification email. Only credentials that are already expired (cryptographically useless) may ever be deleted automatically. Expired unclaimed payload records and expired links SHALL also be purged.

#### Scenario: Expired secret removed
- **WHEN** the daily scan finds an expired client secret on a linked app registration
- **THEN** the expired secret is removed from the app registration
- **AND** the customer contact receives the expired-notification email

#### Scenario: Valid credentials never auto-deleted
- **WHEN** the daily scan evaluates credentials that have not yet expired
- **THEN** none of them are deleted, regardless of any other state

#### Scenario: Expired payloads purged
- **WHEN** the daily scan finds payload records whose link expiry has passed
- **THEN** those records are deleted from the data storage account
