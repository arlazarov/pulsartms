# Shared execution-stop acceptance

Date: 2026-09-17. Local implementation only. This is part of gate 1 in the
[core rebuild specification](../../architecture/core-rebuild.md), not completion
of ordinary import bootstrap, the core rebuild or the production transition.

## Implemented

ExecutionStopAcceptance now owns accepted stop changes to existing execution
legs for source synchronization, explicit source acceptance and the load editor.
Previously these paths separately replaced rows, advanced revisions and wrote
history and planning demand; only the manual paths superseded planned mileage.

The shared operation requires its caller's transaction, checks leg revisions
and protected movement paths before changing accepted stops, updates load-link
boundaries when requested, advances each changed leg once, writes immutable
history and enqueues durable planning demand. The surrounding command retains
authorization, input validation, idempotency and transaction commit ownership.
System synchronization retains a null history actor; manual changes retain the
actor and retry correlation. Transfer and status/resource corrections retain
their existing writers and remain outside this stop-edit operation.

An inserted or moved stop now retires obsolete automatic planned mileage in the
same transaction as acceptance. Path comparison uses ordered occurrence IDs;
repeated facilities stay distinct. It checks the whole interval between movement
anchors, including interior visits and entry/exit connections. Unaffected
adjacent intervals remain intact. Appointment and actual-time changes alone do
not change the physical path.

Recorded movement, actual distance, manual overrides and user-recorded mileage
decisions protect the affected path. Import retains the accepted itinerary and
records a review reason instead of replacing that evidence. Identical reimport
does not repeat the accepted revision, history, queue request or supersession.
Evidence without usable anchors cannot establish an unaffected interval.
Explicit acceptance rechecks protection after preview and before mutation.

Source review fingerprints now include header resource IDs, their display
identities and unmatched stop resource names. Changing an assignment while a
review is open invalidates that review. This does not implement resource
proposal acceptance or infer transfer confirmation.

Actual-time validation no longer substitutes the recording timestamp for a
missing assignment start. A known earlier actual may be accepted when the start
is unknown; known start bounds, future-time rejection and transfer confirmation
rules remain enforced.

## Verification

- `bash test.sh synchronization routing` passed during iteration, including
  architecture checks. This was a category run, not the full suite.
- Final `bash test.sh all`: 2,615 Server, 1,012 Client C# and 561 JavaScript
  tests passed, with no failures or skips: 4,188 total, 30 more than the
  previous durable-refresh slice. Evidence:
  `artifacts/managed/diagnostic-E9uK6v/tests.log`.
- CoreMigrationProbe strict build passed with zero warnings/errors. Evidence:
  `artifacts/managed/diagnostic-Hpyj2Z/build.log`.
- The PostgreSQL fixture verified both acceptance paths, obsolete planned
  mileage, replay, history actors and atomic rollback. It also reran identity
  retention, the clean transition, immutable-history and downgrade guards, and
  both durable queues. The uniquely named fixture database was removed.
  Evidence: `artifacts/managed/diagnostic-a6k7z7/postgresql.log`.
- CSharpier checked all 14 maintained C# files touched by this slice;
  `git diff --check` passed.

New checks cover source insertion and replay, protected actual/manual mileage,
preservation of observed neighbouring paths, transactional rollback, actor and
queue revision consistency, protection appearing after preview, resource changes
during source review, unknown start time, repeated occurrences and changed path
boundaries. An initial new source-review fixture omitted valid coordinates and
was corrected; no production validation or existing test was weakened.

The PostgreSQL acceptance probe explicitly seeds an execution leg. Its success
does not establish ordinary import bootstrap, live provider integration or
production concurrency/throughput. Evidence logs are pinned in managed runs.

## Remaining scope

Ordinary source-only loads still read imported DispatchStops; this slice does
not create their initial ExecutionLegs. Common assignment bootstrap must account
for unassigned work, driver-only prefixes, future assignments, completed work
and unresolved resource changes. Planned ordinary work must not accidentally
inherit the current assumption that a planned native leg awaits a transfer.

Full resource proposal/baseline ownership, transfer and correction writer
unification, historical assignment/movement linkage, remaining calculation
adapters and background delivery work remain in the maintained plan. No actual
fuel/toll import, accounting or compensation implementation was added.

No schema change was introduced here. The existing
`20260917055902_RebuildExecutionStorage` migration remains unapplied to the
working database. No working-data cleanup, deployment or image publication was
performed. The release gate and authenticated browser flows were not rerun for
this slice; production performance and visual correctness were not measured.
