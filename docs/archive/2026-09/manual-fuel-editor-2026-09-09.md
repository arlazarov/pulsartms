# Unified manual fuel editor — September 9, 2026

## Scope

The map editor updates the existing truck-owned fuel plan across all remaining
assigned loads. Station cards support add/edit and distinct return visits. Ordered
rows support removal, ten-US-gallon partial quantities and an exact full-tank target.
Arrival/departure gauges and costs use server replay values. No history storage,
BVD purchase matching, database migration or new routing-provider request was added.

## Review corrections

- Atomic compare-and-replace protects the opened snapshot timestamp, including
  an automatic search started before a competing manual save. The compatibility
  route copy and current truck snapshot remain in one transaction.
- Manual plans require explicit reset before automatic replacement. Preview and
  rejected saves do not mutate either saved copy.
- Stale, removed and unpriced stations remain editable. Unresolved drafts omit
  calculated values; unsafe numeric drafts retain their values with save disabled.
- Only a verified identical remaining road may trim passed purchases on editor
  initialization. Changed geometry cannot silently discard planned visits.
- Remaining cost projection charges remaining access driving, but not historical
  waiting. Manual save includes additional schedule delay without double-counting
  already-priced access driving.
- Save/reset reject late polling responses and update planning-cache aliases.
  Selection changes cancel draft requests. Hidden future visits do not turn an
  explicit Add action into selection of an existing occurrence.

## Verification boundaries

Storage and transactional checks use isolated in-memory SQLite. PostgreSQL execution
was not run because no approved isolated fixture was available. SQLite does not
prove PostgreSQL runtime behavior. Browser editor scenarios use the real staged
Blazor components with intercepted deterministic APIs and map callbacks; they do
not exercise live providers, real financial calculations or business-record writes.
No production performance or memory-leak claim is made.

## Results

- `bash test.sh all`: 1,253 server, 504 Client C# and 254 Node tests passed,
  with no failures or skips (2,011 total).
- `bash verify-release.sh`: the same full set passed in the release gate;
  strict Release builds had zero warnings/errors. Integrity verification checked
  231 assets and six JavaScript entry-point dependency graphs.
- Final Client artifact: `artifacts/release.EFbxF0/publish/wwwroot`.
- Existing GPU/stop-card smoke: 40 scenarios passed, with no reported browser
  errors, blocked requests or layout failures.
- Exact final-artifact fuel editor smoke: four scenarios passed at desktop/mobile
  widths in both themes, with eight screenshots visually inspected. Totals and
  validation remain visible above the editor controls; overflow scrolls within
  the content area while the footer remains accessible. No browser errors or
  unexpected requests were reported. Evidence is local under
  `Client/test-results/manual-fuel-final-polished/report.json`.
- Local Client/API restarted on ports 5067/5086 with migration, synchronization
  and Gmail background maintenance disabled. An authenticated read-only check of
  truck 54777 opened its editable saved visits; the draft was cancelled without
  saving. The unavailable-visit repair state displayed without discarding rows.
- Production deployment was not performed. No migration is required or pending
  for this feature.

Logs are local under `artifacts/manual-fuel-all-tests.log`,
`artifacts/manual-fuel-release.log` and `artifacts/manual-fuel-stop-cards.log`.
