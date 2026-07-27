---
title: Email templates
description: The four notification templates and how to customize them without redeploying.
---

Regnroll sends four kinds of email, each backed by a template with a subject and an HTML body:

| Key | Sent when |
| --- | --- |
| `new-secret` | A new client secret was created — contains the one-time retrieval link |
| `new-certificate` | A new certificate is requested — contains the upload link |
| `warning` | A link is still unused ("not opened") within warn-before of the old credential's expiry |
| `expired` | An expired secret/certificate was removed from the app registration |

## Variables

Templates use plain `{variable}` placeholders, substituted at send time:

| Variable | Value |
| --- | --- |
| `{regnroll_url}` | The retrieval or upload URL (empty in `warning`/`expired` — those links cannot be reconstructed by design; see below) |
| `{credential_type}` | `secret` or `certificate` |
| `{expiry_date}` | Expiry date of the old credential (`yyyy-MM-dd`) |
| `{client_id}` | Client id of the app registration |
| `{client_name}` | Display name of the app registration |
| `{token_endpoint}` | Your tenant's OAuth token endpoint |

Unknown placeholders are left verbatim — a typo never breaks sending.

:::note
Why no link in the warning email? Regnroll stores only a **hash** of each link id and never stores the decryption key, so it *cannot* rebuild the original URL. The warning deliberately points the customer at the original email (or at IT support for a fresh link) — re-issuing automatically would create a new secret each time.
:::

## Customizing

In the admin portal's template editor: pick a template, edit the subject and HTML body, **Save override**. Overrides are stored in the data storage account and apply to the very next email — no redeploy. **Reset to default** returns to the embedded template.

Emails are sent through Azure Communication Services using the managed identity. The sender address defaults to the ACS managed domain (`DoNotReply@<guid>.azurecomm.net`); to send from your own domain, [add a custom domain to the Email Communication Service](https://learn.microsoft.com/azure/communication-services/quickstarts/email/add-custom-verified-domains) and set `Regnroll__SenderAddress`.
