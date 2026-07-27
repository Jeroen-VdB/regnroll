---
title: Admin guide
description: Linking app registrations, overrides, manual triggers and delivery status.
---

The admin portal is served by the function app itself at `https://<app>.azurewebsites.net/` behind Entra sign-in.

## The app list

The portal lists every app registration the managed identity can manage, with:

- linked / not linked state,
- credential overview (count + latest expiry per type),
- effective rotation settings (`*` marks a per-app override),
- recent delivery links and their status.

Apps that are linked but no longer visible to the identity (ownership removed, app deleted) stay in the list flagged in red instead of disappearing silently.

## Linking and unlinking

**Link…** stores Regnroll metadata for the app: the customer contact email(s) (semicolon-separated) and optional immediate flows (*create secret now* / *request certificate now* — the flows shown in the `new-app-reg` use case).

**Unlink** removes the metadata and any pending links. It **never** deletes the app registration or any of its credentials.

## Per-app settings

Each linked app can override the environment defaults:

| Setting | Default | Meaning |
| --- | --- | --- |
| rotate-before | 30 days | create/request the replacement credential this long before the old one expires |
| warn-before | 7 days | send a reminder for still-unopened links this long before expiry |

Clearing a field returns the app to the environment default.

## Manual triggers

- **New secret** — creates a client secret via Graph immediately and emails a one-time retrieval link.
- **New certificate** — emails a certificate upload link.

A manual trigger **supersedes** any pending link of the same type: the old link stops working and only the new one is valid.

## Delivery status

Per link the portal shows: type, status (**Not opened** / Claimed / Uploaded / Expired), sent and expiry dates, and whether a warning was sent. Secret material is never displayed — after a claim, only the stripped receipt remains.

## Run the scan on demand

**Run lifecycle scan now** executes the same logic as the daily timer and reports a summary (rotated / requested / warned / removed / purged / errors). Useful after changing settings or for verification.

## Email templates

The template editor at the bottom of the portal manages the four notification emails — see [Email templates](/email-templates/).
