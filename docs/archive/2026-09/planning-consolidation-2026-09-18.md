# Planning consolidation — September 18, 2026

## Scope

Local implementation following the [cohesion review][review]. This work retains
accepted execution identities, assignment revisions, route publication guards
and optional provider adapters. No database reset, SQL migration or cloud release
is required by these additive JSON contracts.

## Implemented boundaries

- Fuel build/reset returns an explicit feasible, below-reserve feasible,
  unreachable-station or no-feasible-plan outcome. Direct and automatic entry
  points expose its status. Diagnostic publication does not throw after commit
  and does not publish fictional purchases or financial totals.
- Diagnostic quantities use the effective requested profile. Work, complete
  road dependencies and predecessor history are checked before publication and
  when displaying diagnostics. Fuel/GPS values are checked at publication;
  changed values invalidate an old diagnostic immediately. Repeated timestamps
  with unchanged values do not invalidate it. Existing snapshots remain stored
  for recovery, but a current diagnostic suppresses their financial display.
- Current-work capture uses saved completion proofs before resolving crew and
  HOS. Regression scenarios compare its selected leg and driver with ETA when
  an older active leg is complete and the next leg is planned. Profile/cache
  dependencies are resolved before the work-cache factory, avoiding recursive
  striped cache locks discovered during validation.
- Optional reference reconstruction retains prior geometry on provider failure
  or invalid anchoring. The operational route remains independent. Cancellation
  is preserved. Geometry explicitly represents estimated road, not recorded GPS.
- Application supplies segment cargo state and purpose. Fleet Map and Dispatch
  use the same classification. A later pickup without predecessor evidence stays
  unknown; Client code no longer infers empty travel from pickup labels.
- The first-purchase physical minimum is an explicit zero-gallon policy constant.
  Preferred reserve, post-purchase constraints and the existing regional terminal
  requirement keep their distinct meanings and existing behavior.

## Verification

- `bash test.sh all`: 2,798 Server, 1,032 Client C# and 569 JavaScript tests passed.
- JavaScript type checking and JavaScript/SCSS asset builds passed.
- Strict solution build passed with zero warnings and zero errors.
- Local API and Client restarted from the pinned managed build. Client returned
  HTTP 200; an authenticated read-only truck preview succeeded and returned the
  new segment classification and estimated-road provenance contract.
- Tests used isolated existing SQLite fixtures. PostgreSQL integration checks
  were not run; no disposable PostgreSQL fixture was used or created.
- Automated tests cover route rendering contracts and interactions. A complete
  visual browser audit and production performance benchmark were not performed.

The initial full runs exposed fixture registration, query-budget and cache
reentrancy issues. Those runs were not counted as passes. A bounded diagnostic
run identified the blocked tests; the final full run completed successfully.

## Limits

The legacy `FuelRecommendations` JSON field remains the storage/read container
for diagnostics; its new typed outcome and numeric values define the behavior.
This avoids an unrelated data migration. Older records without required input
stamps fail validation. Technical failures and invalid command inputs still use
the application's existing error boundary.

Display reconstruction is still awaited during a route operation. Its expected
failures cannot discard the operational result, but this change does not promise
zero extra latency. Neither estimated context nor warning text is an accounting
fact. Accounting, imported actual expenses and toll integrations remain separate
future work.

[review]: planning-cohesion-review-2026-09-18.md
