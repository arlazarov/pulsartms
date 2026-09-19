# Durable on-demand route refresh

Date: September 17, 2026. Local implementation and isolated verification only.
The [core specification](../../architecture/core-rebuild.md) remains the
authoritative unfinished plan. No deployment or working-database migration
was performed.

## Behavior and ownership

PlanningRefreshRequests replaces the bounded in-process channel for on-demand
route refresh. Application owns demand identity, cooldowns, retry policy and
worker orchestration; Infrastructure implements IPlanningRefreshStore and
atomic PostgreSQL claims. No provider call occurs inside a claim transaction.

Requests coalesce by load, optional execution leg and assignment revision.
Captured work, complete effective profile and route choice identify the input.
Changed input advances a durable version while preserving an existing lease.
An older completion acknowledges only its captured version; newer demand
remains immediately eligible. Repeated demand preserves failure backoff.
Successful cooldown and abandoned-lease recovery survive process replacement.

An idle worker polls every five seconds. A coalesced local signal can wake it
sooner but owns no requests. A five-second memory memo coalesces demand writes;
the database remains authoritative. Concurrency stays bounded by the existing
PlanningConcurrency setting. Removed/completed/superseded assignments finish
without provider work. Completed request state is pruned after seven days.

Provider error reuse now includes captured work, complete profile and route
choice. A changed address, assignment, dimension or route choice cannot inherit
an error solely because it shares the previous load ID and fuel preferences.
Preview reads remain free of writes. Normal planning reads persist demand
before reporting it queued, and retain the saved route during recalculation.

## Migration correction

An isolated PostgreSQL run showed that EF can commit an individual migration's
downgrade before a preceding migration rejects its own downgrade. The temporary
additive queue migration was never applied to the working database. Its schema
and generated model were consolidated into the existing unapplied
`20260917055902_RebuildExecutionStorage` transition and the temporary migration
was removed. One guard now checks pending requests and accepted execution before
any tables are deleted. The final fixture verified unchanged migration history
after both rejected paths.

## Verification

- Full `bash test.sh all`: 2,585 Server tests, 1,012 Client C# tests and 561
  JavaScript tests passed: 4,158 total, no failures or skips.
- CoreMigrationProbe strict build passed with no warnings or errors.
- Real PostgreSQL verified concurrent request coalescing, claims around locked
  rows, changed input during calculation, retry/cooldown persistence, lease
  recovery and downgrade protection. The complete clean migration, protected
  identity/password/roles, accepted visits and immutable history passed too.
- Fixture `pulsr_core_fixture_95f263e8deef4426a5c4539d3514e25f` was removed.
- The read-only preview assertion remains active around preview calls. The
  subsequent planning read explicitly verifies one durable request and unchanged
  saved route JSON. Warm reads still reuse cached demand without repeated queries.

Pinned evidence: `artifacts/managed/diagnostic-1swE05/tests.log` and
`artifacts/managed/diagnostic-sbK6OF/postgresql.log`. The earlier additive-schema
experiment in `diagnostic-6BF7fm` failed the combined downgrade-history check;
it is not a passing migration result. Its fixture was also removed.

No new release gate, authenticated browser run or production performance
measurement was performed. The source-road RoutePreparationQueue still keeps
its repair state in memory; native execution has its separate durable outbox.
Unified ordinary-import acceptance, remaining calculation adapters, the complete
per-truck writer contract and the compatible working-database cutover remain
open. This change does not complete the full rebuild or the entire delivery gate.
