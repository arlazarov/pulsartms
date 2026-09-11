# Verified stop coordinate repair — 2026-09-08

## Scope and cause

Trucks 54777 and 11006 had city-level coordinates on ten stops belonging to
active loads 1358, 1372, 1373, 1375 and 1376. The stops still carried address
verification timestamps, so normal verification treated them as valid.
Cancelled load 1367 was excluded.

This is consistent with the previously documented import bug that overwrote
coordinates without clearing verification. The current mapper preserves verified
coordinates for unchanged source addresses. No historical write audit was
available to attribute each corrupted value to a specific import execution.

## Repair

Before mutation, scoped stops, route plans, base routes, deadheads and financial
snapshots were exported to a private local backup under `artifacts/`.
A guarded transaction cleared only verification and retry timestamps for exactly
ten stops, requiring their previous coordinates, source address and verification
timestamp to match the backup. No coordinates were guessed or routes deleted.

The existing background address verifier resolved all ten street addresses and
persisted new coordinates. Normal route preparation rebuilt the affected base
routes and deadheads and refreshed the saved financial snapshots. This operation
used the application's configured provider budget, without bypassing it.

## Verification

- Before/after comparison: all ten points changed and received fresh verification;
  original source address JSON remained unchanged and retry timestamps are clear.
- All six stops in the three stored current route plans match the verified DB
  coordinates exactly. Future loads without a current plan use prepared base routes.
- All five base routes, five deadheads and five rate snapshots were refreshed.
  Saved empty miles match the respective deadheads; deadhead errors are clear.
- Regression coverage now supplies non-null imported city coordinates and proves
  unchanged imports preserve the verified point, while changed source addresses
  invalidate verification and accept the new source coordinates.
- `bash test.sh addresses synchronization`: 367 passed (311 Server, 38 Client C#,
  18 JavaScript architecture checks). This was a targeted run, not the full suite.
- The local Fleet Map loaded successfully after refresh. Exact visual placement
  of every marker was not independently inspected in the browser.

The repair is applied to the live database. No production source change, schema
migration or new deployment was needed. Live reads were scoped operational checks,
not use of the application database as a test fixture.
