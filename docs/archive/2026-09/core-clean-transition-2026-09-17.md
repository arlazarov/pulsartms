# Clean operational transition and transfer ownership

Date: September 17, 2026. Local implementation and verification only.
No working-database cleanup, migration or deployment was performed.

## Decision

The user permits discarding existing business data and requires users to
survive.
The current [core specification][core] replaces the preservation/backfill plan.
Earlier implementation details remain in the
[historical record][previous].

Preservation includes the full authentication boundary, not only Users:
AspNetUsers, application role claims, roles, external logins, tokens, profile
links, password hashes, security stamps, preferences and DataProtectionKeys.
IntegrationCredentialSettings remains protected because resetting work does not
require changing integrations. No credentials or protected values were printed.

## Implemented changes

- Accepted native stops use typed rows. Position owns ordering; source Sequence
  cannot reorder execution. Commercial-only edits do not create native revisions
  merely because the workspace numbers several legs consecutively.
- Ordinary and transfer stops share accepted row storage. Removed the separate
  ExecutionVisit entity/table and its duplicate location and confirmation data.
- SwitchParticipant owns release and receipt independently. Route, itinerary,
  workspace, source review, mileage and history read transfer facts through the
  common projection. A recorded release cannot confirm receipt; known schedules
  cannot supply missing actual time. Missing boundaries block planning.
- Accepted history is appended in the owning transaction. Retried commands keep
  one result. Database and EF guards reject rewriting accepted history.
- Activity references remain scoped to the selected load, including when a leg
  serves multiple loads. Another load's stop cannot become this load's activity
  reference merely because the assignments share a leg.

The four intermediate September 17 migrations were never applied to the working
database. They are replaced by one pending migration:
`20260917055902_RebuildExecutionStorage`.
It removes mutable stop JSON and old transfer-visit storage, creates typed stops
and immutable revisions, and requires an explicit prior operational reset.
There is no business-data backfill or artificial baseline revision. Existing
deployed migrations and identity schema are retained. Populated new execution
cannot be discarded by a downgrade.

## Verification

The final Release gate with offline UI smoke passed:

- 2,563 Server tests, 1,012 Client C# tests and 561 JavaScript tests:
  4,136 total,
  no failures or skips; strict build reported zero warnings and errors.
- 273 staged assets and seven JavaScript dependency graphs verified.
- 52 offline UI page cases passed with deterministic APIs and a stubbed map
  provider. This is not authenticated live-API or production visual evidence.
- CSharpier checked 90 maintained C# files changed since the task baseline;
  git diff whitespace checks passed. The task started with substantial existing
  changes, which were retained.

Release evidence: artifacts/managed/diagnostic-jtEYnO/release.log.
Staged Client: artifacts/managed/release-CxSXsJ/publish/wwwroot.
UI report: artifacts/managed/browser-ui-6HTJsR/report.json.
Change inventory: artifacts/managed/diagnostic-tCuQ7q/files.json.

The updated CoreMigrationProbe passed on a uniquely named isolated PostgreSQL
fixture hosted on an existing server. It started no SQL server/container and
removed its own fixture afterward. It verified:

1. Migration with old execution rejects without advancing migration history.
2. An explicit reset of 49 old operational tables preserves protected identity
   and configuration byte content through normalized database comparison.
3. The new schema matches the EF model. Existing password verification and
   effective Admin role lookup still work.
4. New accepted order, unknown actual time, independent transfer confirmation
   and transactional history persist through real PostgreSQL reads.
5. Direct SQL history update/delete and downgrade over new work are rejected.

PostgreSQL evidence: artifacts/managed/diagnostic-iWsh9z/postgresql.log.
The probe is restricted to isolated fixture databases and is not a production
reset command.

## Remaining work

This closes the clean-storage transition and duplicate transfer-fact removal,
not the full core rebuild. Ordinary source imports still need one accepted-work
owner with native edits. Remaining adapters, standalone inputs, durable work
ownership and per-truck publication revisions remain in the maintained plan.
Global publication locks remain conservative; production contention and
performance gains have not been measured.

Source bootstrap through the final unified owner, authenticated application
checks and a compatible approved cutover are still required. The pending
migration must not be applied while old API readers/writers are running.
Accounting, compensation agreements and actual fuel/toll imports remain later
product work, as requested.

[core]: ../../architecture/core-rebuild.md
[previous]: core-rebuild-implementation-record-2026-09-17.md
