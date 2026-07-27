---
title: Customer guide
description: Retrieving a delivered secret, uploading a certificate, and automating both.
---

This page is for the *receiving* side — the team operating an application whose credentials are managed by Regnroll. You do not need an account: the emailed links are self-contained.

## Retrieving a new client secret

You receive an email with a link like `https://…/s/<id>#<key>`. Opening it shows a page — **opening does nothing by itself**; automated mail scanners cannot consume the secret.

1. Be ready to store the secret in your configuration.
2. Press **Reveal secret**. This consumes the link permanently.
3. Copy the secret. The page also shows your client id and the new secret's expiry.

If the page says the secret is gone or the link expired, contact the IT team that manages the application — they can issue a fresh link in seconds.

:::tip
The old secret keeps working until its own expiry date (shown in the email), so you have the whole overlap window to switch.
:::

### Automating retrieval

The reveal button is just an HTTP POST — your deployment tooling can claim the secret with no coordination:

```shell
# link format: https://<host>/s/<id>#<key>
curl -sS -X POST https://<host>/api/claim \
  -H "Content-Type: application/json" \
  -d '{"id":"<id>","key":"<key>"}'
# → {"secret":"…","clientId":"…","newSecretExpiresAt":"…"}   (exactly once)
```

Form encoding works too: `curl -X POST …/api/claim -d id=<id> -d key=<key>`. A wrong key returns an error **without** consuming the secret; a second claim returns HTTP 410.

## Uploading a new certificate

For certificate-based auth, you generate the key pair — Regnroll never sees your private key. The email links to `https://…/c/<token>`:

1. Generate a new key pair and certificate, e.g.:
   ```shell
   openssl req -x509 -newkey rsa:2048 -keyout key.pem -out cert.pem -days 365 -nodes -subj "/CN=my-app"
   ```
2. On the upload page, choose `cert.pem` (or paste the PEM) and press **Upload certificate**.
3. The page confirms with the thumbprint. Your current certificate is **not** removed — switch over at your own pace.

Uploads containing private key material (PFX, PEM private keys) are rejected outright, as are expired or unparseable certificates. Rejections do not consume the link.

### Automating the upload

```shell
curl -sS -X POST https://<host>/api/upload \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"<token>\",\"certificate\":\"$(base64 -w0 cert.cer)\"}"
# → {"thumbprint":"…","notAfter":"…"}
```

## Reminder and expiry emails

- **Reminder**: if a link is still unused close to the old credential's expiry, you get one reminder pointing at the original email.
- **Expired**: when an expired credential is removed from the app registration, you are notified — if your integration is failing at that point, contact your IT team for a fresh credential.
