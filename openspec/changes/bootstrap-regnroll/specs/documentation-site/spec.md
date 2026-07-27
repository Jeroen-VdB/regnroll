# documentation-site

## ADDED Requirements

### Requirement: Astro Starlight documentation project
The repository SHALL contain an Astro Starlight documentation site under `docs/` that builds cleanly and covers at least: introduction & architecture (with the exported use-case diagrams), deployment guide (azd), Graph permission & ownership setup, admin portal guide, customer guide (retrieving a secret, uploading a certificate, automation recipe), email template customization, configuration reference (all environment variables and defaults), and the security model (encrypted-only storage, one-time links, why Key Vault is not used).

#### Scenario: Site builds
- **WHEN** the docs project is built (`npm run build` in `docs/`)
- **THEN** the build succeeds and produces the listed pages

#### Scenario: Diagrams included
- **WHEN** the architecture page is viewed
- **THEN** the exported drawio PNGs are displayed

### Requirement: GitHub Pages deployment
A GitHub Actions workflow SHALL build and publish the docs site to GitHub Pages on pushes to the default branch, with the Starlight `site`/`base` configuration set so the published site is served at `https://regnroll.github.io` (org root pages repo) — configurable for forks that serve under a project path.

#### Scenario: Publish on push
- **WHEN** a change to the default branch is pushed to GitHub with Pages enabled
- **THEN** the workflow builds the Starlight site and deploys it to GitHub Pages
