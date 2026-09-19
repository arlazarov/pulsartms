# Core rebuild implementation record before the clean transition

This is historical implementation evidence. The [current specification](../../architecture/core-rebuild.md) supersedes its preservation and backfill
requirements. The four intermediate September 17 migrations were never applied
to the working database and have been replaced by one clean transition.

## Implementation record before the transition decision

Status: normalized native stop storage, accepted native revision history and
durable fuel-history validation
implemented locally. Working-database migration remains pending final verification
and a compatible, explicitly approved API cutover. Standalone work inputs,
unified visit/history ownership, independent observations and finer publication
ownership remain to be consolidated. Synthetic PostgreSQL migration checks
passed; contention is unmeasured. This document owns the core
replacement scope; it does not describe deployed behavior. Existing operating
guides remain authoritative for the running application.

Read alongside [execution and settlements][execution] and the
[SaaS roadmap][saas]. This sequence prioritizes a working operational slice
before broad tenancy or host extraction. It does not activate native load
creation, another company, payroll, payments or deployment.

## Objective

One owner for each operational fact, one itinerary input for calculations,
and independently reproducible financial records. Retain the modular monolith,
PostgreSQL, current integrations and proven calculation policies.

Do not use a rewrite to change fuel optimization, ETA policy, UI design or
the meaning of existing actuals. Corrected semantics require explicit examples.

## Vocabulary and ownership

- Load: commercial obligation; Loads owns customer and charge facts.
- Load stop: requested work and appointment, independent of assigned resources.
- Execution leg: work under a specific resource assignment; Execution owns it.
- Execution visit: stable ordered occurrence, distinct from an address.
  A common identity does not require one record full of optional operation data.
  Ordinary work and transfer actions retain explicit operation-specific rules.
- Load execution link: carried load portion and its boundary visits. Physical
  movement is not duplicated when it serves more than one load.
- Assignment: truck, crew roles and trailer responsibility over an interval.
  Planned intent and confirmed responsibility are distinct.
- Custody: trailer responsibility, including a parked interval between actions.
- Movement: physical travel interval; Mileage owns evidence and attribution.
- Trip: optional operational grouping, not another assignment or status owner.
  Keep current storage during transition; do not expand its responsibilities.
- Payee: recipient of compensation, distinct from driver and login identities.
- Settlement: versioned compensation calculation and approval, not payment.
- Accounting document: issued obligation or adjustment, independent of a load's
  operational status. Payment and reconciliation have their own evidence.

### Field ownership

- Provider IDs, source revisions and raw normalized proposals: Integrations.
- Accepted load obligations and commercial lines: Loads.
- Accepted visit sequence, resource changes and actual confirmations: Execution.
- Distance observations, quality and allocations: Mileage.
- Route, ETA and fuel results: Planning, keyed to exact input revisions.
- Contract versions, payable basis and approved earnings: Compensation.
- Issued invoices, credits, liabilities and payment allocations: Accounting.
- Stable actors, membership and authorization: Identity/organization boundary.

Imports propose facts through the owning Application operation. Local overrides
record the field, actor and version, with an explicit return-to-source action.
Historical evidence survives account deletion and later display-name changes.
Readers must not choose competing owners according to native/legacy origin.

## Invariants

1. A visit ID identifies an occurrence, not a coordinate or list position.
2. Repeating a command returns its recorded result; the same key with different
   content conflicts. No duplicate confirmation or financial line is created.
3. Resource changes commit atomically with their transfer/custody facts.
4. Planning a transfer, reaching its GPS location or importing a changed field
   does not confirm release or receipt. The actions may happen on different
   days.
5. Missing actual time remains unknown. Confirmation and timestamp availability
   are different facts; do not fabricate chronology during migration.
6. HOS follows the driver; fuel follows the physical truck; cargo follows its
   confirmed execution links. A transfer cannot exchange these accidentally.
7. Planned, observed, allocated and payable distances are distinct quantities.
8. A stale calculation cannot replace a result from newer accepted inputs.
9. Source refresh cannot rewrite approved financial evidence. Corrections link
   to what they supersede; they do not silently mutate historical approval.
10. UI filtering cannot decide the authoritative remaining truck itinerary.

## First implementation slice: canonical itinerary read

Owner: Application/Execution. Reuse the existing persistence through
IAppDbContext and existing execution read rules. Provider SQL stays behind
Infrastructure interfaces. Do not introduce a second set of business tables yet.

Existing starting points:

- GetExecutionItineraryHandler selects a leg and applies transfer actuals.
- ExecutionLoads.ReadAsync assembles native execution and owned dispatch IDs.
- GetDispatchBoardHandler currently selects and enriches screen rows.
- ExecutionWorkReader now owns shared work selection; the board projects its
  immutable selection records into screen identities.
- RoutePreviewService and PlanningReadService select current work from the
  complete snapshot through TruckPlanningInputsReader. Their fleet/board paths
  retain paged board reads only to enumerate the requested trucks.
- Fuel search, editing and saved-plan validation use FuelWorkInputs over that
  same complete snapshot, with fresh reads for calculation and publication
  checks and bounded cached reads for display/refresh eligibility.
- ETA reads TruckItinerarySnapshot for each requested truck. Board rows supply
  truck identities only; stored work determines membership and order.
  This is a consumer migration, not normalized visit storage.

Introduce a provider-independent immutable read contract. Names below are
conceptual, not authorization to duplicate existing interfaces mechanically.

Input: truck identity and an explicit as-of instant. Authorization remains at
the Application boundary. Add explicit company scope when its persisted
ownership exists; never invent a default tenant to claim isolation.

Output:

- Truck identity and a deterministic input signature.
- Ordered execution segments and explicit assignment revisions.
- Load references, visit identities, obligations and confirmed actuals.
- Transfer dependencies and reasoned unresolved/ambiguous state.
- Provenance sufficient to diagnose source reconciliation, without raw payloads.

Include unfinished overdue work and assigned future work according to explicit
ordering rules. Return completed boundary evidence where continuity needs it,
without treating completed work as remaining work. Do not silently truncate.
Consumer calculation limits are validated separately from itinerary
completeness.
If cross-load order is ambiguous, expose that dependency; do not invent an order
from GUIDs. Characterize current ordering before replacing it.

The contract contains no screen paging/search, financial enrichment, HOS fetch,
routing request, fuel calculation or cache-mutating read side effects. Attach
telemetry/HOS snapshots in calculation orchestration with their own revisions.
Capture a coherent read or detect changed revisions and retry within a bound.

### Transition and exit gate

1. Add characterization fixtures for the scenario IDs in the acceptance catalog.
2. Extract shared selection rules rather than copy the board into a new service.
3. Compare old/new results against the same fixture snapshot. No extra provider
   requests, source poller or second business writer for comparison.
4. Move one non-mutating planning read to the contract behind existing HTTP
   shape.
5. Remove that consumer's board dependency and temporary comparison code.

This slice finishes when the selected consumer has no board query dependency,
identity/order/revision scenarios agree or have a documented corrected rule,
and saved reads make zero provider calls. Other consumers are explicitly
pending.
Do not call the entire core migrated after this slice.

## Current extraction limits and next gate

The implemented selection contains load/leg identities, assignment revisions
and visit references, not complete calculation inputs. The storage reader still
adapts existing
Dispatch projections through ExecutionWorkProjection. The immutable model itself
has no Dispatch DTO dependency. Date scope, legacy ordering and native snapshot
storage remain unchanged. The first truck preview preserves its previous overdue
policy so preview and live planning do not diverge during this extraction.

TruckItineraryReader now builds a separate immutable snapshot over the complete
remaining-work selection. The lightweight selection itself is still not a
calculation snapshot or financial evidence. Phase 1B characterized display
ordering; phase 1C guards the resolved ETA chain; phase 1D supplies the complete
read contract and consistency boundary described below. Phase 1E moves ETA
preparation onto that snapshot. Phase 1F moves saved preview and live planning
reads onto it. Phase 1G moves fuel search, editing and saved-plan validation
onto it. Phase 1H moves automatic selection, route builds/progress and
route-choice preview/save onto captured work. Phase 1I validates canonical work
inside the protected result transaction. Phase 1J consolidates profile and fuel
compatibility writes and adds uncached effective-profile validation for fuel.
Phase 1K adds immutable historical inputs and guarded deadhead publication.
Phase 1L carries historical dependencies into ETA/fuel publication. Phase 1M
protects stored truck/fleet settings and validates live route/ETA profiles.
Phase 1N brings the automatic exchange-rate writer into the same publication
scope. Phase 1O protects ETA saved-road versions through commit; phase 1P
extends that boundary to fuel. Phase 1Q protects base/choice settings. Finer
publication ownership remains separate.

The replacements remove GetDispatchBoardQuery from per-truck saved preview,
live planning and standalone ETA input paths. Existing board cache invalidation
is reused; distributed freshness and host-role changes remain separate work.
The storage projection bridge is
removed when normalized visits and all consumers have replaced it.

### Complete work snapshot

GetTruckItineraryQuery accepts truck identity and an explicit as-of instant. It
has no paging, search, date exclusion or calculation-depth parameters. It is an
internal Application query, with no new public HTTP endpoint.

The reader includes current and planned native legs and non-terminal legacy
assignments selected by the shared owner, including unstarted overdue work.
Completed/cancelled work is not remaining work. Native load-link evidence keeps
completed predecessor identities, assignment revisions, status, truck and
boundary
visit IDs without turning those predecessors into active segments.

Each segment retains stored visit identity, location and verification/retry
facts, normalized source address, appointment, assignment, manual operation,
operation revision and confirmed actuals. Visits outside
the continuous truck path remain present with InTruckPath=false. An unresolved
path or assignment, missing visits/transfer records and native source review are
explicit problems. Invalid native JSON, duplicate identities and null visit
entries preserve the assignment as a segment with missing visits.

Transfer evidence includes boundary identities, planned/actual times and
separate
release/receipt confirmation. Unknown actual times stay null. Native accepted
and
observed source signatures remain inspectable without exposing provider
payloads.
WorkSequencePolicy assesses the full returned work, while its existing ETA use
still assesses only the calculation subset.

IExecutionReadScope wraps selection, hydration and evidence reads in one
snapshot.
Infrastructure selects repeatable-read for PostgreSQL and serializable for the
SQLite fixture. A compatible caller transaction can be reused for ordinary
reads; weaker isolation is rejected. No provider detection or SQL enters
Application. Snapshot reads neither save nor detach caller-owned tracked edits.

InputSignature hashes the projected facts, resource configuration, memberships,
revisions, dependencies and readiness. It does not rely solely on legacy
revision
counters. AsOf supplies the UTC date used to mark overdue work; it does not
request
historical reconstruction. Advancing the instant within that day does not change
an otherwise identical signature.

MatchesAsync rereads the snapshot and compares its signature. It rejects an
already-open transaction so an older caller snapshot cannot falsely certify
freshness. This check describes its read instant. A writer must still validate
revisions atomically when publishing a result; a successful comparison is not a
lock against subsequent changes.

This scope covers remaining linked assignments under current status/completion
rules. It is not an audit of orphan rows or a financial ledger. Unassigned work
without a truck association is outside a per-truck read. Normalized visit
storage,
historical corrections and remaining consumer cutovers remain. Phase 1I protects
canonical-work publication; it does not make every input transactional.

### ETA consumer boundary

ETA accepts truck IDs at its core. Its Board adapter extracts only these IDs;
filtered, reversed, missing or forged screen load identities cannot choose the
calculation root. The former caller-supplied ordered-load overload is removed.
TruckItineraryReader.ReadManyAsync batches selection, hydration and evidence for
requested trucks. Each returned signature includes only that truck's evidence.

EtaChainInputsService no longer queries Dispatch/Truck assignments or calls
RoutePlanningService.ResolveAssignmentAsync. ExecutionRouteProjection is a pure,
internal compatibility adapter from captured visit facts to the Dispatch input
still required by route algorithms. It preserves the selected truck path,
completion identities, manual operations and address reliability. It performs
no assignment lookup and cannot mutate the immutable source snapshot.

An ETA description retains its complete itinerary and typed exclusions. Existing
legacy planned/unassigned and unstarted-overdue exclusions remain calculation
policy. Inactive native work, native continuation without a supported connection
and unresolved snapshot problems stop the calculable prefix. Later work is
explicitly blocked. A matching saved completed root is separately recorded.
Malformed or source-review-blocked native current work cannot silently select a
legacy root. If no usable root remains, no ETA description is returned; the
complete Execution query still exposes the unresolved facts.

All operational inputs, selected driver external IDs, root metadata, future
route
versions and predecessor evidence are read within one IExecutionReadScope.
Every description requires a fresh database snapshot, including revalidation
after calculation. No provider/HOS request or result publication runs within
this
transaction. Existing profile/settings cache behavior remains separate; this is
not a transaction over caches, telemetry or the entire calculation lifetime.

The full itinerary signature and exclusions participate in ETA InputHash, while
GeometryHash retains its existing independent reuse policy. A change in excluded
work therefore invalidates the forecast without forcing geometry to be
recompiled
when geometry inputs are unchanged. Saved owner, revision, anchor and route-hash
checks remain in force. Publication still uses the existing newer-only/revision
store; a fresh read does not close every read-to-write race for legacy inputs.

DeadheadHistoryService still owns predecessor selection, including completed
work and unknown-start evidence outside the remaining itinerary. Its reads
share the ETA transaction; they may overlap captured work. Replacing history
with only remaining segments would lose those guards. Phase 1K makes the
historical contract immutable and adds its content signature. Phase 1L
includes this historical token in ETA input hashing and final publication
validation. Phase 1I protects canonical work; non-itinerary inputs retain
separate validity policies.

### Saved preview and live planning read boundary

TruckPlanningInputsReader captures a complete TruckItinerarySnapshot and the
selected driver's external identity in one IExecutionReadScope. Saved preview
and live planning use that shared owner for membership, order, visits and
assignment facts. Paged board reads enumerate fleet/board truck IDs only; screen
load filtering cannot select a different current load. Per-truck reads do not
query the board or repeat mutable assignment resolution.

PlanningWorkPolicy keeps the existing display scope explicit: native remaining
work is eligible, while legacy work must be assigned/in transit and not overdue
under the existing overdue rule. The complete captured snapshot still retains
planned and overdue work. A candidate with unresolved assignment, source or
visit problems blocks selection rather than falling through to a later load.
Existing saved owner, leg/revision, stop, profile and input-hash checks remain.
Missing or stale geometry does not skip the first remaining load.

The captured work uses the existing bounded ReadCache lifetime. UTC date and
board, dispatch and execution generations invalidate it. This preserves warm
polling without database queries, but does not provide a fresh publication
check or distributed invalidation. ETA continues to require fresh reads for
calculation validation. Commodity and notes are captured visit facts so updated
metadata can refresh independently of saved geometry.

Live planning requests HOS after the capture's read scope and cache gate have
finished, once per batch. It attaches clocks to the captured driver identity.
An active native leg with no driver cannot inherit the previous truck driver's
clocks. HOS is not stored in the captured-work cache; saved previews do not
request it. External clocks and cached settings are not part of the database
snapshot transaction.

ExecutionRouteProjection is shared with ETA and remains a pure compatibility
bridge to existing route algorithms. Fuel signature validation still needs a
Dispatch DTO projection from the captured fuel subset. Explicit per-dispatch
reads first resolve the requested assignment, replace it with captured facts
when present, and preserve the historical/completed-leg path when it is outside
remaining work. Phase 1K supplies immutable historical facts; removing the
remaining compatibility projection still requires migrating its consumers.

AutomaticPlanningService writes and RouteChoiceService remain separate
migration work. Existing display refresh scheduling is unchanged; saved preview
still makes no provider requests or persistence writes.

### Fuel consumer boundary

IFuelWorkInputsReader separates fresh calculation captures from bounded display
reads of the same TruckItinerarySnapshot. Fresh capture requires a new
IExecutionReadScope and rejects an existing caller transaction. The transaction
ends before route assembly, price reads, HOS schedule evaluation or writes.
Display validation reuses TruckPlanningInputsReader without requesting HOS.
PlanningReadService passes its existing itinerary to TruckFuelPlans so root
selection and fuel validation use the same captured work.

FuelWorkInputs selects assigned/in-transit legacy work including overdue work.
Unassigned legacy planned work stays visible in the full snapshot but outside
fuel scope. Existing native rules remain: confirmed active work can continue
past ordinary delivery into eligible legacy work; transfer boundaries, other
native legs and unconfirmed receipt keep their existing cutoffs. Selected work
with source, assignment or visit problems cannot produce recommendations.
Scope selection still uses the existing fuel policy; this does not introduce
new physical precedence rules for fuel or native successor forecasting.

FuelHorizon and FuelRegionPlanner receive the same immutable capture and use
ExecutionRouteProjection for route inputs. They no longer load assignments
again or accept supplied screen load lists. FuelPlanningService's automatic
search, edit preview/save and reset use the shared input owner. TruckFuelPlans
and FuelPriceRefreshService validate against captured work rather than the
Board. An initial per-dispatch lookup remains to locate and validate the
requested truck/leg; captured facts replace its route input before calculation.

Persisted fuel signatures and FuelPlanProjection retain their pure Dispatch DTO
compatibility format. Normal native and legacy signature parity is tested.
Normalized number-only assignments may conservatively invalidate older
signatures; no old recommendation is declared current by ignoring a mismatch.
No financial formula, optimizer bound, geometry basis or public contract changes.

Before saving, fuel rereads a fresh complete itinerary and compares its content
signature with the original capture, then validates saved assignment and stop
identities. A changed input rejects automatic or manual publication and keeps
both previous saved copies. The existing transaction and expected-result
revision still guard replacement. Phase 1I additionally compares canonical work
inside the protected replacement transaction. Inputs outside this itinerary
retain separate consistency policies.
The complete signature is a calculation token, not new historical storage.

Saved base/deadhead roads retain their existing hash and anchor checks.
Historical selection has its own immutable capture in phase 1K. Fuel does not
yet retain that historical token for final publication. Geometry and
profile/settings caches remain separate; consolidate their guards before
removing compatibility.

## Phase 1H route mutation migration

TruckPlanningInputsReader now exposes fresh uncached capture and complete
signature validation in addition to its bounded display cache. Both modes share
the same capture/driver mapping; fresh reads reject an older outer transaction.

| Consumer | Input ownership after phase 1H |
| --- | --- |
| Automatic truck planning | Fresh itinerary, existing planning subset |
| Dispatch/upcoming preparation | Locator then captured truck/leg facts |
| Route build and progress | Captured facts under the truck gate |
| Base route during route build | Same capture checked before writing |
| Route choice preview/save | Fresh queue and durable signature/as-of stamp |
| Route choice current selection | Same capture, saved-road completion checks |
| Display and saved preview | Existing bounded cache remains |
| Standalone base/deadhead history | Deadhead capture/publication in phase 1K |
| Profile and fuel compatibility writers | Consolidated in phase 1J below |

A provider response cannot publish a route after a detected assignment, queue,
configuration or visit change. Progress also checks saved-road ownership and
input matching before tracking. Unstamped old choice previews require a new
preview; no schema migration or Client contract change is introduced. Complete
signature checking may conservatively invalidate work whose changed facts do not
affect geometry; geometry reuse keeps its separate signature.

Phase 1H checked work before publication. Phase 1I moves the final comparison
inside a transaction protecting canonical source facts and membership; neither
phase creates durable accounting evidence. Provider waits do
not hold a database read transaction. Existing current/upcoming ordering,
overdue scope, HOS policies and native GPS receipt requirements remain intact.
Mutation paths deliberately reread work; cached display query budgets remain
separate. Production latency has not been measured for these extra checks.

## Phase 1I protected canonical-work publication

PlanningWorkPublication opens IPlanningPublicationScope, rereads the itinerary
at the captured as-of instant, compares its complete signature, and returns that
same transaction to the writer. The owning writer commits after all result
parts succeed. Validation failure disposes the transaction before writing.
There is no provider work or automatic callback replay inside this scope.

Migrated writers: route build, automatic progress, base-road writes invoked by
a captured route build, route-choice preview/save, fuel search/edit replacement
and ETA forecast batches. Existing result concurrency checks and native leg
locks remain. Fuel keeps both stored copies atomic; ETA marks memory publication
successful only after commit. Display reads retain their separate bounded cache.

The PostgreSQL adapter takes SHARE ROW EXCLUSIVE locks with NOWAIT on these
source tables before the first snapshot query: DispatchBaseRoutes,
DispatchDeadheads, DispatchRoutePlans, DispatchStops, Dispatches, Drivers,
ExecutionLegs, ExecutionVisits,
FleetPlanningSettings, LoadExecutionLegs, SwitchParticipants, Trailers,
TruckPlanningProfiles and Trucks. Phase 1K adds DispatchDeadheads for stored
completed-leg membership; phase 1M adds the two stored-settings tables and
phase 1O adds both saved-road tables. The inventory includes navigation joins
and table references in Infrastructure metadata SQL. It protects updates,
deletes and newly inserted work, including number-only legacy assignment.
Locks held by an existing writer
defer publication; disposal releases partial acquisition. The protocol follows
PostgreSQL's [LOCK ordering and snapshot
rules](https://www.postgresql.org/docs/current/sql-lock.html). SQLite uses its
serializable transaction for isolated tests. Unsupported providers and
caller-owned transactions fail closed.

This lock is deliberately broader than a truck. Publications across trucks
serialize and can briefly delay source writes; ordinary PostgreSQL SELECTs can
continue. Production throughput is unmeasured, and PostgreSQL execution needs a
safe isolated fixture before release. Replacing this with a narrow revision
check requires every source writer to own the affected truck revisions, including
legacy number changes and native queue membership. Locking only captured rows
would leave insertion and reassignment gaps.

No new table, migration, public HTTP field, calculation formula or normalized
visit storage is introduced. This scope protects the facts represented by
TruckItinerarySnapshot and, with phase 1M, stored truck/fleet settings.
Phase 1N also makes automatic exchange-rate writes join this scope. Phase 1O
checks ETA saved roads within it; phase 1P adds fuel saved-road dependencies.
Display caches, telemetry, prices and financial evidence retain separate
policies. Source updates after a valid commit still invalidate results
through existing read policies. Phase 1K adds the historical connection guard
below; standalone base-road preparation and inputs
outside captured work retain separate policies.

## Phase 1J profile and fuel write ownership

FuelPlanningService no longer persists a profile before price reads and
calculation. Its publication transaction now owns three results: the requested
truck profile, route compatibility fuel copy and truck-owned fuel snapshot.
Validation, any result write or commit failure rolls them back together.
The opened fuel-result revision and native assignment guards remain in force.
The route-copy writer checks both stored and serialized truck ownership for
legacy as well as native results.

Explicit profile saves use fresh remaining work and PlanningWorkPublication.
Completed work, assignment-revision mismatch and a changed publication capture
cannot write a profile through an obsolete dispatch reference. Low-level profile
and fuel writes are internal operations requiring a transaction. Their current
callers are the protected route/profile/fuel publication owners. Profile cache
invalidation happens after commit, including route builds.
Fuel also invalidates the exact dispatch/execution-leg route cache after commit,
so pre-commit cache repopulation cannot retain an older native fuel result.

Removed unused RoutePlanningService fuel-clear, road-replacement and
recommendation writers, plus the corresponding unused store methods. The fuel
publication owner calls the route store directly inside its protected operation.
Legacy result fields remain readable; no persisted data or public HTTP field was
removed, and fuel calculation still makes no routing/geocoding request.

Fuel compares the initially observed effective profile against fresh stored
profile, fleet preferences and saved exchange rate before overwriting the
profile. The compatibility copy validates the effective profile again after the
write. Cached and uncached reads share the same fleet-default and rate-selection
logic; uncached reads do not fill display caches or request providers. A changed
value missed by local cache invalidation now rejects publication.

Phase 1J did not extend the source-table lock to fleet settings or exchange-rate
storage; phase 1M below protects stored truck/fleet settings. Rate reads share
the publication transaction when their store uses the scoped database.
Phase 1N joins rate writes to the same publication scope. Explicit profile
editing remains last accepted write, without a new client revision field. Phase 1L
protects historical dependencies at ETA/fuel publication. Telemetry, prices
and cross-process non-itinerary consistency remain separate work.

## Phase 1K historical connection ownership

DeadheadHistoryService now returns immutable HistoricalRouteLoad/Stop records
and an InputSignature. Mutable Dispatch objects remain only at the storage and
algorithm compatibility boundaries. Supplied current inputs are deep-copied
before asynchronous lookup; projecting them back for existing algorithms creates
new objects. Candidate enumeration order cannot change the content signature.
Financial inputs, assignment/completion revisions, schedules, native identity,
address readiness and unknown-start evidence are captured separately from the
narrow geometry hash. Captures are transient calculation inputs, not durable
accounting or settlement evidence.

Candidate lookup and native hydration run in one IExecutionReadScope. Completed
native legs linked to bounded legacy candidates or saved previous-leg references
remain available; they are not inferred from remaining work. The legacy lookup
still materializes at most two predecessors per destination and retains unknown
and tied pickup guards. Native selection retains its existing active/planned and
completed-reference rules. No predecessor ordering policy changes.

The PostgreSQL batch now starts from parameterized captured ID/truck/date/time
arrays with unnest, then applies the existing correlated top-two query. It no
longer rereads the current dispatch to choose a different truck or start time
from the values supplied by the caller. SQLite uses the same captured inputs in
its bounded fallback. PostgreSQL translation is checked without connecting;
real execution still requires an isolated fixture.

MatchesAsync compares a ReadAsync capture with a fresh database read and rejects
an older caller-owned transaction. ReadLoadedAsync can intentionally represent
supplied current facts differing from storage; it does not certify that those
facts are current. Neither a content token nor a read-time comparison alone
protects a subsequent write.

DeadheadHistoryPublication opens IPlanningPublicationScope, rereads history,
compares its token, and returns that same transaction for writes. The protected
table inventory now includes DispatchDeadheads because saved previous-leg IDs
participate in completed-history selection. Unchanged retry reservations and
route geometry are not themselves inputs to that signature.

DeadheadService uses two short transactions: one validates history and saves
the retry reservation with current financial values; the other validates the
same capture and atomically writes completed road mileage/geometry and
DispatchRates. Provider/geocoding calls occur between them. DispatchRates
reuses an existing transaction and retains its own transaction for standalone
callers. Failures in final validation, financial writes or commit preserve the
committed reservation and preceding financial mileage. Existing optimistic
result conflicts preserve the concurrent winner. Failed publication detaches
its tracked road/rate values so a later save cannot publish rolled-back data.
Price-only refreshes reuse valid road geometry.

ETA, next-load and fuel helpers consume the immutable historical read contract.
Phase 1L below carries the token through ETA/fuel publication. Standalone
base-road inputs and external observations retain separate policies. The broader
table lock is transitional; no throughput or latency improvement is claimed.

## Phase 1L historical dependencies in ETA and fuel publication

PlanningWorkPublication now accepts historical batches as well as the complete
itinerary. It validates canonical work, then replays each historical lookup from
its captured current facts inside the same owned transaction. All comparisons
finish before the caller writes results. A mismatch disposes the transaction and
returns an expected planning error. No nested publication transaction, new lock
inventory or provider call is introduced.

DeadheadHistoryBatch preserves the original lookup membership. Completed native
leg hydration shares candidate and saved-leg references across the destinations
in that lookup; flattening independent batches or validating a grouped capture
one destination at a time can change its evidence without a source mutation.
The guard therefore replays each original batch. Current facts are not certified
by this history replay alone: the owning itinerary must be validated first.
DeadheadHistoryPublication retains its separate database-current capture policy.

EtaChainDescription retains immutable history and includes destination IDs and
historical signatures in its calculation InputHash. Policy version 14 invalidates
old forecasts through normal reads. GeometryHash remains separate, so a history
revision without a geometry change can reuse compiled road timing. Missing and
ambiguous predecessors are dependencies too. The final forecast transaction
checks that history, including changes after the earlier full-description read.
Failures keep stored forecasts and remove the uncommitted calculation's memory
entry. The earlier description check remains for other inputs such as saved-road
versions; this stage does not make every input transactional.

DeadheadService.CaptureRouteAsync returns saved road geometry together with the
historical batch used to select its predecessor. FuelHorizon retains those
batches for legacy connections. A connection derived directly from the captured
native itinerary remains protected by canonical work. FuelRegionPlanner returns
FuelArrivalInputs: the existing arrival policy plus any historical onward-road
dependency, including its conservative unknown-exit return path. Neither service
requests road/geocoding providers or persists these capture records.

Automatic fuel search, manual save and reset combine horizon and onward-road
history in the shared CommitAsync operation. Canonical and historical checks
precede profile, route-copy and truck-snapshot writes. A changed completed load
can reject publication even when the remaining itinerary signature is identical.
Existing replacement revisions, cached-profile policies and native transfer
boundaries are preserved. Public fuel DTOs and financial formulas are unchanged.

These guards protect calculation-to-commit consistency. They do not introduce a
new persisted historical signature for fuel display/automatic-refresh decisions,
a settings/rate write protocol, a saved-road version lock, or durable accounting
evidence. Those policies need separate ownership before claiming complete input
consistency. PostgreSQL execution and contention remain unverified; added reads
inside the existing broad publication lock have no measured production cost.

## Phase 1M stored settings at publication

IPlanningPublicationScope now protects TruckPlanningProfiles and
FleetPlanningSettings as well as canonical/historical work. The fixed
PostgreSQL lock set has twelve tables; acquisition still precedes every
snapshot query and uses NOWAIT. A missing settings/profile row is meaningful
because it supplies defaults, so locking only existing rows would not protect
its insertion. SQLite uses the existing serializable scope. No schema change
or new business service is introduced.

Fuel's existing uncached checks now execute while those settings tables remain
protected through profile and both result writes. The automatic exchange rate
is stored in a shared synchronization checkpoint table; this stage deliberately
leaves that table outside the source lock. An uncached rate read still does not
protect its observation through commit. A dedicated rate ownership protocol
remains necessary without serializing unrelated synchronization jobs.

Live route builds capture the observed effective profile before provider work.
Their final transaction validates it before writing the requested profile and
route. Automatic builds also reject an already-stale supplied profile before
provider work. A manual build can intentionally change dimensions when stored
settings remain unchanged during calculation. This prevents background builds
from restoring an older profile over a newer edit. Existing explicit profile
saves remain last accepted write, without a new client revision field.

Automatic progress/reroutes and ETA share the profile owner's uncached
routing-input guard. It compares the existing RoutePlanningService.HashInputs
for the same captured load and each profile, retaining its established routing
fields rather than inventing a second dimension signature. The check runs
inside result publication. Fuel-only preference, rate and confirmation changes
do not reject road ETA or progress; full-profile writers retain the stricter
comparison. No-write progress checks keep the existing cached profile path;
only publication adds an uncached settings check. Rejected ETA publication
preserves stored forecasts and removes the
uncommitted memory result. ETA description reads keep their bounded display
profile cache; validation does not refill that cache or call a provider.

Phase 1Q below extends settings validation to standalone base preparation and
route-choice preview/save. Existing saved-road metadata checks are not
strengthened into version ownership by these changes. Settings writes can delay
planning publication, and publication can delay settings writes; the broad lock
remains transitional. PostgreSQL execution/contention and production throughput
are unmeasured. Complete source-writer ownership is still required before
replacing the global source-table protection with narrower revisions.

## Phase 1N automatic exchange-rate publication

FuelExchangeRateStore now requires IPlanningPublicationScope. SaveAsync opens
its own fresh transaction, updates the leased observation and commits before
returning. The existing FuelExchangeRateService invalidates its cache only
after this successful return. Lease acquisition, provider requests and lease
release remain outside that transaction; the store retains its stale-owner and
expired-lease predicates.

Rate writes and planning result writes now use the same exclusion protocol.
The final uncached profile read therefore sees a stored rate that cannot change
until result commit. An update completed before publication is visible to that
validation, including the first usable rate after a missing observation.
Explicit fleet conversion rates retain priority. An unused automatic rate
change does not reject an unchanged effective profile.

Phase 1N leaves the then twelve-table source lock inventory unchanged.
SynchronizationCheckpoints
is not added: rate saves join the existing boundary while unrelated checkpoint
rows keep their normal write policy. This is a shared writer protocol, not a
claim that arbitrary writes to the rate row outside its store are protected.
No production alternate rate-state writer exists. Future rate writers and any
replacement for the broad publication lock must preserve this boundary.

A failed write, commit, busy scope or cancellation preserves the previous rate
and leaves its cached value/generation unchanged. The refresh releases its
lease and can retry. Expected planning exceptions carrying a retry time defer
fleet jobs without failure-count growth, success timestamps or failure logs.
Other failures retain the existing backoff and boundary logging.

The change introduces no table, migration, HTTP field, financial formula or
extra worker. It does not persist the full chosen rate observation with fuel
results or freeze clock-based age validation. Historical accounting evidence
remains a separate requirement. Saved-road versions, independently prepared
base roads, choice validity and fuel refresh after historical corrections are
still pending. PostgreSQL execution/contention and production cost remain
unmeasured; the rate writer now briefly acquires the existing global protection
and can defer while source or result writers are active.

## Phase 1O ETA saved roads through publication

EtaChainDescription retains immutable root signatures and future road versions.
Root dependencies include every route consulted before selecting current work,
including completed roots skipped by selection. Legacy and native metadata use
their respective dispatch and execution-leg identities. The root token retains
stored ownership/input identity and plan ID, truck, leg, assignment revision,
version and tracking. It deliberately excludes fuel calculation timestamps.
Policy 15 includes these tokens and the root routing-input signature in ETA
InputHash, including a root-only chain.

Cold future timing compilation derives metadata from the loaded geometry and
requires an exact match before filling EtaMemory. Next Loads uses the same
loaded-version projection. Version metadata now includes base/deadhead geometry
presence: a retry reservation that clears a road but retains financial miles
and its timestamp cannot keep the old timing identity. These flags are computed
inside metadata queries; route JSON is not transferred for a version check.

After work, history and profile validation, ETA rereads root/future metadata
inside the existing publication transaction. It also compares the actual
current plan used by calculation with the captured selected root. This rejects
a stale cached plan even when both description reads see newer database data.
A conflict keeps previous forecasts and removes the unpublished memory entry.
Unchanged roads and fuel-only root updates still publish. Cache invalidation
and expiry remain bounded and per instance; rejection does not add distributed
cache invalidation or reload geometry while holding the source lock.

The existing PostgreSQL lock expands to fourteen tables with DispatchBaseRoutes
and DispatchRoutePlans. DispatchDeadheads was already protected. This prevents
updates, deletion and insertion after validation until forecast commit, even
from source writers that do not enter PlanningWorkPublication. The architecture
inventory now includes table references from Infrastructure metadata SQL as
well as EF sources and navigation joins. SQLite serializable fixtures exercise
independent writers; real PostgreSQL execution and contention remain untested.

No migration, public payload field, provider request, financial formula or
worker is added. ETA continues to exclude unsupported native continuations.
The version contract relies on road writers maintaining existing input/date
and plan-version identities when replacing geometry; it is not a content hash
of every coordinate. Phase 1P below carries fuel road tokens through
publication. Durable historical tokens for read/automatic-refresh eligibility,
standalone base work-input capture remain work. Phase 1Q protects base/choice
settings below. The broader lock can delay more writers; no production latency
or throughput gain is claimed.

## Phase 1P fuel saved roads through publication

SavedRoadVersion captures immutable identities for a base road, connection or
full saved plan. Fuel retains the current plan's token from the actual row
that supplied geometry, including a cached row. FuelHorizon collects future
base/connection versions; a full-plan fallback also retains the absent or
unusable base that selected it. FuelRegionPlanner carries its onward road in
FuelArrivalInputs even when no exit price is eligible. Native and legacy lookup
identities stay explicit. Query keys are deduplicated, but every observation
remains a dependency if a source was read more than once.

SavedRoadValidation runs after PlanningWorkPublication opens the protected
transaction and before any profile or fuel write. It checks captured versions
through compact readers without transferring geometry or calling a provider.
Automatic calculation, manual editing and reset share this path. Late changes
reject publication and preserve the profile and both saved fuel copies.
Existing optimistic result checks and native ownership rules remain intact.
An unused baseline or fuel-only plan change is not a road-version conflict.

The former ETA root metadata reader now belongs to Routing as
ISavedRoutePlanReader, with one Infrastructure implementation shared by ETA
and fuel. Its projection includes stored/serialized ownership, assignment
revisions and full-plan mode. Provider-specific JSON extraction remains in
Infrastructure. Both consumers sort visited-stop dictionary keys before hashing
tracking, so object key order does not create a conflict. ETA policy 16
invalidates signatures produced before that normalization. Passed-stop sequence
and tracking values remain significant.

The existing fourteen-table publication scope already protects these sources;
this adds no locks, schema, migration, public HTTP fields or provider requests.
These version tokens are transient calculation dependencies. They do not add
persisted fuel history/road evidence for read-time validity or automatic refresh
after a previously valid commit. Geometry replacement still relies on writers
maintaining existing input/date or plan-version identity. Arbitrary payload
edits that preserve every version field are outside this contract.

SQLite and architecture regressions cover conflicts, retention, metadata-only
validation, stale root caches, fallback selection and unchanged success paths.
Real PostgreSQL execution and contention remain unverified; production latency
has not been measured. Phase 1Q below protects standalone base/choice settings;
durable fuel refresh dependencies remain work before narrowing publication locks.

## Phase 1Q base and route-choice settings publication

Base preparation and route-choice preview/save now reuse the profile owner's
uncached routing guard. It compares the existing route-input signature for the
same load and the captured/current profiles, rather than adding a second list
of routing dimensions. Warm caches cannot hide changed restrictions. Fuel-only
preferences, exchange rates and confirmation flags remain irrelevant to road
identity. Work without a truck uses the existing default-profile identity.

Both paths check before provider work and immediately after opening their
protected publication transaction. Choice validation precedes draft replacement,
choice revision increments and base/live-plan writes. Its current-road, native
assignment, work-signature and optimistic result checks remain intact. Settings
conflicts retain the previous draft and selected roads without cache
invalidation.

Standalone base preparation now enters IPlanningPublicationScope instead of
writing a legacy row without protection or opening a separate native
transaction.
The scope already protects settings, including an initially absent profile row.
The native assignment lock remains inside it; provider/geocoding work stays
outside. A supplied outer transaction is rejected before provider work.
Caller-owned tracked rows remain under their existing owner; operation-owned
base rows are detached as before. Commit failure rolls back both the base write
and any native planning request.

Manual live builds pass their observed stored profile separately from requested
dimensions through an internal base-build entry point. A deliberate dimension
change can prepare its route, but cannot hide an intervening stored restriction
change. The later live-plan transaction retains its stricter full-profile check.
Base-cache publication and live-plan/profile publication remain separate
commits; an independently committed cache row is not an approved live route
or profile.

This stage adds no schema, public HTTP contract, provider integration or lock
tables. It protects settings without claiming fresh canonical work capture for
standalone historical/unassigned sources. Their remaining input ownership and
cross-process revisions still need consolidation. The global fourteen-table
scope now also handles standalone base writes, which can increase contention.
Real PostgreSQL execution, runtime contention and production performance remain
unverified. Phase 1R below adds post-commit fuel road validity; historical
selection evidence remains separate.

## Phase 1R persisted fuel-road validity

TruckFuelPlanSnapshot now retains FuelRoadDependencies in its existing summary
JSON. The versioned, bounded list comes from the exact observations accepted by
publication, including the current plan, future base/fallback selection and
horizon/onward connections. It retains repeated observations. No road geometry,
historical load copies, schema migration or public HTTP field is added.

SavedRoadVersion separates progress from road identity. The publication guard
still checks both inside the owned transaction. Durable reuse stores only road
identity; normal visited/passed-stop and deviation-timer updates do not require
fuel recalculation. Plan version, assignment/owner, input signature, road time,
connection predecessor and geometry-presence changes remain invalidations.

SavedRoadValidation now exposes ISavedRoadValidation for the shared publication
and read paths. Reads batch the existing compact road/plan adapters inside one
IExecutionReadScope. Provider-specific queries remain in Infrastructure. Display
validates against fresh metadata even when its fuel summary is cached. Remaining
dispatch blocks and an onward connection participate; completed earlier blocks
do not. A mismatch marks the projected Fuel stale and clears schedule impact
and purchase-dependent stop arrivals. Stored choices remain available for
recovery and explicit editing/reset.

The existing automatic scan uses the same check before quote comparison and
requests recalculation without waiting for price changes. Manual choices and
manual starting fuel are preserved. Missing legacy evidence or an unsupported
version requires refresh; a successful calculation supplies current evidence.
Failures leave the old result for retry with its existing replacement guard.
There is no new worker, automatic road repair or changed polling cadence.

Persistence checks dependency size, hash shape, work ownership and root presence
within the existing payload limits. Test composition reuses the same validator.
SQLite tests cover warm display, cold summary round trips, unchanged progress,
root/native/future/fallback corrections, manual protection, failed refresh and
recovery. Existing strict publication checks still reject tracking changes
during calculation. Full verification and limitations are recorded in
[phase 1R evidence](core-rebuild-phase-1r-2026-09-16.md).

Post-commit history correction is deliberately separate: a corrected predecessor
can invalidate fuel before a saved road row has been rebuilt. Closing that gap
requires persisted historical selection context, including each original native
lookup batch. This stage does not substitute road versions for that evidence.
It also does not detect arbitrary geometry edits that fail to advance any
supported road version; writer ownership remains required.

The global publication lock is unchanged. Fresh display metadata checks add
bounded database reads without adding long-lived geometry copies. PostgreSQL
execution, contention and production latency/memory effects were not measured.
No provider, browser, deployment or application-database experiment was run.

## Ordering and scope characterization

WorkOrderKey preserves the existing comparator:

1. Active native execution, then other started work, then upcoming work.
2. First non-driver-only stop's local scheduled date/time; ShipDate is the date
   fallback and an unknown date sorts at DateOnly.MaxValue.
3. LoadNumber resolves equal priority and schedule. Equal complete keys retain
   input order; no new GUID tie-break was introduced.

The key makes the existing display choice inspectable. A load number is not a
physical predecessor relationship. WorkSequencePolicy now assesses the resolved
ETA chain independently of this key:

- Stored LoadExecutionLeg.Sequence establishes precedence between portions of
  the same load. A reversed explicit sequence is a conflict, even when the first
  displayed portion is active. It does not prove a saved connecting route
exists.
- One started load precedes upcoming work. Multiple started legacy loads in the
  resolved chain block the current forecast until current work is reconciled.
- Distinct local appointments provide provisional scheduling evidence only.
  Different time-zone identifiers are not compared. Two absent zone identifiers
  retain the legacy local-schedule convention, without claiming UTC chronology.
- Missing dates, tied starts or a missing time on the same day leave the order
  unresolved. ShipDate remains the date fallback. Load numbers never settle it.
- Every pair is assessed: a later unknown candidate may precede an earlier
  future candidate. ETA retains the unambiguous prefix and blocks the affected
  continuation through existing unavailable reasons.
- Incoming native work requires release and receipt confirmation from each
  non-cancelled transfer dependency. Actor presence confirms an action; a time
  alone does not. Confirmed actions with unknown times remain valid.

WorkSequenceAssessment exposes typed precedence, issues and transfer revisions.
ETA hashes this assessment with its other inputs. Geometry reuse remains keyed
separately; no sequence check requests a provider or HOS data. Native evidence
uses two batch queries per description batch, with no extra legacy-only queries.

Current scope matrix:

- Complete snapshot query: all remaining linked work, including planned and
  overdue assignments; no native-successor calculation cutoff.
- Board: request date/planned/overdue flags still control screen selection.
- Saved truck preview and live planning: existing current/upcoming scope.
- ETA: complete snapshot retained as input; the calculation subset preserves
  started overdue work and explicitly excludes unstarted overdue work.
- Rolling fuel horizon and its validation: include overdue assignments by
  explicit
  policy. Native planned/active legs follow existing execution selection.
- ETA chain continuation: retains the existing stop at a native successor
  because
  transfer readiness requires explicit connection and assignment evidence.

The ETA assessment covers only work that ETA resolves before its existing
native-continuation boundary. It does not assess excluded overdue work or detect
all competing assignments beyond that boundary. It does not activate native
successor forecasting, reorder UI rows, modify routes or confirm transfers.

Durable historical fuel validation is now implemented in the next slice below.
Consolidate remaining standalone work-input ownership.
Establish complete per-truck revision ownership before narrowing the
conservative publication lock, and measure PostgreSQL contention. Preserve the
existing result revision checks and native leg locks throughout. Ordinary
display ordering remains separate from calculation readiness and approved
financial evidence.

## Native stop normalization and durable historical validity

ExecutionLegStops replaces the mutable ExecutionLeg.StopsJson field. Typed rows
preserve accepted occurrence IDs, independent array positions, operational
facts, appointments, location provenance, actuals and correction actors.
Assignments remain leg-owned. Tracked replacements update retained rows and
remove obsolete rows under the existing aggregate transaction/revision guards.
Route, mileage, source reconciliation, workspace edits and switch cancellation
use the shared row projection. Immutable cancellation receipts remain readable.

Migration `20260917045303_NormalizeExecutionLegStops` validates legacy arrays,
rejects ambiguous or duplicate identities, inserts rows and only then drops the
old column. Downgrade reconstructs the current rows, including corrections made
after upgrade, and serializes UTC timestamps explicitly. Coordinate identities
compare decimal values independently of representation scale. Some previously
stored route hashes with trailing decimal zeros may need one recalculation;
no speed or provider-call reduction is claimed.

The synthetic PostgreSQL probe checks upgrade, typed edits, downgrade, re-upgrade
and twelve rejection/transaction-rollback cases on a separate database on the
existing server. It creates no SQL server or container. A full working-data
restore rehearsal and the release gate also passed; see the
[September 17 report](core-storage-and-fuel-history-2026-09-17.md).
The approved maintenance cutover remains a separate gate.
Old API binaries cannot read the new schema, so database-only rollout is unsafe.

Fuel now persists compact historical lookup seeds and their accepted signatures
alongside saved-road versions. Shared saved-input validation replays complete
lookup batches and compares only dependencies still affecting remaining work.
It shares one database read snapshot with road checks and bypasses the summary
cache for validation. Corrections, inserted history and newly unknown chronology
can invalidate fuel without changed road metadata or new prices. Incoming
connections already traversed and completed earlier blocks are excluded.
Automatic refresh retains its existing revision guard and manual-plan policy.

This does not migrate all imported dispatches into native assignments, unify
ordinary and transfer visit ownership, remove Dispatch.ForExecution or replace
the global publication lock with per-truck ownership. Those remain explicit
completion work; successful storage migration is not completion of the rebuild.

## Accepted native execution history

ExecutionLegRevisions records accepted native assignment versions under the
same transaction as the owning operation. It preserves leg resources and status,
ordered stop facts, load membership and boundaries, and transfer confirmations.
The immutable document has an explicit schema version; it is not a second
mutable itinerary or the input source for current planning.

The mutation inventory is transfer planning, cancellation, release/receipt,
explicit source acceptance, automatic source reconciliation, native workspace
editing and stop/resource correction. Retry receipts prevent duplicate versions.
Corrections retain the new actor and request identity; automatic synchronization
has no invented user actor. Cancellation captures removed incoming load links as
removed. Mileage consumes accepted stop order independently of source Sequence.

Migration 20260917052512_AddExecutionLegHistory captures one marked baseline for
every existing leg after stop normalization. Baseline recording time means
observation at migration, not a historical assignment start. Earlier versions,
missing actual times and original change actors remain unknown. Recording time
must not be used alone to attribute backdated fuel or toll transactions.

PostgreSQL rejects updates/deletes to accepted history, and the EF persistence
boundary applies the same rule. Downgrade is allowed only before post-baseline
history exists; subsequent repairs must move forward or use an explicitly
reviewed recovery procedure. User deletion and later display-name changes do
not erase the retained actor identity.

This completes native accepted-version retention, not the ordinary imported
assignment backfill or historical custody/allocation ownership. Existing
workspace, transfer, custody and mileage records retain their specific roles.

## Subsequent slices

1. Normalize ordinary visits and links with versioned history and a resumable
   backfill. Ambiguous rows require reconciliation, not guessed facts.
2. Move transfers/custody and mileage references onto the same identities.
3. Move remaining Route/ETA/Fuel readers and remove Dispatch.ForExecution,
   competing stop ownership and compatibility fuel writes when no reader needs
   them. Each removal has a consumer inventory and migration gate.
4. Complete cross-process revisions, work delivery and role-based workers.
   Host extraction follows evidence of need, not this document's module list.
5. Implement one real compensation agreement and one invoice workflow before
   expanding financial rules. Add the trip cost view described below, including
   fuel, toll estimates and contract-based compensation. Preserve operational
   independence; a cost projection does not require a general ledger first.

Worker enablement and the reproduced document-author defect are separate small
stabilization changes; they must not be hidden inside the itinerary extraction.

## Financial extension contract

Execution exposes accepted work and corrections. Mileage exposes selected
evidence and allocation versions. Loads exposes eligible charge-line versions.
Compensation snapshots its chosen inputs, effective contract, units, currency,
rounding and result. Approval freezes these values.

Contracts select payable basis explicitly. A missing observation does not
silently select planned miles. Team compensation requires a stated allocation;
the same physical distance may produce multiple legitimate earnings only under
the agreement, never by duplicating movements.

Accounting consumes approved obligations, not mutable screen totals. Partial
payments and linked adjustments reconcile independently. No payment execution,
tax policy or statutory payroll rules are specified by this design.

## Trip costs and toll estimates

Requested product scope: each trip should expose fuel costs with its refueling
plan, toll-road costs, driver compensation and owner/operator payments. Owners
means truck owners/owner-operators, as confirmed by the user. A financial
projection groups evidence from the owning modules; it does not become another
writer of assignments, route geometry or compensation rules. Trip membership
and boundaries must explicitly identify included loads, execution legs and
empty travel. A trip is not assumed to equal one load.

Show planned, current forecast and actual amounts with their evidence and
completeness. Planned refueling is not a confirmed purchase, a toll quote is
not a charge, and approved compensation is not proof of payment. Missing input
stays unknown; an incomplete total must not appear to be a complete trip cost.
Preserve the accepted planning baseline when updating a current forecast.

Fuel needs two distinct views: purchase cash outlay and the cost of fuel used
by the trip. Retain station, quantity, unit price, currency, timestamp and
receipt/import reference for actual purchases. Opening and closing tank fuel
can span trip boundaries. Allocate consumed fuel under an explicit valuation
policy; do not charge a whole fill to the trip containing its purchase or add
purchases and consumption together as two operating costs. Estimated station
access, uncertain readings and missing opening cost basis remain identified.
The valuation policy and actual purchase source require product decisions.

Actual fuel expense ingestion is a later integration. The initial trip view
must work with explicitly estimated fuel costs while actuals remain unavailable.
Keep the planning baseline and imported purchases separate so a later upload
can show plan-versus-actual differences without rewriting the accepted plan.
The import boundary will retain source/transaction identity, truck and payer,
purchase time separately from import time, quantity and units when available,
amount, currency and evidence reference. Attribute purchases against historical
work at the purchase time, not the truck's current assignment. Unresolved
matches remain pending reconciliation. Receiving a purchase alone does not
prove the whole purchased quantity was consumed by the matched trip.
The source and format can be selected later; no connector or speculative
financial schema is required during the current core phase.

Toll estimates bind to the selected route version and its priced coverage,
vehicle configuration, travel time assumptions and applicable payment profile.
Coverage must account for loaded/empty travel and any routed fuel access.
Geographic fuel-access estimates alone do not prove toll-free access. Retain
quote source, calculation time, currency, assumptions and unavailable segments.
Routing or relevant vehicle changes invalidate the quote; a late quote cannot
replace an estimate for a newer route. Zero is valid only for confirmed covered
segments with no charge; absent or unsupported coverage remains unknown.
Infrastructure owns the provider adapter behind an Application interface.
Provider selection, coverage, pricing and live validation are pending.
Estimate caching and refresh should remain independent of ETA provider calls.

Actual toll transactions retain their source identities and work allocation.
They reconcile with estimates without deleting the original quote or counting
the estimate again as an actual charge. Imported retries must not duplicate
fuel purchases or toll transactions. Route comparison may later show toll
differences; toll-aware optimization is a separate algorithm change.

Actual toll expense ingestion is also a later integration. Retain source and
transaction identity, original amount/currency, payer, passage location and
time when supplied, vehicle/transponder references, and posting/import times
separately. Match historical vehicle and work assignments at passage time;
later posting must not move the charge to the currently active trip. Missing
passage or assignment evidence remains a reconciliation item. A valid actual
charge can exist without a matching estimate, including an unplanned passage.
Corrections and refunds retain source links and update the appropriate actual
projection without silently rewriting approved records. Repeated imports are
idempotent. An incomplete import does not establish zero toll expense for
uncovered work. Source selection and integration can follow the estimated trip
view independently of the later fuel import.

Driver and owner/operator costs use the applicable compensation agreement and
assigned work. Record the payee, responsible payer and included cost components
explicitly. An owner/operator payment may already cover driver labor, fuel or
tolls under its agreement; overlapping amounts must not be added twice to
company cost. Company-paid charges, owner-paid charges, reimbursements and
deductions retain their own basis. A deduction can affect net settlement
without representing an additional operating expense. Concrete agreements must
define percentages, eligible revenue, rates and who bears each cost.

All calculations remain server-owned with decimal money, original currencies
and an explicit reporting conversion basis. Financial records preserve their
rate source/date/version; the mutable fuel-planning exchange setting is not
sufficient historical evidence. Work shared by loads/trips uses a versioned
allocation rule, with allocated totals reconciling to each original amount.
If revenue and margin are displayed, label the selected revenue and expense
basis; uncovered costs prevent a claim of complete net profit.

Deliver this extension in bounded slices: define trip membership and cost
evidence; add selected-route toll estimates; connect fuel forecasts and
compensation agreements; expose the combined view with explicit completeness.
Add actual fuel and toll ingestion with reconciliation in later integrations
when their sources are selected. Relevant design probes are F06-F13 in the
[acceptance catalog](../../architecture/core-rebuild-scenarios.md#financial-design-probes).
These are planned requirements, not implemented tables, APIs or UI.

## Deferred decisions and evidence gates

- Actual agreement samples, settlement period and approval roles: required
  before implementing financial rules, not before itinerary extraction.
- Trip cost membership, owner/operator cost responsibility, fuel valuation,
  and toll coverage: resolve before implementing the affected calculations.
- Actual fuel/toll sources and import formats: deferred until their later
  ingestion slices; they do not block the explicitly estimated trip view.
- External accounting export versus an internal general ledger: defer until
  accounting scope is selected. Do not build an empty ledger framework now.
- Trip lifecycle and multi-load workflow: require demonstrated product cases;
  preserve current supported behavior during the read-only first slice.
- Multi-company migration: separate workstream before second-company activation.
  Keep ownership explicit now without claiming existing tenant isolation.
- PostgreSQL restore evidence: required before persistence cutover. The separate
  synthetic fixture passed; the full working-data rehearsal is tracked with
  the migration report. No application database may become a disposable test
  fixture, and no SQL container or automatic server installation is allowed.
- Performance budgets: collect a permitted baseline before claiming gains.

## Verification and reporting

Follow docs/testing.md. Shared contracts or persistence changes require the full
test.sh suite; Client changes require its build and affected browser checks.
SQLite cannot certify PostgreSQL concurrency or migrations.

Each stage report lists implemented behavior, removed paths, checks actually
run, remaining compatibility, unverified assumptions and the next bounded slice.
Archive dated evidence; update this specification only as decisions change.
No rollout is implied by local completion.

[execution]: ../../architecture/dispatch-execution-and-settlements.md
[saas]: ../../architecture/saas-evolution-plan.md
