# Signature R brand design — September 12, 2026

## Scope

Implemented the user-selected PulsR TMS Signature R direction as self-contained
vector artwork, a shared `Shared/Brand/BrandLogo` component, matching R favicon,
and a visual reference at `/brand/index.html`. The SVG adapts the supplied raster
reference; it is not an exact reconstruction or a licensed-font identification.
Login and Sidebar reuse the same external SVG symbol. No font or remote asset
dependency was added. Existing theme, operational color and account identity
contracts remain unchanged.

The maintained [brand design](../../design/brand-design.md) is linked from the
required UI instructions and documents naming, assets, variants, palette,
typography, clear space, responsive behavior and prohibited treatments.

The first browser matrix exposed document overflow at 320px with 200% root text
size. Login now uses bounded grid content and smaller named padding at narrow
widths; Users pagination wraps without shrinking its touch controls. Mobile
navigation groups its logo and toggle above the backdrop, replacing the stale
60px inset with the actual header-relative panel height. At narrow widths the
toggle retains its accessible name and full touch size without its visible label.

## Verification

- `npm run styles:build --prefix Client`: passed.
- Affected `bash test.sh styles identity`: passed (server 167, Client 52, required
  Node and architecture checks). Early source checks caught a test selector that
  also inspected navigation icons and non-semantic swatch access; both were
  corrected without relaxing architecture requirements.
- Final `bash test.sh all`: server 1,533, Client 704 and Node 438 passed, with no
  failed or skipped tests. Includes logo variants, shared asset ownership,
  self-contained SVG and matching Signature R geometry regressions.
- `dotnet build Client/Client.csproj -warnaserror -p:UseSharedCompilation=false`:
  passed with zero warnings or errors. Local Client restarted on port 5067.
- Managed Release publish passed at `artifacts/managed/release-ob3xjk/publish`;
  `Client/build/verifyRelease.mjs` verified 261 assets and seven JavaScript
  entry-point dependency graphs. Optional WASM workload optimization was not run.
- `Client/tests/browser/brandSmoke.mjs` against that staged artifact: 35 cases
  passed in Chrome, with no browser errors or unexpected requests. Includes real
  Login and Users/Sidebar at 1440/768/390/320px, both themes and 100%/200% root
  font size, desktop/mobile visual guides and 16px/32px favicon samples. API
  responses and identity were isolated fixtures; all other requests were blocked.
  Checks cover painted SVG pixels, geometry, minimum wordmark width, horizontal
  overflow, Login controls, retained account identity and mobile header/panel
  positioning. Report/screenshots: `artifacts/managed/browser-ui-k8PEQl`.
- Representative guide, Login, dark Login and enlarged mobile navigation
  screenshots were inspected. The remaining existing Users/account layout at
  extreme text enlargement is not certified by these brand-specific assertions;
  passing bounds checks are not a complete application visual audit.
- `git diff --check`: passed.

No server code, schema, database records, cloud resources, live provider calls or
production deployment were changed in this task. No Git commit or push was made.
Live authentication, the full authenticated application matrix and real
PostgreSQL checks were not run; no isolated PostgreSQL fixture is available.
This work introduces no migration and does not certify pre-existing pending
migrations. No performance claims are made. Managed artifact retention removed
expired generated runs only; source and application data were not cleanup targets.
