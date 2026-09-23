# Fleet efficiency implementation

## Contract

The target is 100 active trucks without work proportional to retained GPS
history or the number of open dispatcher screens. This is an implementation
plan, not a production capacity claim. Existing assignment publication guards,
company isolation, fuel warnings and retained display behavior remain mandatory.

## Required read and publication ownership

These rules apply to new Dispatch, Fleet Map and planning consumers. The sections
below also retain design constraints and measurement gates; they are not a claim
that every proposed optimization has been implemented.

| Responsibility | Existing owner |
| --- | --- |
| Business formulas and route matching | Domain rules |
| Durable route and movement state | RoutePlanStore / RoutePlanStorage |
| Durable fuel selection | TruckFuelPlans and its Infrastructure store |
| Fuel hand-over records | FuelIssueRecords |
| Sending a hand-over to a driver | FuelIssueSender |
| Provider delivery statuses and inbound windows | WhatsAppWebhookHandlers |
| Messaging transport | IDriverMessaging (WhatsAppCloudMessaging) |
| Driver contacts | UpdateDriverContact / DriverContactImport |
| Trailer catalog | TrailerCatalog |
| A truck's current trailer | TruckTrailerAssignments (targeted after a committed request by TruckTrailerRefreshBehavior) |
| Heavy preparation and durable demand | PlanningRefreshOperation |
| Shared display snapshots | PlanningSummaryCache |
| Board planning reads | BoardPlanningReader |
| Selected-truck planning reads | PlanningSummaryReader |
| Committed change notification | PlanningWorkPublication |
| Prepared-result publication | PlanningSummaryPublisher |
| Cold/freshness recovery and running-work demand | PlanningSummaryOperation |
| Client polling and retained display | PlanningDisplayCache |
| Provider transport and SQL | Infrastructure interfaces |

PlanningReadService is the background reconstruction owner. Do not inject it into
new board/map HTTP handlers to repeat heavy saved-fuel validation on every poll.
Handlers may resolve current work, authorization and compact metadata. Opening a
page must not synchronously recalculate routes, scan fuel geometry or fetch
provider history. Explicit user calculation commands remain separate writes.

Running work is prepared whether or not a page is open. Every thirty seconds,
per carrier, PlanningSummaryOperation reads the trucks with running work - an
execution leg that is active or planned, or an older in-transit load without a
leg - batch-reads their planning inputs, and asks PlanningSummaryCache for their
summaries exactly as a reader would. The same consumers prepare them on the
same path, so a road or fuel plan is rebuilt only when its inputs changed, and
the ETA worker keeps their forecasts because the summary read views them.
Finished and cancelled work never enters this set. At most 128 trucks are held
this way, those already driving first, so trucks somebody opens are never
crowded out. A click
opens what is already prepared; it is not the start of a calculation. The
thirty-second summary refresh of each running truck is a projection of saved
route and fuel state, not a geometry or fuel rebuild; its cost at 100 trucks has
not been measured.

A saved fuel plan's hand-over state - which stops are this shift's, and which
were sent - is set when TruckFuelPlans projects it for display, from the hours
already in the planning inputs and one query of that truck's hand-overs. It
reaches Fleet Map and Dispatch through the same summary, never per card. A
confirmed hand-over asks for that one truck's summary after it commits.

For a new displayed field, first find its existing calculation and durable owner.
Add the value to the shared display projection when both screens need it. Carry
it through the server/Client contracts and format it in Client. Do not create a
second calculator, page-specific cache or repeated per-card HTTP request.

For a route/profile/fuel/progress write using PlanningWorkPublication, use its
CommitAsync boundary and perform related read-cache invalidation before summary
notification. Never publish a speculative result at SaveChanges or on rollback.
For another write owner, identify its actual commit boundary and invalidate all
affected assignments/trucks there. The itinerary signature remains the read-side
protection for changed work; a cache ticket is not a durable database revision.

Prepared snapshots are isolated from mutable calculation objects. Publication
must check company, work/settings signature and the current ticket. A delayed
worker may not overwrite a newer commit, restored assignment or evicted entry.
Keep the previous matching snapshot visible while refreshing. Never reuse one
carrier's, truck's or assignment's result as an error fallback.

Planning inputs and profile rows use company-scoped, per-truck cache entries.
Overlapping pages reuse existing entries and batch-load only missing trucks.
After committing a truck change, invalidate its `planning-inputs` entry through
`PlanningWorkPublication` or `RoutePreparationQueue.MarkTruckDirty`. Assignment
transfers must notify both the previous and receiving trucks. Profile writes
also invalidate the matching `profile-rows` entry. Never invalidate before the
owning transaction commits.

Global board, dispatch, execution and route-preview generations do not invalidate
these planning-input entries. Shared settings, fleet catalog changes and the UTC
date remain common dependencies because they can affect multiple trucks. This
broader read refresh does not recalculate or rewrite every saved route/fuel plan.
Keep batch caches within the existing bounded `ReadCache` memory budget.

## Consistency contract

Every prepared or published result names the facts it was made from, and
is checked against them where it is published. There is no global counter:
each result depends on the versions below and on nothing else, so a change
elsewhere neither invalidates it nor is hidden by it.

| Result | Checked against at publication | Not a dependency |
| --- | --- | --- |
| Route and geometry | company, accepted assignment or leg revision, itinerary input signature, truck profile, saved road versions | contacts, prices, ETA |
| Fuel plan | the route's dependencies, telemetry observation, price materiality, `CalculatedAt` compare-and-swap | contacts, ETA |
| ETA forecast | company, work key (load, leg, assignment, stops), chain input hash, road plan id and version | contacts, prices |
| Summary snapshot | company and truck key, work and settings signature, cache ticket | - |
| Fuel hand-over | company, truck, leg or load, assignment revision, station, stop before, content (fill or gallons) | wording, miles ahead, ETA, price |
| WhatsApp message | idempotency key: assignment, visits with content, recipient; provider message id | later plan versions |
| Truck's current trailer | stored telemetry word, active leg or in-transit load at its current stop, trailer active, one truck per trailer | planned or finished loads, a missing provider record |

- Calculate outside a long database transaction. Publish through the
  owner above, which re-reads the dependencies inside its transaction and
  refuses a result whose inputs moved.
- Order is commit, then read-cache invalidation, then shared summary
  notification. A failed commit notifies nothing.
- A late result never replaces a newer one: stores compare-and-swap or
  compare timestamps, the summary cache compares tickets, and ETA memory
  keeps the newer road version of the same work.
- Work planning refuses (it needs review, it has conflicting truck
  assignments) is answered with that reason for the inputs it was refused
  for, not left as "updating"; a refusal because the work moved under the
  calculation is not stored.
- A result for the same work stays visible with honest freshness
  (`IsRefreshing`, `RouteUpdatePending`, "Changed since sent"). A result
  for other work - another company, truck, assignment or stops - is never
  shown as current.
- A send to a driver is an external operation. Its content, recipient and
  visits are fixed in an attempt row committed before the provider call;
  the plan version is checked on acceptance and again right before the
  call. The database and the provider are not atomic and a lost answer is
  not exactly-once: it is recorded as unknown and repeated only when a
  dispatcher explicitly sends again. A plan that changes during the call
  keeps the old hand-over and reads as changed since sent.
- Provider statuses apply only to the message with that provider id and
  only move forward; a failed or read message never moves back.

A change that adds a dependency, a publisher or a retained result states
which of these rows it belongs to and adds a controlled-interleaving
regression where it touches one. Existing coverage:
`PlanningSummaryCacheTests`, `PlanningPublicationTests`,
`TruckFuelPlanReplacementTests`, `EtaRetainedForecastTests`,
`FuelIssueRecordsTests`, `FuelIssueSenderTests`, `WhatsAppWebhookTests`,
`TrailerCatalogTests`, `TruckTrailerAssignmentTests` and
`TruckTrailerRefreshTests` and `SourceCancellationTests`.

## Preventing repeated database and provider work

- Batch identities, profiles, names and ETA inputs for the visible page before
  projection. Do not put a database/provider call inside the per-card loop.
- Read only the columns needed by the consumer. Metadata-only reads must not
  hydrate route chunks, checked fuel geometry, full HOS history or raw payloads.
- Reuse the existing shared readers and generation keys. A second cache with a
  different invalidation rule is another consistency problem, not an optimization.
- Coalesce concurrent demand by company and accepted work. Do not launch a new
  background calculation for every poll, tab, map selection or page component.
- Do not save unchanged state on every read or timer tick. Geometry is immutable;
  state-only changes must not rewrite its chunks. Durable business writes remain
  transactional; the display cache is disposable and is not a write-behind ledger.
- Keep cold work and retained payloads bounded. Planning summaries allow two
  consumers, 256 entries, 512 KiB per entry and 8 MiB total payload. Demand expires
  after two minutes without reads; running work is asked for again by the
  background pass. Dispatch uses compact metadata; maps request
  geometry only when the acknowledged plan/version changes.
- Preserve the 30-second recovery/freshness path until all relevant inputs have
  an equivalent reliable lifecycle. Telemetry, ETA and other API processes can
  change independently. Do not claim a purely event-driven or distributed cache.
- Add regression coverage when a query fan-out or duplicate calculation is fixed.
  Assert call counts/loaded data and identity invariants, not wall-clock timing.
  Measure cold and warm reads separately; include background SQL, allocations and
  provider work instead of merely moving their cost outside the HTTP timer.

Before review, identify the read owner, write owner, invalidation boundary,
company/work/version key, payload bound and cold/failure behavior. New read paths
must explain why an existing shared reader cannot serve them. Run the affected
checks from [test selection](../testing.md), including architecture; shared
contracts, persistence and dependency injection require the full suite.

The summary's 8 MiB payload budget is additional to CacheBudgets' 80 MiB
partitions. Neither sum caps process RSS, temporary allocations, runtime heaps or
all integration caches. Never estimate total server memory by counting only
retained truck DTOs. On each API process serving summaries, keep the
PlanningSummary background role enabled; cold entries otherwise cannot recover.

## Work sequence

1. Separate mutable route state from immutable geometry chunks. A logical route
   owns an ordered manifest of chunk references, not full geometry snapshots.
   State-only updates must not rewrite chunks. Legacy rows remain readable.
2. Record bounded movement segments from received telemetry: route-version ranges,
   simplified deviations and explicit gaps. No recurring daily-history requests.
   Preserve pinned stop, reroute and gap boundaries.
3. Reuse exact route indexes, evaluate new GPS observations once and confirm
   deviation with freshness, persistence and hysteresis. Never infer actual
   completion or a traveled road from missing observations.
4. Coalesce planning demand, retain publication guards and bound heavy work.
   Fuel validation and price refresh must not automatically rebuild geometry.
5. Share a bounded cache budget with explicit entry estimates and bounded cold
   work. Eviction must not remove durable work or current saved results.
6. Reuse existing geometry-version acknowledgements on the Client. Read history
   on demand and do not resend unchanged full roads.
7. Record compact recalculation evidence and retain road versions referenced by
   history. Evidence does not promise replay of unrecorded provider responses.
8. Verify invariants, migrations and synthetic 100-truck bursts. Report measured
   allocations separately from container memory and real provider cost/latency.

Implementation status and dated measurements belong in the archive. No migration
is applied to the application database or production without release approval.
No binary coordinate format is introduced without a measured need.

## Revised segment design

A computational segment contains provider distance and duration, ordered event
positions, and a reference to immutable geometry. Geometry storage blocks are
not independent provider requests. Mandatory stops and assignment boundaries
remain explicit. A long motorway does not need one calculation per bend.

Each retained matching anchor must carry its original cumulative road distance
and duration, source vertex/edge identity, and an error bound. Merely assigning
an original leg's total miles to a simplified polyline is incorrect: the current
RouteGeometry constructor distributes distance by geometric segment length.
Uneven simplification changes intermediate progress even when totals agree.
The characterization test preserves this counterexample; it is not a new
algorithm or a desired production behavior.

Display geometry may use a visual tolerance. Matching geometry needs a separate
acceptance policy. Begin by sweeping 2, 5, 10 and 20 metres in offline fixtures;
none is an approved universal production tolerance. Pin stop, station access,
reroute, gap and segment boundaries. A geometric simplifier cannot identify a
junction or distinguish parallel roads without additional evidence. Retain exact
local geometry when matching is ambiguous, near a decision threshold, or near a
pinned boundary. Do not claim that a small positional error bounds progress error
on overlapping roads or loops.

Use prior segment/progress, observation time and direction to narrow matching.
Reject stale and duplicate observations. A discontinuous GPS jump expands the
search and can produce an unknown match; it cannot manufacture passed stops.
Cumulative time needs an explicit interpolation assumption if the provider only
supplies per-leg duration; it is not an observed or provider segment speed.

## Replacement and history

A route is an ordered manifest of immutable segments. On deviation, select a
forward reconnect candidate before the next mandatory stop. Validate the new
connection and all reused suffix inputs, including truck restrictions, vias and
route choice. Reject backward, ambiguous and materially inferior connections.
If candidate evaluation would require excessive requests, use one remaining-route
calculation. Smaller geometry does not imply cheaper provider billing.

History references the immutable segment and distance interval for confidently
matched movement. Off-route observations form bounded simplified chunks with
pinned endpoints. Gaps remain explicit. Close and simplify chunks once, not the
whole trip after every observation. Flush batches with idempotent identities and
persist a checkpoint; crash recovery must not duplicate history. Unflushed data
loss and retention must be explicit policies before implementation. No fixed
retention period or irreversible purge is approved by this design.

Old chunks remain while the active manifest or deviation events reference them.
Recalculation evidence records reason, input/version identities and result differences; exact
provider replay is not promised. Financial/IFTA/toll evidence is a separate owner.

## Test and rollout gates

| Gate | Evidence required before replacing the current path |
| --- | --- |
| Summaries | Original leg miles/time and mandatory stops remain unchanged |
| Progress | Original cumulative measures survive uneven point removal |
| Geometry | Bound positional error; keep bends, loops and pinned boundaries |
| Matching | Parallel roads, intersections, reverse travel and GPS jumps |
| Freshness | Duplicates/stale GPS do not advance state or request work |
| Rerouting | Only changed geometry replaced; incompatible suffix rejected |
| Fuel/ETA | Recompute dependent values without unnecessary road requests |
| History | Explicit gaps, restart idempotency and immutable references |
| Isolation | Company/assignment changes cannot reuse foreign state |
| Load | 100 independent trucks, burst GPS and simultaneous deviations |
| Bounds | Cache eviction, full queue, slow provider and cancellation |
| Client | Existing known-version reads retain geometry without retransmit |
| Migration | Legacy read, backfill, concurrent writes and rollback policy |

First establish characterization tests against existing code. Then implement the
cumulative-measure representation and differential tests against exact matching.
Next introduce storage behind current read/publication boundaries, with an isolated
database migration fixture. Follow with bounded history and coalesced scheduling.
Finally run end-to-end replay and browser checks before an approved release.

The load harness must report cold/warm allocations, retained memory after
collection, matching latency percentiles, queue age, provider-call counts and
serialized bytes. Test ordinary driving, noisy GPS, curves/loops, a 100-truck
burst and a sustained run. Compare the same inputs before and after; wall-clock
assertions do not belong in unit tests. Synthetic replay cannot establish real
provider latency/cost or Cloud Run capacity. Set production budgets from the
baseline measurements rather than treating 512 MiB as a guaranteed target.

## Shared ownership, not parallel algorithms

Domain owns one cumulative-measure and matching contract. Application owns
versioned publication, movement state and bounded scheduling. Infrastructure
owns persistence, migrations and provider transport. Client only renders and
formats the supplied results; fuel formulas stay on the server.

Reuse the existing block-refinement mechanism in FuelSearchGeometry when
introducing a shared matching owner. It already prunes remote blocks and refines
nearby original segments. Do not introduce a second independent nearest-road
algorithm for telemetry. Preserve an independent exact matcher as a differential
test oracle before replacing RouteGeometry, or tests comparing the two names
would become self-comparisons. Any borrowed geometry must remain immutable for
the index lifetime; mutable request objects cannot enter a shared cache.

Keep the existing durable planning request versions, leases, retry deadlines and
publication checks. Extend their ownership rather than adding another queue.
Reuse Client known-plan/version acknowledgements and display simplification.
Introduce storage and contract changes through one read owner with a bounded
legacy migration path, then remove obsolete readers. No silent fallback planner,
per-truck exception or duplicate source of route truth is acceptable.

## Shared index implementation boundary

The first implementation extracts block refinement into RouteGeometryIndex,
shared by the established RouteGeometry and FuelSearchGeometry APIs. Cached
geometry captures private coordinate arrays; request-scoped fuel searches borrow
checked points. Cumulative block miles preserve exact local refinement. The
previous expanded matcher is retained only under test Support as an independent
oracle. This preserves shared exact matching ownership. The implementation below adds
chunk storage and movement persistence without claiming a production rollout.

## Clarified route identity and deviations

There is one logical route for an accepted execution scope. Its current manifest
is an ordered list of immutable chunk references and covered distance intervals.
A manifest revision is an optimistic concurrency token, not a duplicated road.
Stop-to-stop legs remain the business boundaries; storage chunks may be smaller.

A deviation stores its departure position/time, the replaced reference interval,
new chunk references, and a confirmed rejoin anchor when available. Unchanged
prefix and suffix references are reused. History stores splice events and actual
observation evidence, not complete geometry snapshots per reroute. A GPS point
alone cannot determine which road was taken between observations. Missing
observations remain gaps; a newly calculated road is planned, not actual travel.

For example, [A, B, C, D] can become [A, B-prefix, X, Y, C-suffix, D]. B-prefix
and C-suffix refer to ranges in existing chunks. Only X and Y need new geometry.
Pin mandatory stops and reject a splice that skips one. All reused suffix inputs
must still match the assignment, truck restrictions and route choice. A different
final destination can require replacement of the entire remaining suffix.

Required splice tests: unchanged chunks preserve identity, interval split keeps
original cumulative measures, reversing/looping roads require an unambiguous
forward rejoin, retries are idempotent, stale manifest revisions fail, historical
references survive, company boundaries hold, and a state-only update writes no
geometry. Boundary coordinates alone must not serve as chunk identity.

The unpublished whole-road RouteGeometryVersions experiment was removed after
this clarification. Its migration was never applied to an application database.
Do not reintroduce whole-road snapshots as the durable history representation.

## Implemented operating boundaries

The implemented representation retains exact original geometry in bounded chunks.
It does not substitute sparse display points for calculation distances. Current
and reference manifests reuse coordinate ranges and retain original leg measures.
State updates use a geometry fingerprint to avoid packing or writing unchanged
chunks. Replacement receipts and a streaming replay reader preserve older roads
without retaining complete snapshots of every version.

Automatic ordinary reconnect uses the next mandatory stop, then reuses matching
later legs. This is an unambiguous boundary that preserves their provider measures.
Internal reconnect anchors remain subject to the same distance-preservation rule;
no extra speculative provider requests are introduced to search for them.

Movement capture runs in the existing background planning path and consumes its
received telemetry. Open deviations are limited to 64 observations; completed
chunks simplify once, pinning endpoints. Continuous matched estimates retain two
observations and a distance interval. A one-minute gap, implausible displacement,
ambiguous match, reversal or assignment boundary prevents an invented continuous
road. Existing polling/queue delays can therefore appear as gaps: this is not a
new high-frequency recorder and does not promise complete GPS coverage.

Uncommitted observations can be lost on process failure. The next capture resumes
from the durable checkpoint and explicitly represents missing evidence; it does
not issue daily-history requests to fill it. No automatic retention purge was
added. Parent operational-plan deletion retains the existing cascade lifecycle;
financial evidence needs its own retention owner.

Existing bounded queues, leases, coalescing, provider budgets and Client known
geometry acknowledgements remain the owners of scheduling and publication. Cache
partitions total 80 MiB in estimated entries across reads, road display, exact
indexes, fuel and requested history. These partitions do not cap all process
memory. Measurements must distinguish logical JSON, PostgreSQL stored-column
size, allocations, retained managed memory and total container RSS.

Local verification and measurement evidence is recorded in the
[September 21 report](../archive/2026-09/route-chunks-measurements-2026-09-21.md).
The authorized working-database migration, publication and subsequent live
observations are in the
[release record](../archive/2026-09/route-chunks-release-2026-09-21.md).
