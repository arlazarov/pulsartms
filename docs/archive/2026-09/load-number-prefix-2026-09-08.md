# Configurable load-number prefix — September 8, 2026

Removed hardcoded `AMF`/`#` differences between current and future loads. Settings
now has an independent load-number form with custom and explicitly empty prefixes.
All Dispatch views and current/future map references use the same display formatter.
Numeric identifiers, order numbers, copy behavior, URLs, searches, financial values
and route/ETA inputs are unchanged. Prefix text is rendered as text, not HTML.

The authenticated layout reads one small setting for its child labels. The Admin
form fetches its own current revision before editing; saved state cascades back to
the layout, and older reads cannot undo a newer save. Prefix-only updates do not
reselect a dismissed truck or send route geometry; only map reference text changes.

Server read/write handlers use a dedicated DispatchSettings table with revision
concurrency. Stale updates and competing first inserts return 409. Invalid/null
input is rejected; default `AMF` is used only when there is no settings row, never
as a fallback for an explicitly saved empty prefix. Authenticated GET and Admin PUT
are separate from fuel preferences and their invalidation mechanisms.

## Verification

- `bash test.sh dispatch fleet styles`: 412 Server, 279 Client, 122 map JavaScript,
  12 style and 18 JavaScript architecture tests passed during iteration.
- Final `bash test.sh all`: 595 Server + 300 Client + 158 Node = 1,053 passed,
  including architecture. New coverage includes empty/custom formatting, safe text,
  draft retention, live cascade updates, latest-save ownership, scoped requests,
  settings persistence, input validation and competing writes.
- Strict Client publish, typed JavaScript, JavaScript/style builds and artifact
  verification passed: 222 assets and six dependency graphs. Exact stage:
  `artifacts/load-prefix.3zMSkx/publish/wwwroot`.
- Offline browser checks passed 44 page cases plus 20 detail crops, five future-stop
  scenarios, four GPU scenes, eight current-stop popup cases and two constrained-
  height cases. Checked custom/blank previews and independent Settings forms;
  inspected desktop/mobile Settings images. No checked clipping failures,
  browser errors or unexpected requests. Reports are under
  `Client/test-results/load-prefix-ui`, `load-prefix-stop-details` and
  `load-prefix-stop-cards`.
- Migration/model consistency passed. Read-only inspection confirmed that
  `20260908223826_AddDispatchSettings` was the only pending migration. Its reviewed
  additive Up migration was applied to the configured application database:
  one new settings table and the migration-history entry, with no existing load
  records changed. No local database server/container was started.

No production application deployment was performed. Isolated PostgreSQL fixture
tests and authenticated browser saves against the application database were not
run; the migration application is not a substitute for those checks. No provider
calls or production performance measurements were added for this display feature.
