---
title: Security model
description: Encrypted-only storage, one-time POST-gated links, and the reasoning behind the design.
---

## What Regnroll protects against

Regnroll's server *generates* client secrets via Graph, so — unlike yopass — the design goal is not zero-knowledge. The goals are:

1. **Nothing recoverable at rest.** A dump of the data storage account must reveal no secret.
2. **Email scanners cannot burn links.** Corporate mail protection that prefetches URLs must never consume a one-time secret.
3. **A compromised mailbox has a bounded window.** One-time semantics, link TTL, and admin-visible claim status limit the damage of a leaked email.

## How delivery works

- Every secret is encrypted with **AES-256-GCM** under a random 256-bit key. Storage holds only the ciphertext, nonce and tag — plus a **SHA-256 hash** of the link id as the row key, so storage alone cannot even reconstruct valid URLs.
- The key travels **only in the URL fragment** (`/s/<id>#<key>`). Fragments are never sent by browsers on page load, so server logs never see the key.
- `GET` of a link serves a static page shell and nothing else — it cannot reveal, consume, or change anything. **The only consuming operation is `POST /api/claim`**, which verifies the GCM tag, returns the plaintext exactly once, and strips the ciphertext in the same atomic (ETag-conditional) write. A wrong key fails verification *without* consuming the payload; a concurrent race is won by exactly one caller.
- Links expire (default 14 days, never later than the old credential's expiry) and expired rows are purged daily.

This is strictly stronger than the classic yopass flow against scanners: yopass relies on scanners not executing JavaScript; Regnroll's GET has nothing to burn.

### Why one self-contained URL (no split links)?

yopass optionally splits id and key across two channels. Regnroll deliberately does not, because (a) the burn-risk split links mitigate is eliminated by POST-only claiming, (b) a second channel per rotation is exactly the manual effort this product removes, and (c) zero-setup automation needs everything in one place. The residual risk — a compromised mailbox — is mitigated by one-time claiming (visible to admins), TTL, and rotation cadence.

## Why no Key Vault?

Key Vault would add per-secret vault objects, access policies and cost for payloads that live *hours to days* and are deleted on first read. Encrypted-at-rest table rows with keys that only exist inside emailed URLs achieve the stated threat model with far less machinery. (Credentials that must live long-term — there are none in Regnroll — would be a different conversation.)

## Admin surface

- App Service built-in authentication (EasyAuth) requires Entra sign-in for everything except the enumerated public paths.
- Independently, every admin endpoint validates the platform-injected `X-MS-CLIENT-PRINCIPAL` header and **fails closed** on Azure — a misconfigured or disabled EasyAuth yields 401s, not exposure. External callers cannot spoof the header (App Service strips it).
- Public endpoints never leak tenant internals: Graph/permission errors surface to customers as a generic "contact IT support" message and are logged server-side.

## Graph blast radius

Default mode is `Application.ReadWrite.OwnedBy`: the identity can only touch app registrations it owns, and it cannot expand its own access (adding owners requires `Application.ReadWrite.All`, which it does not have). Automatic deletions are restricted to credentials **already past expiry** — cryptographically dead material. Valid credentials are never deleted by any code path, and this is covered by tests.

## Operational notes

- Both storage accounts disable shared-key access; all data-plane access is Entra RBAC via the managed identity.
- The app never logs link ids, keys, or secret material; correlation uses the SHA-256 row key.
- Secrets created by Regnroll carry an identifiable display name (`regnroll-<timestamp>`) for auditability in Entra.
- If sending the delivery email fails, the just-created secret is rolled back (`removePassword`) so no live-but-undelivered credential lingers from failed flows.
