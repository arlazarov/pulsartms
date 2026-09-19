# Dispatch stop expansion and repeated visits

## Observed failure

On September 11, read-only diagnostics for truck 11007, load 1383 found five
Torque stops but only two persisted stops. The source included three distinct
Webster pickup visits, an Amsterdam pickup and a Florida delivery. The dispatch
checkpoint reported repeated `DbUpdateConcurrencyException` failures.

A disposable in-memory SQLite reproduction using the real synchronization handler
showed the cause: new children with assigned GUIDs were discovered through an
existing tracked parent's navigation as modified rows. Updates for nonexistent
rows failed and rolled back the import. No application database was used as a
test fixture.

## Changes

- Explicitly insert newly imported stop sequences through the existing Application
  persistence interface. Preserve existing sequence identities, separate repeated
  visits, selective removals and post-save route/cache invalidation.
- Cards and the shared load dialog show counts for loads with more than two stops.
  Each repeated complete address gets `Visit N of total` context without merging
  stop IDs, appointments or completion state. Table preserves full-load ordinals
  across its pickup/delivery columns.
- Map inspectors distinguish full-load position from remaining-route marker
  numbering and identify repeated visits. Existing coincident-marker selection,
  route colors and geometry ownership are unchanged.
- Clear the current map route when the server explicitly flags changed inputs.
  Updated stop names cannot appear on old coordinates during route preparation.
  Discard obsolete ETA/mileage retention while preserving the dispatch identity
  for existing fuel actions. A valid replacement sends full geometry again.

## Verification

- `bash test.sh all`: 1,432 Server, 657 Client C# and 421 JavaScript checks passed
  (2,510 total), including architecture checks.
- The new server expansion regression reproduced the concurrency failure before
  the fix. It now verifies two-to-five expansion, repeated visits, retained IDs,
  route/successor invalidation, unchanged replay and selective removal.
- Strict Client Release build: zero warnings/errors. JavaScript type checking,
  SCSS/JavaScript builds and staged integrity verification passed: 252 published
  assets and seven entry-point dependency graphs.
- Staged browser UI: all 44 page cases passed. Expanded five-stop Cards, Details
  and available Table views were additionally checked at desktop/mobile widths,
  both themes and 100%/200% root font sizes.
- Native inspector: eight cases passed, including selecting all five occurrences,
  distinct appointments, repeat labels, unchanged map bounds and scroll access.
- HOS/ETA browser scenarios: ten cases passed, including quiet retention for
  still-compatible routes. Representative desktop/mobile repeated-visit
  screenshots were inspected visually.

Browser checks used deterministic offline APIs/map ports; native inspector tests
use the production renderer without Google Maps or GPU rendering. No live route
provider, PostgreSQL fixture, authenticated production browser or production
performance check was run. There is no schema migration. At the implementation
handoff, the application database had not been repaired manually and neither
server nor Client had been deployed. Live recovery requires the updated server
and a successful normal sync.

## Deployment follow-up

Published on September 11 after explicit authorization to deploy without repeating
checks. Google Cloud and Firebase authentication were renewed by the operator.

- Cloud Build: `44ab647d-ac14-4c7e-a2c8-b4a100ee71f2`, successful API image build
  without rerunning the verification stages.
- Image digest: `sha256:e08123cd5575c2db3cdffd844095fc407a7a6ed6471ec60d0284bbb2032600ad`.
- Cloud Run revision: `amftms-api-00098-slc`, reported serving 100% of traffic.
- Firebase Hosting: `https://amftms.web.app`, release completed using the previously
  verified `artifacts/managed/scratch-3jlkbN/publish/wwwroot` artifact (252 files).

No repeat test suite, post-deployment browser/API check or manual database repair
was performed. Successful live dispatch recovery was not separately measured;
normal background synchronization owns the import.

## Compact card follow-up

Wide summary cards now place appointment/ETA beside the address; narrow cards
retain a stacked layout. Summary rows omit a separate facility name when a street
is available, while Details and the location title preserve it. All five stops,
their order, repeat labels and appointments remain visible. Spacing uses the
existing tokens and type sizes are unchanged.

- `bash test.sh dispatch styles`: 476 Server, 390 Client C#, 86 style, four
  Dispatch JavaScript and 39 JavaScript architecture checks passed. This was an
  affected-category run, not a repeat full-suite run.
- Strict Client Release build/publish passed with zero warnings/errors; staged
  integrity verification covered 252 assets and seven entry-point graphs.
- Offline browser UI passed 12 width/theme/font scenarios (44 page cases), with
  no browser errors or unexpected requests. Desktop/mobile five-stop screenshots
  were visually inspected; layout assertions cover stacked and two-column facts.

This subsequent compact-layout change has not been deployed. No server code,
schema or live data changed in this follow-up.

## Compact layout deployment

Subsequently published the compact Dispatch cards and closer coincident stop
markers to Firebase Hosting (`https://amftms.web.app`) on September 11, following
explicit authorization to deploy without checks. A fresh Release publish produced
`artifacts/managed/release-eBVtYH/publish/wwwroot`; Firebase reported all 252 files
uploaded and the release completed. No test suite, artifact verification or
post-deployment browser check was rerun. The API and database were unchanged.

## Summary warning alignment follow-up

Compact Dispatch stop summaries now align the cycle warning beneath the ETA label,
not beneath the timestamp. Lateness remains grouped with the arrival time and can
wrap within available width. Detailed dialog and map layouts are unchanged.

`bash test.sh dispatch styles` passed 476 Server, 390 Client C#, 87 styles, four
Dispatch JavaScript and 39 JavaScript architecture checks. Strict Client publish
and staged integrity verification passed (252 assets, seven JavaScript entry-point
graphs). All ten offline HOS/ETA browser scenarios passed, including new summary
alignment assertions; desktop/mobile screenshots were visually inspected. This
was not a full-suite run or a live-data check. This subsequent CSS fix has not
been deployed.

### Warning alignment deployment

Following explicit authorization, Firebase Hosting released the previously built
`artifacts/managed/scratch-kefXMs/publish/wwwroot` artifact (252 files) to
`https://amftms.web.app`. Deployment completed successfully; no build, test suite
or post-deployment check was repeated. The API and database were unchanged.
