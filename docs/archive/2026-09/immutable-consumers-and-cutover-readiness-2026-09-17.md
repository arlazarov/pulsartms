# Immutable consumers and core cutover readiness

Date: September 17, 2026 (America/Toronto).

This record follows the shared execution owner, durable queues, scoped writer
revisions and immutable base/history slices. It records local implementation and
verification. The working database reset, compatible deployment and post-cutover
acceptance remain separate work; no production change is claimed here.

## Removed transitional paths

- Deleted ExecutionRouteProjection and ExecutionWorkProjection. The former
  reconstructed mutable Dispatch objects from accepted work; the latter made
  core selection depend on screen DTOs.
- Native reads now return ExecutionLoadSnapshot: immutable RouteWorkSnapshot
  facts and separate immutable commercial/display labels. ExecutionWorkReader
  creates immutable selection directly. Dispatch responses are projected at the
  display boundary.
- ETA descriptions, live routes, previews, choices, progress and fuel horizons
  retain immutable work through asynchronous calls. Base/history consumers
  already use the same contract. Tests now exercise these actual contracts;
  no production adapter remains solely to support a test fixture.
- Domain TruckPath, StopOperation and StopCompletion own shared pure policies.
  Synchronous sequence/fuel dependencies use data-only IWorkFacts contracts and
  no longer depend on Dispatch screen types. Source entities remain valid edit
  and persistence inputs, captured before asynchronous calculations.
- Appointment windows, time zones, actual arrivals, resource IDs, completion
  revisions, commodity and notes survive capture. Native handoff readiness and
  confirmed actions with unknown actual time retain their meaning.

A real projection defect was corrected: accepted Hook/Loaded had been rewritten
as Hook/Unknown by source-operation inference during response construction.
The screen now retains accepted cargo state, matching the immutable calculation.
A regression covers immutable rich metadata, later source/display edits and
unknown actual times. Saved native fuel signatures based on the incorrect state
become stale; the planned clean reset removes those old operational results.
Fuel optimization and ETA timing policies were not changed.

Architecture checks now prevent the two removed bridges from returning, forbid
Dispatch screen models in core selection and verify immutable native/ETA
contracts. Existing layer boundaries were not weakened.

## Historical actors

The final scenario audit identified a remaining C15 defect: document listing
joined the current Users table. Deleting an uploader hid its file from the list;
a rename changed the displayed historical author and successful retry response.

Documents now persist the author's name at upload, and list/retry responses use
that captured value without joining the current account. Content remains scoped
to its load and existing access checks still apply to the current caller. Tests
cover rename, disabled account and deletion, metadata-only reads, download and
stable retry output. Execution revision JSON now also captures its actor label;
account rename/deletion cannot rewrite the accepted record. System operations
retain a null actor rather than inventing one.

The document column is consolidated into the same unapplied
`20260917055902_RebuildExecutionStorage` migration. Applied migrations remain
unchanged.
Activity and workspace history already store actor labels independently.

## Plan audit

- Accepted work: imports, explicit assignment, edits, review and transfers share
  acceptance/history/demand ownership. Ambiguous source facts remain reviewable;
  the import invents no assignment or actual time.
- Custody and mileage: transfer boundaries, unknown time, independent actions,
  observed/planned distance, allocations and immutable corrections are covered.
  No new compensation agreement or multi-load editing policy is invented.
- Calculation inputs: mutable route and screen bridges are removed. Shared
  capture and fresh publication checks cover Route/ETA/Fuel. Native successor
  ETA limits remain explicit existing product policy.
- Durable work: persisted demand, lease/version ownership and scoped PostgreSQL
  writer coordination are implemented and rehearsed. Production throughput and
  contention remain unmeasured.
- Reset and cutover: exact inventory, guarded SQL, protected backup and sequence
  are prepared. Working cleanup, migration, API/Client cutover and live
  acceptance are not performed.

Source-only review and explicit transfer bootstrap are entry points into the
common owner. They are not competing accepted stores. Provider-specific load
mapping and network calls stay in Infrastructure; the integration credential
catalog still legitimately names its supported providers. TorqueAI remains an
optional configured import adapter, with native load creation and numbering.
Disabling its adapter is not part of this cutover and does not require removing
shared execution, routing or finance policies.

Accounting, driver compensation, owner/operator agreements, toll estimates and
actual fuel/toll imports remain the user's future extensions. Their financial
ownership and evidence contracts stay in the core specification; no speculative
financial schema was added here.

## Working database preparation

A read-only inventory confirmed the configured working database has 60 public
tables, 39 applied migrations and the expected September 14 starting schema.
The 49 operational tables exactly match the reviewed model/legacy inventory;
there are no unknown tables. Eleven tables are protected, including migration
history. The inventory contains only table names/counts and migration IDs.

The maintained `scripts/sql/reset-core-storage.sql` contains the explicit list,
requires exact target/schema acknowledgement plus stopped-writer and backup
checks, rejects another database client or unexpected table, and takes bounded
exclusive locks. It truncates without CASCADE and compares protected rows inside
the transaction. It is rehearsed only on the synthetic isolated fixture.

A full private custom-format preflight backup was created outside disposable
artifacts. Its index contains all ten identity/configuration data tables;
full archive decoding and its SHA-256 were verified. The backup is 231,375,842
bytes, with private directory/file permissions. A database restore was not
performed. Writers were still live, so a fresh backup after draining them is
required by the final cutover procedure.

The read-only Cloud Run inspection could not refresh the current credential:
`Reauthentication failed; cannot prompt during non-interactive execution`.
The user was asked to repeat `gcloud auth login`. No Cloud Run configuration,
traffic, scaling, deployment credentials or database data was changed.

## Evidence

Repository-root-relative paths below identify pinned managed outputs.

- Full suite after historical-author changes:
  `artifacts/managed/diagnostic-6JrLS0/tests.log`.
  4,300 checks: 2,724 Server, 1,015 Client and 561 JavaScript; no failures.
- Earlier immutable-consumer release gate:
  `artifacts/managed/diagnostic-Qlp3Q5/release.log`.
  4,295 checks, zero build warnings/errors; 273 published assets and seven
  JavaScript graphs verified. Later author changes require the final gate below.
- Dispatch workspace on that unchanged Client source:
  `artifacts/managed/browser-ui-t8nnFE/report.json`.
  Eleven scenarios, zero failures, browser errors or unexpected requests.
- General offline UI:
  `artifacts/managed/browser-ui-IlbioR/report.json`.
  Twelve layout matrices, zero failures/errors/unexpected requests. Synthetic
  Synthetic authentication does not prove live sign-in, provider behavior or
  complete visual correctness.
- Read-only working inventory:
  `artifacts/managed/diagnostic-m3OP4c/inventory.json`.
- Private backup manifest and complete archive-decoding result:
  `artifacts/managed/diagnostic-jfJqIB/backup.json`.

- Final strict PostgreSQL build and complete isolated rehearsal:
  `artifacts/managed/diagnostic-T5ZHob/build.log` and `postgresql.log`.
  No build warnings/errors. The reviewed reset script, all migrations, source
  bootstrap/replay, accepted history, transfers, queues, writer revisions and
  publication concurrency passed. The synthetic fixture was removed.

- Final strict release gate with offline UI:
  `artifacts/managed/diagnostic-1w0rl7/release.log`.
  All 4,300 tests passed with zero build warnings/errors. Verified 273 published
  assets and seven JavaScript graphs. The exact staged Client is
  `artifacts/managed/release-HBn24c/publish/wwwroot`.
- Final offline UI report:
  `artifacts/managed/browser-ui-JUihQD/report.json`.
  Twelve matrices/52 page checks, zero failures, browser errors or unexpected
  requests. The workspace scenarios above use the same unchanged Client source.
- Pinned CSharpier verification passed for all 172 changed maintained C# files.
  Repository whitespace checks passed. No generated model was hand-formatted.

The local implementation, full verification and transition preparation are
complete. Production release is pending. Live sign-in, provider bootstrap and
planning after the working migration have not been checked. Cloud Run access
must be restored and deployment explicitly approved before that final phase.
