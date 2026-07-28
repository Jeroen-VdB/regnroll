**⚠️ Regnoll is not yet production ready.**

# Regnroll

**Entra ID app registration secret & certificate automation** — a cheap, simple, self-hostable Azure Function that rotates app registration credentials before they expire and delivers them to your customers through one-time encrypted links.

- **Rotate early** — a daily scan creates replacement client secrets (default 30 days before expiry) and requests replacement certificates.
- **Deliver securely** — secrets are stored AES-256-GCM encrypted only; the decryption key lives exclusively in the emailed URL. Retrieval requires an explicit POST, so email-security scanners can never burn a link. The same POST is a zero-setup automation API for the receiving side.
- **Chase stragglers** — reminder emails for links still unopened close to expiry (default 7 days), automatic cleanup of expired credentials with a notification.
- **Least privilege by default** — the function's managed identity uses `Application.ReadWrite.OwnedBy` and only manages app registrations it owns; tenant-wide mode is an explicit opt-in.
- **Cheap** — one Flex Consumption function app, two storage accounts, pay-per-email Azure Communication Services. No Key Vault by design.

Architecture modeled on [acmebot](https://github.com/polymind-inc/acmebot) (function-hosted minimal UI, EasyAuth); delivery logic modeled on [yopass](https://github.com/jhaals/yopass) (encrypted-only storage, one-time links).

## Deploy

Prerequisites: [Azure Developer CLI](https://aka.ms/azd), Azure CLI (signed in), and rights to create an app registration; a tenant admin for the one-time Graph permission grant.

```shell
azd up                                          # provision + deploy (postprovision hook wires EasyAuth)
pwsh ./infra/scripts/grant-graph-permissions.ps1  # tenant admin: grant Application.ReadWrite.OwnedBy
az ad app owner add --id <app object id> --owner-object-id <printed principal id>   # per managed app
```

Then open the printed URL, sign in, and link your app registrations in the admin portal.

## Documentation

Full documentation (deployment, permission setup, admin & customer guides, automation recipes, configuration reference, security model) lives at **[regnroll.github.io](https://regnroll.github.io)** — source under [`docs/`](docs/).

## Repository layout

| Path | Contents |
| --- | --- |
| `src/Regnroll.App/` | C# Azure Functions app (.NET 10 isolated, Flex Consumption) — admin portal, public pages, APIs, daily timer |
| `tests/Regnroll.App.Tests/` | xUnit suite incl. Azurite-backed storage integration tests |
| `infra/` | Bicep (azd) + post-provision and permission scripts |
| `docs/` | Astro Starlight documentation site + drawio use-case diagrams |
| `openspec/` | Spec-driven change artifacts |

## Development

```shell
azurite --silent &                                # local table storage
cp src/Regnroll.App/local.settings.sample.json src/Regnroll.App/local.settings.json
cd src/Regnroll.App && func start                 # func resolves the app root from the current directory
dotnet test                                       # unit + integration tests (from the repo root)
```

## License

See [LICENSE](LICENSE).
