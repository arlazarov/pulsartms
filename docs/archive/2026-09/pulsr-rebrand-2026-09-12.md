# PulsR product rebrand — September 12, 2026

## Scope

- Replaced product branding in Sidebar, Login, the HTML document and all nine
  Razor page titles. Added a simple P favicon and reused existing layout/color tokens.
- Renamed the solution to `PulsR.slnx`, updated executable build/test references
  and root discovery, and renamed the private npm package to `pulsr-client`.
- Changed the Gmail API client's display application name to `PulsR`; retained
  its credential user identity and added an assertion for that boundary.
- Updated maintained product/development documentation. Existing unrelated
  working-tree changes were preserved.

The [compatibility guide](../../development/product-branding.md) lists intentional
legacy names. Carrier data, persistent identities, encryption purposes, roles,
browser storage/locks, metrics, external resources and deployment identifiers were
not migrated. The GitHub remote and workspace directory were not renamed.

## Verification

- First `bash test.sh all`: server 1,532 passed; Client 692 passed and one failed.
  `SavingFuelPreferencesDoesNotSubmitOrResetAnIntegrationDraft` could not find
  `#settings-ifta` after a separate integration section became available. The
  runner stopped before Node checks. Its existing immediate lookup was not changed.
- Immediate full repeat without a code change: server 1,532/1,532, Client 693/693,
  Node 428/428 passed, including architecture checks. The first timing-sensitive
  failure remains a test-stability observation, not a corrected invariant.
- `dotnet build Client -warnaserror -p:UseSharedCompilation=false`: passed with
  zero warnings and errors; Client/style/JavaScript outputs rebuilt.
- Managed Release publish of Client: passed. `Client/build/verifyRelease.mjs`
  verified 255 assets and seven JavaScript entry-point dependency graphs in
  `artifacts/managed/release-xga8xl/publish`.
- Focused offline Chrome branding check: 12 cases at 1440/768/390px, light/dark
  themes, 100%/200% root font sizes. Real staged Login and Users/Sidebar were
  rendered with intercepted fixture APIs; no provider calls or business writes.
  Product text, page titles, favicon availability, brand bounds and retained account
  identity passed; no browser errors or unexpected requests. There are 24 screenshots
  and `pulsr-branding-report.json` under `artifacts/managed/browser-ui-TswYz4`.
- Representative desktop Login/Sidebar and mobile Sidebar screenshots were
  inspected. At 200% root size the existing mobile backdrop's fixed 60px inset
  shades part of the taller header. This pre-existing layout issue is not fixed
  by the product rename; successful text/bounds checks are not full visual approval.
- `git diff --check`: passed.

The local Client development server was stopped before its direct build and
restarted on port 5067. The API process was not restarted. No production deployment,
domain registration, cloud/GitHub rename, OAuth consent-screen update or database
operation was performed. This rebrand adds no migration; it does not certify or
apply pre-existing pending migrations from other work.

Live authentication/provider behavior, the full authenticated browser matrix and
real PostgreSQL checks were not run. No isolated PostgreSQL fixture is available;
no local SQL server was started. No performance claims are made. The existing
artifact-retention runner removed two expired managed output runs during publishing;
application data and source files were not cleanup targets.
