# Saved-route fuel planning follow-up — September 9, 2026

## Scope

Version 21 follows the user's request to stop recalculating roads during fuel
selection. It supersedes the routing strategy, not the historical measurements,
in the [per-leg verification record](fuel-leg-search-2026-09-09.md).
Current behavior is defined in the [fuel rules](../../features/fuel-planning-rules.md).

Manual fuel calculation now reads compatible saved current, base and deadhead
geometry for all remaining assignments. It does not call routing or geocoding
providers, repair roads or advance route tracking. Normal route preparation is
unchanged. Missing required saved geometry retains the prior result rather than
inventing a mandatory connection.

Nearby station access is estimated separately from saved road mileage. The
estimate contributes fuel, time and schedule cost, but is not a verified truck
approach or guaranteed upper bound. Selection retains reserve, bridge purchases,
terminal fuel value and the $20 stop cost. New snapshots store the unchanged
baseline and explicit access allowances, not a synthetic checked route.

Full purchases now reach the exact configured tank target, including fractional
gallons. Partial bridge purchases remain partial. Fuel numbers are shown inside
popup cards only; paired gauges, price colors, selection rings and numbered load
stops remain.

## Live local observation

The first provider-free calculation for truck 54777 found its GPS position about
0.224 geographic mile from the saved road, beyond the old 0.05-mile trim gate.
A scoped read-only diagnostic established that mismatch. Fuel planning now permits
the same two-mile nearby boundary for the starting position and charges a separate
one-way access allowance without moving the saved road or loosening mandatory-stop
continuity checks. Regression checks cover consuming that allowance exactly once.

After rebuilding the local API, one explicit calculation completed in
**5,290.665 ms**, trace `5c020acc2ad7ad9e21d0af98252d0340`. The LOVES #706 popup
showed arrival 13% / 27 US gal, purchase 184 US gal, and departure **100% / 211
US gal**, with quantities rounded for display. Its visit number appeared only
inside the card. This is one live application observation, not an isolated
benchmark or proof of globally optimal station selection.

## Checks and limitations

- `bash test.sh all`: **1,164 Server, 485 Client C#, 221 JavaScript tests**,
  **1,870 total**, all passed. The suite includes architecture, provider-free
  horizon/entry-point, origin access, exact fill, projection, persistence and
  bounded fuel-memory regressions.
- API Release and final Client builds with warnings treated as errors passed
  with zero warnings and errors. Both local services were restarted.
- The offline browser report
  `Client/test-results/fuel-no-floating-order-2026-09-09/report.json` recorded
  40 cases with zero failures, browser errors or blocked requests. It covers
  selectable station rings, absent floating fuel numbers and retained popup
  numbers/gauges. Representative mobile and map screenshots were inspected.
- After the final Client restart, HTTP integrity matched for 20 generated
  JavaScript assets with identity and gzip requests: 40 checks passed.

No production deployment or migration was performed. PostgreSQL fixture checks
were not run because no safe isolated fixture was available. Live application
operations and read-only diagnostics were not used as substitute database tests.
No new retained-heap, peak-memory, sustained-load or production-latency measurement
was made; passing tests and removal of fuel routing calls do not prove a memory
ceiling, universally accessible station approaches or full visual correctness.
