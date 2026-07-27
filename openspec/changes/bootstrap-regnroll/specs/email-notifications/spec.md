# email-notifications

## ADDED Requirements

### Requirement: Email delivery via Azure Communication Services
All product emails SHALL be sent through Azure Communication Services Email using the function's managed identity (Entra ID auth); connection strings MUST NOT be required in the default deployment. The sender address SHALL be configurable, defaulting to the ACS Azure-managed-domain address (DoNotReply@<guid>.azurecomm.net).

#### Scenario: Email sent with managed identity
- **WHEN** any flow sends an email in a default deployment
- **THEN** the mail is submitted to ACS using Entra ID credentials of the managed identity and the configured sender address

### Requirement: Four email templates
The system SHALL provide exactly these templates, each with a subject and HTML body, used by the corresponding flows:
1. `new-secret` — new client secret retrieval link
2. `new-certificate` — new certificate upload link (public part)
3. `warning` — link still "not-opened" within warn-before: explains a notification email should have been received and must be actioned within the remaining time
4. `expired` — old secret/certificate was removed and should have been replaced

#### Scenario: Correct template per flow
- **WHEN** the secret rotation, certificate request, warning, or expired flow sends mail
- **THEN** the matching template (new-secret, new-certificate, warning, expired) is used

### Requirement: Template variables
Templates SHALL support at least these placeholders, substituted at send time: `{regnroll_url}` (retrieval or upload URL), `{credential_type}` ("secret" or "certificate"), `{expiry_date}` (expiry of the old credential), `{client_id}`, `{client_name}` (app registration display name), `{token_endpoint}` (the tenant's OAuth token endpoint). Unknown placeholders SHALL be left verbatim rather than failing the send.

#### Scenario: Variables substituted
- **WHEN** the new-secret template containing `{client_name}` and `{regnroll_url}` is rendered for an app registration
- **THEN** the sent email contains the app registration's display name and the actual one-time URL

#### Scenario: Unknown placeholder tolerated
- **WHEN** a customized template contains `{not_a_variable}`
- **THEN** the email is still sent with that placeholder left as-is

### Requirement: Customizable templates without redeploy
Default templates SHALL be embedded in the application. An admin SHALL be able to override any template (subject and body) from the admin portal, with overrides stored in the data storage account and applied to subsequent sends without redeploying, and SHALL be able to reset a template to its embedded default.

#### Scenario: Override applied
- **WHEN** an admin saves a customized `warning` template and the warning flow later runs
- **THEN** the sent email uses the customized subject and body

#### Scenario: Reset to default
- **WHEN** an admin resets the `warning` template
- **THEN** subsequent warning emails use the embedded default again
