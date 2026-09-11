# Current ELD Cycle anchor — September 10, 2026

## Scope and diagnosis

Truck 11005's live Samsara Cycle was approximately 50:02:38 while summing its
continuous duty history gave approximately 49:29:32. Repeated read-only checks
found the same 1,985-second difference. History was fresh, covered sixteen days,
had no unsupported statuses, conflicting overlaps or material gaps, and its
cycle restart matched Samsara's reported cycle start. The reason those two
provider views diverged was not established.

The previous fifteen-minute reconciliation gate discarded all signed cycle
forecasts when totals differed. The user confirmed current ELD Cycle as the
authoritative starting value.

## Changes

- Application's shared cycle ledger anchors to the current ELD value, including
  seconds, and subtracts projected driving and on-duty work across the load chain.
- Continuous, fresh history and supported rules remain required for signed
  feasibility. Missing/stale history and jurisdiction changes remain unknown.
- Recap verification is separate. Unreconciled history grants no historical
  credits or recap date; it does not erase a usable ELD starting balance.
- Reconciled credits are bounded by ELD-used hours. A small discrepancy cannot
  double-credit hours, erase projected duty, or remain after its home-day window.
- Explicit completed restarts establish a full-cycle anchor without erasing
  earlier driving shortages. Ordinary forecast waits do not assume a restart.
- Chain policy 8 invalidates older snapshots through normal refresh. No API/Client
  DTO, database schema, provider request or UI style changes were needed.

Maintained rules: [ETA](../../features/eta-service.md) and
[HOS planning](../../features/hos-eta-planning.md).

## Checks and local runtime

- Affected routing/fleet/fuel/dispatch checks passed during iteration.
- Final `bash test.sh all`: 1,306 Server, 596 Client C#, and 376 JavaScript tests
  passed; 2,278 total, with no skipped tests in that run.
- Strict Release API publish passed into the clean local
  `artifacts/cycle-anchor-runtime.BQYN2K/publish-final` directory.
- A fresh bounded, read-only provider diagnostic confirmed an anchored Cycle of
  3,002.6257667 minutes, signed feasibility available, and recap unverified.
- Local API was restarted on port 5086 with existing Development settings and
  migrations, synchronization and Gmail maintenance disabled. Liveness returned
  HTTP 200. The existing staged Client on port 5067 was left unchanged.
- Actual authenticated Fleet Map displayed Cycle 50:02 and current-load arrival
  balance +34h 25m. Dispatch showed the current and both following loads without
  `Cycle unknown`; AMF1379 pickup details displayed +32h 09m. These are observations
  at verification time, not fixed expected future values.

No production deployment or migrations were performed. No isolated PostgreSQL
execution fixture was available, so those checks were not run; scoped application
database reads were diagnostics, not database tests. Production performance and
provider-side synchronization behavior were not measured. Existing browser design
scenarios were not rerun because this change did not alter Client code or assets.
