# admin-portal

## ADDED Requirements

### Requirement: Function-hosted admin UI
The function app SHALL serve a minimal HTML admin portal directly from the function app itself (acmebot-style), with no separately hosted frontend.

#### Scenario: Admin opens the portal
- **WHEN** an authenticated admin browses to the portal root
- **THEN** the function app returns the admin UI page itself (HTML + embedded assets served by HTTP-triggered functions)

### Requirement: Admin surface requires Entra authentication
All admin pages and admin API endpoints SHALL require an Entra ID-authenticated user via App Service built-in authentication (EasyAuth). The application code MUST additionally validate the injected client principal on every admin API request, so admin endpoints fail closed even if platform auth is misconfigured. Public customer endpoints are exempt (see `secure-link-delivery`).

#### Scenario: Unauthenticated browser request
- **WHEN** an unauthenticated user requests an admin page
- **THEN** the platform redirects the user to the Entra ID sign-in flow

#### Scenario: Request without a client principal reaches admin API
- **WHEN** an admin API request arrives without a valid `X-MS-CLIENT-PRINCIPAL` header (e.g. EasyAuth disabled or misconfigured)
- **THEN** the request is rejected with 401 and no admin action is performed

### Requirement: List manageable app registrations
The portal SHALL list the app registrations the function's managed identity can manage (in default owner mode: the app registrations it owns), showing for each whether it is linked to Regnroll, its credential expiry overview, and pending delivery-link status.

#### Scenario: Portal lists owned app registrations
- **WHEN** an admin opens the portal in default owner mode
- **THEN** all app registrations owned by the managed identity are listed with their linked/unlinked state

### Requirement: Link an app registration
An admin SHALL be able to link (register) a manageable app registration to Regnroll. Linking stores a metadata record containing at minimum the `client_id`, one or more customer contact email addresses, and optional per-app overrides. Linking MAY optionally trigger the new-secret or new-certificate flow immediately.

#### Scenario: Link with contact email
- **WHEN** an admin links an app registration and provides a customer contact email
- **THEN** a metadata record is created for that `client_id` with rotate-before and warn-before resolving to the environment defaults

#### Scenario: Link with immediate credential flow
- **WHEN** an admin links an app registration and selects "create secret now" (or "request certificate now")
- **THEN** the corresponding new-secret (or new-certificate) flow is triggered right after the metadata record is created

### Requirement: Unlink an app registration
An admin SHALL be able to unlink an app registration. Unlinking removes the Regnroll metadata record and any pending delivery links for it, and MUST NOT delete the app registration or any of its credentials.

#### Scenario: Unlink removes metadata only
- **WHEN** an admin unlinks a linked app registration
- **THEN** its metadata record and pending links are removed
- **AND** the app registration and all its secrets/certificates remain untouched in Entra ID

### Requirement: Per-app setting overrides
An admin SHALL be able to override `rotate-before` and `warn-before` per linked app registration and to edit its contact email addresses. When no override is set, the environment-variable defaults apply (rotate-before 30 days, warn-before 7 days).

#### Scenario: Override honored
- **WHEN** an admin sets rotate-before to 60 days for one app registration
- **THEN** the lifecycle engine uses 60 days for that app registration and the default for all others

#### Scenario: Clearing an override
- **WHEN** an admin clears a per-app override
- **THEN** the environment default applies to that app registration again

### Requirement: Manual credential triggers
An admin SHALL be able to trigger the new-secret flow and the new-certificate flow on demand for a linked app registration. A manual trigger supersedes any pending delivery link of the same credential type for that app registration (the prior link is invalidated).

#### Scenario: Manual new-secret trigger
- **WHEN** an admin triggers "new secret" for a linked app registration
- **THEN** a new client secret is created and a fresh secure delivery link is emailed to the customer contact

#### Scenario: Manual re-trigger invalidates prior link
- **WHEN** an admin triggers "new secret" while an unclaimed secret delivery link already exists for that app registration
- **THEN** the prior link stops working and only the newly issued link can claim a secret

### Requirement: Delivery status visibility
The portal SHALL show the status of delivery links (pending / claimed / uploaded / expired, with timestamps) per linked app registration, without ever displaying secret material.

#### Scenario: Pending link visible
- **WHEN** a secret delivery link has been emailed but not claimed
- **THEN** the portal shows that app registration with a pending ("not-opened") delivery link and its expiry time
- **AND** no plaintext or ciphertext secret content is shown
