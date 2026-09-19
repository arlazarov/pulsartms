# Core rebuild phase 1C: precedence and ETA readiness

Date: 2026-09-16. Status: locally implemented and verified.
No migration, provider experiment or deployment was performed.

## Implemented behavior

Execution owns a separate WorkSequenceAssessment containing typed precedence,
unresolved issues and transfer dependencies. It contains no Dispatch screen DTOs.
WorkOrderKey continues to control display selection. Calculation readiness no
longer treats its load-number tie-break as predecessor evidence.

WorkSequencePolicy assesses the resolved ETA chain without changing its order:

- Portions of the same load use stored LoadExecutionLeg.Sequence. Explicit
  reverse order conflicts even if the first displayed portion is active.
- A unique started load precedes upcoming work. Competing started legacy loads
  prevent an arbitrary current forecast.
- Distinct local appointments remain provisional evidence. Missing dates, tied
  appointments, absent times on the same day and different time-zone identifiers
  do not resolve order. ShipDate remains the date fallback. Two empty time-zone
  identifiers retain the existing local-schedule convention; no UTC chronology
  is inferred.
- Every earlier/later pair is checked. An unknown third load can invalidate the
  choice of the second load while leaving a unique current load calculable.
- An incoming native leg requires both release and receipt confirmation from
  each non-cancelled transfer. Confirmation is actor presence, independent of
  whether the actual timestamp is known.

WorkSequenceReader reads native link positions and transfer confirmations in two
batch queries through IAppDbContext. Legacy-only input adds no evidence queries.
The reader makes no provider requests, fetches no HOS data and writes no facts.

EtaChainInputsService assesses hydrated assignment data, so minimal supplied-row
DTOs do not determine legacy schedule readiness. The assessment is required in
EtaChainDescription and included in input-hash policy 12. Transfer revisions
participate even when confirmation booleans remain unchanged. Geometry caching
retains its separate key.

PrepareAsync translates typed issues into existing current/future unavailable
reasons. Existing ETA calculation propagates a blocked continuation downstream;
the unambiguous current prefix remains calculable. A pending transfer blocks the
incoming root. No Client or HTTP response contract was changed.

## Scope and limits

This is a read-time guard for the resolved ETA subset, not the complete canonical
truck itinerary. Native continuation still stops at the existing boundary;
planned native successors are not enabled. Competing work outside that boundary
or outside existing overdue selection is not assessed by this integration.
Board, preview and other planning consumers retain their current selection.

Explicit precedence proves order, not immediate physical continuity or existence
of a saved connecting route. Existing route ownership, connection, driver and
travel-time checks still apply. Unresolved work remains assigned and visible;
this phase does not reconcile it automatically or introduce a new ordering UI.

StopsJson, legacy projection adapters, cached assignment reads and multi-query
consistency remain. The new evidence is not a transactional snapshot or financial
record. PostgreSQL migration and recovery gates remain required before storage
cutover. Performance has not been measured in production.

## Regression evidence

Added 19 server checks across pure policy and SQLite integration fixtures:

- Equal appointments cannot acquire precedence from load numbers.
- Future ambiguity retains a unique current root and changes ETA input identity.
- Competing started work blocks current ETA.
- Known appointments remain provisional; missing times and different zones are
  unresolved; one unscheduled load alone has no ordering conflict.
- Explicit native sequence overrides schedule; reversed links remain conflicts.
- Missing native links and each incomplete confirmation combination block work.
- Transfer confirmations without timestamps unblock incoming readiness and
  change the input hash without changing geometry identity.
- Timestamps without actors do not confirm transfer; cancellation excludes its
  dependency while retaining load-link evidence.

The existing architecture guard now includes WorkSequence contracts. Existing
ETA tests cover propagation of a future unavailable reason without discarding
current results. No architecture constraint or allocation limit was weakened.

## Verification

Affected-category run passed before the two native integration checks were
added: 1,162 server, 625 Client and 52 JavaScript tests.

An initial full run passed 2,215 server, 1,012 Client and 561 JavaScript tests.
After making sequence assessment a required constructor input, a full rerun had
one failure in the unchanged FuelSearchGeometryAllocationTests fixture:
the 25,001-points case measured 9,000 allocation bytes against a 4,096-byte bound.
Both cases passed when rerun in isolation. The cause of the intermittent result
was not established; the test and production fuel algorithm were not changed.
Final `bash test.sh all` completed with exit code 0:

- Server.Tests: 2,215 passed, zero failed or skipped.
- Client.Tests: 1,012 passed, zero failed or skipped.
- JavaScript: 561 passed, zero failed or skipped.
- Total: 3,788 passing checks, including architecture checks.

Pinned CSharpier formatting, scoped whitespace, documentation links and
preservation against the pre-edit file-hash baseline were checked separately.
No browser or release gate, PostgreSQL check, recovery exercise or live-provider
experiment was run. No migrations were authored or applied.

## Next bounded slice

Build the complete per-truck read result with accepted visit facts, explicit
excluded/unresolved work and validated revisions. Keep calculation limits
separate from itinerary completeness, then align the next planning consumer's
scope. Do not normalize storage or add compensation/accounting tables merely to
hide unresolved ownership or snapshot consistency.

See the [core specification][spec] and [acceptance scenarios][scenarios].

[spec]: ../../architecture/core-rebuild.md
[scenarios]: ../../architecture/core-rebuild-scenarios.md
