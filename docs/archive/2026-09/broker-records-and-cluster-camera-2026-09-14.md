# Broker records, contextual transfers and cluster camera

## Implementation

- Customer master profiles reuse the existing normalized-name matcher and unique
  identity index. Explicit bounded search and revision-protected default saves
  are separate from per-load snapshot saves.
- Regular billing and Quick Pay contacts/terms remain visible. No emails,
  payments or automatic Quick Pay fees are sent or calculated.
- Stable broker/driver adjustments carry recorded currencies and reasons.
  Driver IDs and name snapshots are checked server-side. Driver adjustments
  do not calculate payroll. Broker totals are server-owned; mixed currencies
  produce a scoped unavailable total, never an implicit conversion.
- Selected-stop Add stop offers contextual Pickup, Delivery and transfer
  actions. Locked selections cannot silently use a different insertion anchor.
  The existing transfer planner keeps explicit location/confirmation semantics.
- Selected-stop facts use content-sized resource groups. Broker contacts and
  terms share a two-column desktop panel; billing and adjustments follow below.
- Truck-group clicks fit only member positions, respect inspector insets and
  release Follow/pending initial fits without clearing truck selection.

## Final checks

- `bash test.sh all`: 977 Client C#, 1,890 Server and 544 JavaScript tests passed
  (3,411 total), including architecture checks.
- Strict Client publish passed under `artifacts/broker-preview`. This is a local
  verification artifact, not a deployed release. The SDK reported that wasm-tools
  was not installed; no AOT/performance improvement is claimed.
- Styles compiled successfully. Relevant source diffs passed whitespace checks.
- EF model check after rebuilding: no pending model changes.
- Dispatch browser matrix: 1440/390/320 widths, light/dark, 100% and 200% text,
  eight cases, no browser errors or unexpected requests. It exercises contextual
  transfer selection, preserved drafts, billing fields and driver records.
  Report: `artifacts/managed/browser-ui-bWZxib/report.json`.
- Actual offline GPU marker fixture: DPR 1/2, both passed, no errors. Camera
  bounds exclude other trucks and route geometry and preserve the selected truck.
  Report: `artifacts/managed/browser-map-markers-bAT4lP/report.json`.
- The marker pixel assertion samples card background around its geographic
  center rather than a tiny center region potentially covered by white glyphs.
  The final requirement is at least 100 dark pixels per squared DPR within
  a centered 20-by-20 CSS-pixel region.

An earlier full run overlapped a publish and failed existing allocation and
batch-refresh checks. Subsequent complete runs passed; their thresholds were
not relaxed. This is not a claim that those checks are immune to flakiness.

## Not performed

- Migration `20260914204235_AddBrokerProfiles` was generated, not applied.
  It adds only Customer profile JSON and revision columns.
- No server deployment or business-data writes. The local API uses the shared
  application database, so it was not restarted with the new schema requirement.
  Coordinate migration, API and Client rollout; an older API ignores the new
  metadata fields.
- PostgreSQL execution was not tested: no approved isolated PostgreSQL fixture
  was available. Database integration tests used existing isolated SQLite
  fixtures, never application/production data.
- Browser fixtures intercept APIs and do not validate live Google Maps,
  production broker records, authentication or external providers.
- No production memory/latency measurement or full per-stop map/mileage feature
  was added. The existing mileage section was moved into the Overview sidebar.
