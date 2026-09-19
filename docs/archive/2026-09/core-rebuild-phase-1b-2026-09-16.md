# Core rebuild phase 1B: ETA inputs and explicit selection order

Date: 2026-09-16. Status: locally implemented and verified.
No migration, live provider experiment or deployment was performed.

## Implemented behavior

EtaChainInputsService.DescribeAsync now reads shared execution work selection
when the caller supplies no ordered rows. It no longer sends
GetDispatchBoardQuery and no longer injects ISender. Its batch/core reads
consume
immutable WorkLoadReference values. Existing supplied-row and batch entry points
adapt their input at the boundary; their public signatures remain compatible.

The selection contract now includes DriverId. Native execution uses the leg's
driver, independently of the truck profile's older assignment. This preserves
the driver selection previously supplied by hydrated Board rows.

TruckWorkSelection no longer references Dispatch screen models. The temporary
legacy conversion lives in ExecutionWorkProjection, alongside storage selection.
Preview's fleet adapter reuses it. No second assignment selector was introduced.

WorkOrderKey names and centralizes the existing comparator: active execution,
started work, upcoming work; local scheduled start; load number. The selection
sort and exported reference use the same key. Existing completion, scope,
transfer readiness, ETA input hashes and calculation policies are preserved.

## Characterized limitations

Load-number sorting does not prove physical precedence. Equal complete keys
still retain input order, and local appointment times do not establish UTC order
across time zones. The implementation exposes the key, but does not yet emit an
unresolved-order result or change current-load selection for tied schedules.

Scope remains deliberate and unchanged: current preview/live planning/ETA
exclude
unstarted overdue assignments; rolling fuel scope includes them. Started overdue
work remains visible. The next change must coordinate consumers before changing
this behavior. The scope matrix is in the [core specification][spec].

The result is still a work selection, not a complete coherent itinerary
snapshot.
Native StopsJson, legacy projections, multi-query consistency and other Board
consumers remain. Accounting/compensation must not treat this selection as
approved financial evidence. No claim of complete phase-1 migration is made.

## Regression evidence

Added three ordering scenarios:

- Started work precedes an earlier unstarted appointment.
- Known schedules precede unknown schedules; dates precede load numbers.
- Equal schedules retain the existing load-number tie-break.

Extended native-leg assertions for active/upcoming order keys. Added three ETA
scenarios: direct versus supplied-input hash parity, native driver selection
against a different truck driver, and cancellation before input reads.

The direct-read fixtures inject a sender that rejects Board requests and verify
zero telemetry requests and HOS calls. Legacy parity also verifies no dense
geometry transfer while describing inputs. Existing transfer-boundary and saved
route validation checks continue to run in the full suite.

Added an architecture guard: execution selection contracts cannot reference
Dispatch screen DTOs and ETA input preparation cannot reintroduce a Board query.
No existing architecture check was weakened.

## Checks run

`bash test.sh dispatch routing` passed before the final architecture guard:
1,144 server, 625 Client and 52 JavaScript checks.

Final `bash test.sh all` completed with exit code 0:

- Server.Tests: 2,196 passed, zero failed or skipped.
- Client.Tests: 1,012 passed, zero failed or skipped.
- JavaScript: 561 passed, zero failed or skipped.
- Total: 3,769 passing tests, including architecture checks.

Changed C# was formatted with pinned CSharpier. Scoped whitespace, documentation
links and file preservation were checked against a pre-edit metadata baseline.
No Client source or HTTP contract changed. No separate browser/release gate ran.
No migrations were authored or applied. PostgreSQL concurrency/physical schema,
production performance, financial rules and live provider behavior were not
checked. SQLite fixture results are not evidence for PostgreSQL concurrency.

## Next bounded slice

Define operational precedence separately from display sorting: explicit leg/load
continuity, transfer dependencies and a reasoned unresolved-order result.
Capture
fixtures for competing started work, same-time future loads and missing
schedules.
Move the next read consumer only after its scope and failure behavior are
aligned.

The complete itinerary still needs accepted visit facts, versioned input
identity
and coherent snapshot/revalidation semantics. Persistence cutover retains the
isolated PostgreSQL and recovery gates established in phase 0.

[spec]: ../../architecture/core-rebuild.md
