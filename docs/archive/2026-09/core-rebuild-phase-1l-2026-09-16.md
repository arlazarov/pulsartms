# Core rebuild phase 1L: historical inputs at ETA and fuel publication

Date: 2026-09-16. Status: implemented locally; full automated suite passed. No
deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

Phase 1K captured historical work and protected deadhead publication. ETA
still finished its historical check before entering the final write
transaction. Fuel validated a connection while reading saved geometry but
discarded the historical capture before committing its result. A
completed-load edit or new unknown-start predecessor could therefore escape
the remaining-work signature check.

PlanningWorkPublication now validates the complete itinerary and its
historical dependencies inside the same owned transaction. It checks current
work first, then repeats predecessor selection from the captured current
facts. A mismatch rejects publication before profile or result writes and
releases the transaction. The existing ten-table protection remains unchanged.
No provider call or nested publication transaction is introduced.

Historical dependencies preserve their original lookup batches. Completed
native leg hydration shares candidate and saved-leg references within each
batch; flattening independent lookups can produce different predecessor
evidence even without a source mutation. DeadheadHistoryBatch retains that
context explicitly. This replay validates predecessors after current work has
been validated; it does not independently certify arbitrary supplied current
facts.

EtaChainDescription retains immutable history and includes its signatures in
calculation InputHash. Policy version 14 rejects older forecast signatures
through normal reads. GeometryHash remains separate, so a historical revision
can invalidate ETA without recompiling unchanged road timing. Missing and
ambiguous connections remain dependencies. Final publication rejects
historical changes after the earlier full-description check, preserves stored
forecasts and removes the unpublished memory result.

DeadheadService.CaptureRouteAsync returns saved geometry with the history used
to select its predecessor. FuelHorizon retains these inputs for legacy
connections. Native connections derived directly from captured work retain the
canonical itinerary guard. FuelRegionPlanner returns its arrival policy with
any onward-road dependency, including the conservative unknown-exit path.
Automatic search, manual save and reset pass horizon and arrival dependencies
to the shared fuel commit operation before writing the profile or either fuel
copy.

No public HTTP fields, Client files, financial formulas, predecessor ordering
rules or database schema changed. Existing unrelated working-copy changes were
preserved. These captures are transient server inputs, not accounting
evidence.

## Verification

Added 19 server cases covering:

- Historical endpoint, completion revision, cancellation, unknown-start and
  inserted-work changes immediately before ETA or fuel publication.
- Unchanged remaining-work signatures alongside changed historical evidence.
- Preserved forecasts, requested profile and both previous fuel copies.
- ETA memory cleanup, unavailable-history validation and unchanged-history
  success using one publication scope.
- Historical input hashing with compiled geometry timing reuse.
- Onward arrival-policy dependencies and native/captured-work connections.
- Canonical and historical reads sharing the owned transaction.
- Revalidation preserving independent native-history lookup batches.

The existing architecture check now verifies that historical validation
follows canonical validation and precedes returning the result transaction.
Source/lock closure remains exact; no exception or weakened boundary was
added.

Initial checks identified constructor/return-type adaptations and incomplete
test fixtures: a missing history-reader registration, unavailable fake
telemetry and duplicate fixture load numbers. These were corrected using
existing shared test services. No production fallback or skipped test was
added.

Affected `bash test.sh routing fuel dispatch` passed:

- Server.Tests: 2,083 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed.

Required final `bash test.sh all` passed:

- Server.Tests: 2,371 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,944; no failures or skipped tests.

The stage changes 30 source, test and documentation files. Pinned CSharpier
validation covers 23 C# files. Whitespace and local documentation links are
checked separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-Ajn2rc/baseline.json`: pre-edit hashes for
  1,974 files.
- `artifacts/managed/diagnostic-S1STNK/affected-1.log`: compilation feedback.
- `artifacts/managed/diagnostic-UKbpv0/affected-2.log`: fixture constructor
  feedback.
- `artifacts/managed/diagnostic-HQqKn5/affected-3.log`: test-fixture feedback.
- `artifacts/managed/diagnostic-e6v276/affected-4.log`: affected checks passed.
- `artifacts/managed/diagnostic-xZVgC6/full-1.log`: full suite passed.
- `artifacts/managed/diagnostic-TOBPPm`: formatting/whitespace checks and the
  pre-report file inventory.
- `artifacts/managed/diagnostic-nnkmJj/audit.json`: final 30-file inventory,
  successful formatting/whitespace checks and 137 local documentation links.

## Limits and next slice

This protects the historical dependencies carried from calculation to commit.
It does not add a persisted historical token to fuel display or
automatic-refresh decisions after a valid commit. Existing saved-plan
validation remains in place. Saved-road versions, settings/rate writes and
external telemetry/price observations still have separate consistency
policies. The earlier ETA description check is retained for those other
inputs.

The broad publication lock remains a transitional mechanism. More validation
reads run inside it; production throughput and latency have not been measured.
Narrowing the lock still requires complete source-writer revision ownership.
The next slice should define settings/rate and saved-road ownership, including
fuel refresh after historical corrections. Durable accounting and driver
settlement evidence remain later work.

PostgreSQL execution/lock contention, live providers and authenticated browser
checks were not run. No suitable isolated PostgreSQL fixture was used; no
local database server was started or installed. SQLite tests do not certify
PostgreSQL behavior. No migration was introduced or applied, and no deployment
was made.
