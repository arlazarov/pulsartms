# Project architecture

The layer boundaries in `AGENTS.md` are mandatory. Existing modules may contain
violations and must not be treated as permission to repeat them.

## Shipment and Border ownership

Ordinary shipments belong to commercial loads and remain usable for domestic
work. Border owns independent crossing drafts, selected shipment revisions and
crossing-specific crew/equipment snapshots. It does not own accepted execution,
freight billing or the existing Trip grouping. Border preparation requires Admin
because its personal identity details have a separate protected read/write path;
ordinary shipments retain Dispatch access. An optional future filing adapter
belongs in Infrastructure. See [current behavior](features/border-preparation.md).

## Dependency boundaries

Company identity follows the authenticated request or an explicit background
pass. Fleet metadata, telemetry snapshots, HOS clocks and fuel-price signatures
must not reuse another company's cache entries. Provider cursors and refresh
cooldowns belong to that same company. The fleet synchronization lease remains
server-wide; its checkpoint retains independent company states. Legacy root
checkpoint fields belong only to the original carrier.

Runtime EF writes reject missing company context, foreign ownership and changes
of ownership, including updates and deletes. Tooling contexts without a company
service retain their explicit bootstrap behavior. Raw SQL has separate ownership
checks. Expense commands validate resource and target-load references through
scoped reads before writing; query filters alone do not validate foreign keys.

Integration credentials use company/provider identity and protection purposes.
Deployment credentials and the configured Gmail push mailbox belong to the
original carrier. A validated push selects that owner before importing; arbitrary
notification content cannot select another company. Gmail watch checkpoints are
also company-specific. This does not add multi-mailbox push configuration.

- Application has no dependency on API or concrete Infrastructure. Required
  external capabilities are expressed as Application interfaces.
- Infrastructure has no dependency on API. It implements Application interfaces
  and calls Application capabilities only through interfaces. It must not depend
  on concrete Application services or handlers, or construct Application requests
  directly. Contract data types are permitted as part of interface signatures.
- API's composition root references Application and Infrastructure for dependency
  injection registration. API request handling communicates with Application only
  through MediatR. It must not reference Domain or access database contexts.
- Business checks belong in Application handlers, not controllers. Concrete
  implementations must remain behind their interfaces, including background work.

Background operation implementations and their queues live in Application.
Feature operations are grouped under Routing/Background, Eta/Background,
Fleet/Background, and Fuel/Background; Synchronization owns shared fleet scheduling
and checkpoint contracts. Feature-specific lifecycle policies stay in their feature.
Infrastructure's generic ApplicationWorker hosts only IBackgroundOperation
interfaces. ISynchronizationStore owns persistence and IReadCache provides the
cross-layer cache contract. Infrastructure registers database startup/health and
the authentication/session startup filter; API does not resolve database types.
Administrative auditing runs in the Application pipeline. API diagnostics dispatch
a query through MediatR rather than reading static metrics directly.

Shared read caching lives in Application/Caching. Fleet owns its server telemetry
snapshot. Routing services are grouped into Routes, Addresses, Deadheads, and
FuelPlanning. RoutePlanningService coordinates the route lifecycle;
TruckPlanningProfileService owns profile persistence and effective fleet defaults,
and RoutePlanStore owns saved-plan writes, optimistic concurrency, and read-cache
invalidation. Geometry and optimization remain pure Algorithms.

Profile writes are internal persistence operations requiring an existing
transaction. Explicit profile saves capture remaining truck work and validate
it inside PlanningWorkPublication; completed or superseded work cannot choose
the truck to update. Route builds and fuel publication own their profile writes
inside the same transaction as their results. Profile cache invalidation occurs
after commit. Unused fuel clearing, recommendation and road-replacement writers
have been removed; durable legacy result fields remain readable.

Selected truck temperature uses the independent Google Weather endpoint described
in [truck weather](features/truck-weather.md). Its shared ten-minute per-truck
cache uses confirmed GPS and does not trigger route or HOS reads. Legacy Samsara
sensor fields remain readable, but the incremental GPS/engine/fuel feed must keep
its original parameter set: adding decorations invalidates its saved cursor.
The high-frequency location job runs independently of feed failures or backoff.
Only snapshot publication is serialized; provider waits do not hold that gate.
Both jobs merge GPS monotonically into the checkpoint without replacing newer
sensor values or advancing another job's cursor.

The existing high-frequency Samsara stream reads structured `location.address`
fields, including postal code and country, with legacy formatted-address support.
Infrastructure formats only supplied components. Fleet snapshots reuse the newest
stream observation when it is no older than the stats/feed GPS,
updating position and GPS timestamp together while preserving independent sensor
readings. This applies to synchronized and direct reads without extra provider
calls, browser geocoding or persistence changes. Missing postal codes/countries
are not inferred from nearby stops, previous positions or state abbreviations.

Dispatch owns provider-free load creation and workspace receipts. Native creation
and imports allocate display numbers through one transactional counter. A durable
DispatchSourceLink maps a provider-scoped external key to the internal load ID;
matching display numbers cannot merge unrelated loads. Torque-specific mapping
and HTTP calls remain in Infrastructure. DispatchImport selects an optional
adapter, and disabling it removes only the import loop and provider registration.
See [optional load imports](features/dispatch-import.md) for identity, configuration
and the clean-transition boundary.

ExecutionWorkReader owns shared current/upcoming selection for the Dispatch
index and complete itinerary reader. Its immutable
references retain load/leg/driver identities, revisions and the existing
ordering key. Selection operates on captured work and commercial labels without
constructing Dispatch screen models. ExecutionLoadSnapshot keeps immutable work
separate from its immutable display details.
WorkSequencePolicy assesses precedence and transfer readiness separately from
display order. Unresolved order blocks ETA at the affected work without
reordering the Board. Saved preview and live planning now consume complete
itineraries; their paged Board reads enumerate trucks, not authoritative work.
Automatic planning and route-choice orchestration now use the same captured
selection. Route building and progress writes retain the captured truck and
leg under their gate, validate fresh work before calculation, and compare the
complete signature again after provider work before saving. Fuel search and
validation use the complete itinerary through IFuelWorkInputsReader.

Native ExecutionLeg stops are typed ExecutionLegStop rows, with the leg owning
truck/trailer assignment, default drivers and optimistic revision. Explicit
driver/co-driver corrections belong to accepted stops and preserve the execution
path and transfer confirmations. Position is the single accepted
order; consumer sequence numbers derive from it. Source Sequence remains with
the imported load stops and cannot reorder execution. An occurrence identity may
continue in another leg. ExecutionStopRows replaces tracked rows without losing
their keys
and projects detached facts for existing consumers. The leg navigation loads
with its aggregate, and the publication scope protects the new table. Immutable
switch cancellation receipts retain their historical JSON contract. Ordinary
and transfer stops use the same accepted rows. SwitchParticipant owns release
and receipt independently; ExecutionTransferVisit is a detached read projection,
not a second persisted confirmation or location. Its source visit reference
lives on the accepted stop. Unresolved source-only work remains under review until ordinary assignment or
explicit transfer acceptance. Planned source transfers do not imply active work
or either handoff confirmation; preview and acceptance share source eligibility.
The full core rebuild is not complete.
See the [core specification](architecture/core-rebuild.md) for migration status.

ExecutionAcceptance owns accepted stop replacement, revision advancement,
immutable history and durable planning demand for initial assignments, source
reconciliation, manual edits and transfer planning/cancellation/confirmation.
New legs start at revision one; existing revisions are locked before changes.
The owning command prepares resource, custody, confirmation and load-link facts
inside the same transaction. Transfer source references are captured with the
accepted boundary before history is recorded.

Protected movement paths require review; unaffected adjacent paths remain usable.
Transfer splitting uses the same protection as stop edits and retires obsolete
planned paths. Cancellation uses the common planned-mileage retirement owner.
Independent release/receipt and their unknown actual times keep their existing
rules. Source-review previews share the acceptance path protection instead of
using a separate endpoint-only check.

InitialExecutionAssignment accepts explicit ordinary assignments from the load
correction editor and truck-start confirmation. One resolved resource set
across the complete truck itinerary creates a Trip, ExecutionLeg, typed stops,
load link, immutable revision and planning request in the caller's
serializable transaction. Unique load-link sequence and serializable reads
reject competing initial acceptance. Mixed resources, driver-only travel and
invalid source times remain source facts requiring review; no handoff or
chronology is invented. Workspace readiness remains visible even after import
replaces its source notice. An existing accepted assignment cannot be cleared
by the legacy truck override.

Automatic ordinary imports also use InitialExecutionAssignment. Source resource
proposals and accepted assignments retain separate signatures, including unresolved
names and resolved identities. A changed, resolved ordinary source assignment
replaces the accepted truck, trailer and crew automatically, including local
resource overrides. The provider-neutral import proposal is authoritative;
provider mapping remains in Infrastructure. Replacement records one accepted
revision, retires obsolete planned mileage and requests planning for both trucks.
Repeated unchanged observations do not create revisions. Unknown resources,
contradictory header/visit assignments and transfer or shared-load boundaries
remain explicit data problems rather than guessed assignments. The latest normalized source resource proposal is retained
before local overrides and exposed beside accepted work in the Assignments tab.
Accepted assignments with source-review warnings remain calculable using their
accepted stops and resources. Planning responses retain the review warning;
calculation does not accept changed source facts or release reserved resources.
Fuel calculation, replacement and saved-plan reads follow the same eligibility
for a planned accepted root. Scope, revision and receipt checks still apply.
Fuel reset, editing and calculation share route selection: an earlier leg is
skipped only when its saved route has passed all stops and still matches the
accepted inputs. This does not complete or release that execution assignment.
The first remaining ordinary accepted assignment may use fresh truck GPS even
while planned; a resource conflict does not turn its calculation into a future
route. Incoming work awaiting a handoff remains gated by receipt. Future loads
retain their own origin and are not advanced by the current truck location.
Missing paths, ambiguous truck assignments and missing transfers still block
calculation. Source-only work still requires acceptance before calculation.
Review-only warnings and their removal request planning revalidation without
advancing accepted revisions or adding accepted history.
Unaccepted imports retain their review reason and block their
planning segment. Matching actual visits update ordinary planned work even when resource conflicts
prevent activation. The conflict stays visible, and the leg retains planned
status until its resources are available. Unchanged repeated observations do
not add execution revisions. Transfer receipt remains mandatory. Import actors
are nullable; recording time does not become actual start time.

Stop completion and operation commands read accepted visits when present and
use ExecutionAcceptance for their writes. Their remaining source mirrors
are compatibility state, not planning ownership. Initial assignment without actual facts
remains planned. Explicit actual work activates ordinary execution, and
complete delivery closes it without inventing unavailable timestamps. Incoming
transfer work still requires receipt; legacy stop actions cannot confirm
transfer boundaries.

ExecutionLegRevisions retains immutable accepted native assignment versions.
Application's ExecutionHistory records the final leg, ordered stops, load links
and transfer confirmation facts in the same transaction as each owning mutation.
Source synchronization records a system operation; user actions retain their
actor and retry correlation. Mere source observations and route-choice changes
are not accepted execution revisions. Current planners still read current facts.
History is keyed by leg and revision, not inferred from a truck's current state.
Its recording instant is not an actual visit or assignment effective time.
The clean transition requires old operational data to be reset explicitly;
it creates no fictional baseline or backfilled events. Persistence rejects
history rewrites, and downgrade refuses to discard new accepted work.

ExecutionPlanningChanges commits native planning and mileage requests with
their originating mutation. Infrastructure claims due requests atomically on
PostgreSQL and skips rows held by another worker. Completion requires the
same unexpired lease; a former owner cannot complete or reschedule recovered
work. Failed requests retain their retry deadline, and each request has its
own acknowledgement, so finishing an older assignment cannot consume a newer
request. This durable outbox remains separate from source road preparation.

On-demand route refresh uses PlanningRefreshRequests through Application's
IPlanningRefreshStore. A request commits before the read response reports it as
queued. Its scope retains load, optional execution leg and assignment revision.
An input signature includes the captured itinerary, complete effective profile
and route choice. Changed inputs advance a durable request version; repeated
demand preserves the current attempt and retry deadline. PostgreSQL claims
skip locked rows. Completion requires the original unexpired lease and can
acknowledge only the captured version, leaving newer demand pending.

PlanningRefreshOperation bounds concurrency, retires removed or superseded
assignments without a provider call, and retries failures with durable backoff.
Shutdown leaves unfinished claims for expiry recovery. A local signal only
wakes consumers; polling discovers work from other processes or after restart.
A five-second memory memo coalesces demand writes and supplies display status;
it is never the owner of pending work. Successful cooldowns are also durable.
Completed records expire after seven days. Source-road preparation persists its
own demand, input versions and lease/retry state through ISourceRoadStore.
None of these delivery leases replace the calculation publication guard or
justify narrower locks.

TruckItineraryReader supplies the internal GetTruckItineraryQuery with all
remaining linked work, including planned and overdue assignments. It reuses
the selection batch and native hydration, retains visits excluded from a
continuous truck path, and reports assignment/source/transfer problems
explicitly. The immutable snapshot contains visit facts, resource and
assignment revisions, boundary references, transfer facts and precedence
assessment. IExecutionReadScope keeps its queries in one database snapshot.
Infrastructure selects PostgreSQL repeatable-read or SQLite serializable
isolation; weaker outer transactions are rejected. MatchesAsync requires a
fresh snapshot and compares the projected content signature, including
membership. It is a read-time check, not an atomic publication guard or
historical reconstruction. ReadManyAsync shares selection/hydration across
requested trucks and isolates their evidence.

TruckPlanningInputsReader shares capture and driver resolution between bounded
display reads and uncached fresh mutation reads. HOS is read after the capture
transaction. Route-choice drafts persist the input signature and its as-of
instant; previews without that stamp require recalculation. Preview/save current
selection uses the same captured queue, retaining existing saved-road, GPS,
revision and owner checks. Route building also validates its capture before
writing a base-road cache entry. PlanningWorkPublication now validates the
complete signature inside an owned IPlanningPublicationScope and returns that
same transaction for the result writes. Route build/progress, captured base-road
writes, choice preview/save, fuel search/edit and ETA use this boundary.
Standalone and historical base roads now capture the common immutable
RouteWorkSnapshot also used by deadhead history and saved fuel lookup seeds.
Routing dimensions and supplied coordinates are copied before asynchronous work.
Before publication, standalone preparation reloads its accepted leg or source
itinerary inside IPlanningPublicationScope and compares stops, assignment and
route choice. A source acquiring native execution invalidates its former source
scope. Number-only assignment retains the existing fleet resolution policy.
The saved road's observed identity, input hash and calculation time must also
match, so another publication cannot be overwritten by a late calculation.
Effective routing settings are validated uncached before provider work and again
inside publication. The same
profile guard protects route-choice preview and save. Base preparation invoked
by a manual route build keeps the requested dimensions separate from the
observed stored profile; an intervening routing edit rejects the base write.
Work without a truck uses the existing default-profile identity. Fuel-only
settings do not reject road calculation. These checks do not normalize
historical work or make a base-cache write atomic with a subsequent live-plan
transaction. Base geometry, historical connections, coordinate policy and their hashes now
consume immutable facts directly. Financial persistence receives its explicit
value inputs. Live route/ETA/fuel orchestration still has compatibility reads;
those remaining adapters are tracked separately.

Infrastructure opens a fresh publication transaction before validating work.
PostgreSQL uses a repeatable-read snapshot and PlanningInputRevisions rows.
A known truck publication shares the global guard and exclusively owns its
truck revision, acquired with NOWAIT. Unresolved source membership and automatic
exchange-rate publication own the global guard exclusively. Busy ownership is
an expected planning retry, including provider-wrapped lock/serialization errors.

Database triggers cover the complete work, history, settings and saved-road
writer inventory. They advance every affected old and new truck, including
number-only assignments, removed links and missing profile insertion. Shared
resource/catalog changes own the global guard; ordinary truck writes share it.
Revisions survive deletion so the old membership remains protected. New truck
identities and number changes serialize with publications because they can
resolve previously unmatched source work. The exchange checkpoint participates;
unrelated checkpoint rows do not acquire planning ownership. Provider SQL and
trigger registration stay in Infrastructure. The unapplied core migration owns
the table, triggers and normalized truck-number index.

SQLite uses serializable isolation. Outer transactions and unsupported providers
are rejected. Independent unrelated trucks can publish concurrently; shared
source loads or global resource changes may still coordinate multiple trucks.
Provider calls and calculation stay outside. Production contention and latency
have not been measured. See the [core specification](architecture/core-rebuild.md)
for protected inputs and independent observation policies.

ETA now consumes these snapshots. Board rows select trucks only. ETA
preparation uses a pure compatibility projection for route algorithms, never
repeats mutable assignment resolution, and retains the complete snapshot plus
typed calculation exclusions. Its database reads share a fresh
IExecutionReadScope; existing profile/settings caches remain separate.
DeadheadHistoryService captures immutable current/predecessor facts, including
completed native legs and unknown-start evidence, inside one
IExecutionReadScope. Selection and native hydration share that database
snapshot. Supplied current work is captured before asynchronous reads. The persistence
interface accepts and returns immutable route facts; PostgreSQL seeds its
bounded batch directly from them. Saved fuel history reuses those facts without
reconstructing a Dispatch. Shared Domain TruckPath/StopOperation policies retain
manual-start, conflicting-assignment and personal-travel-gap rules. History has its
own content signature, separate from geometry reuse.
DeadheadHistoryPublication validates it under the protected publication
transaction. DispatchDeadheads participates in writer revisions because its
stored previous-leg references affect completed-history membership. Deadhead
reservation/financial writes and final road/financial writes use this
boundary; provider calls stay between transactions. These captures are
transient inputs, not immutable accounting records.

ETA predecessor reads retain completed-work and unknown-start guards and may
overlap remaining-work capture. Calculation, HOS/provider work and result
publication happen after the read transaction. The full itinerary signature
participates in InputHash; geometry reuse is separate. ETA also includes its
historical input signatures in InputHash, retaining unavailable connections as
dependencies. PlanningWorkPublication checks canonical work first, then
replays historical selection inside the same protected transaction. Validation
retains each original lookup batch because completed native references are
shared within that batch. Supplied current facts are reused only after their
owning work has been validated. Before writing forecasts, ETA validates the
captured routing dimensions against an uncached effective profile in that same
transaction. It reuses the existing route-input signature; fuel-only preference
changes do not reject road ETA. Display caches retain their bounded lifetime.
ETA and fuel retain the saved-road versions actually used by calculation and
validate them inside publication. ISavedRoutePlanReader belongs to Routing;
its Infrastructure adapter projects compact metadata without geometry. Fuel
also captures future base/fallback roads and horizon/onward connections,
including absent sources that affected selection. Repeated observations remain
dependencies. The cached root carries its own immutable version; newer metadata
cannot certify an older cached road. Tracking signatures sort visited-stop
dictionary keys so JSON object order does not change road identity.
Telemetry and external prices retain separate policies. See [core
rebuild](architecture/core-rebuild.md) for remaining migration gates.

Dispatch first reads page data without HOS, financial hydration or ETA
validation. Independent `board/enrichment` reads reuse the authoritative board
handler and return only financial/forecast values with ordered load/stop
revision guards. The Client merges into retained components only while query,
row, assignment, stop revisions and forecast-driver identity still match.
Financial formulas remain server-owned. Supplemental reads still hydrate their
page server-side; this is not a database change feed or a shared transactional
snapshot across HTTP requests. ETA input validation batches root metadata and
canonical truck snapshots. Infrastructure projects compact saved-route
metadata. Shared selection preserves number-only assignment resolution; the
fleet identity map remains a lightweight read. Freshness and saved-route
ownership checks are not relaxed.

Fleet and Dispatch read `GET /api/fleet/hos` independently of geometry. It signals
the existing shared HOS snapshot and returns clocks paired with the current driver
assignment, without a Samsara call per viewer. Initial empty snapshots have at most
five prompt attempts; normal Client polling is fifteen seconds and pauses while
hidden. Provider freshness and forty-five-second refresh cadence are unchanged.
The snapshot is per API instance, not distributed across instances.

Display-cache HTTP reads are coalesced; cancellation stops transport only after
the last consumer leaves. Version-matched geometry stays shared and bounded.
Session reset and cache disposal cancel every owned planning transport, including
superseded refreshes, direct previews and optional fleet preloads. Generation and
request-owner guards still reject late responses from transports ignoring cancel.
Client JSON reads stream without the HttpClient full-body buffering step. API
JSON compression is restricted to explicitly opted-in operational endpoints after
authentication; authentication and credential responses are excluded.

ReadCache retains at most 4,096 generation identities. Its monotonic eviction epoch
prevents an evicted identity from reviving an older cached result. Cache byte limits
are accounting bounds, not process working-set guarantees.

The fuel map price overview projects the existing cached, selected-day station
read into IDs and chosen cash/IFTA prices only. It does not query tomorrow, fetch
providers or recalculate fuel. The Client uses these prices for the same global
per-currency color scale when detailed station markers are hidden.

TruckFuelPlans owns the rolling fuel snapshot lifecycle through ITruckFuelPlanStore.
Infrastructure stores the compact itinerary/purchases separately from the checked
and baseline geometry and guards replacement against older concurrent results.
FuelPlanningService commits the requested profile, route compatibility copy and
truck snapshot in one transaction. Failed calculation or publication preserves
all three. FuelPlanProjection validates the ordered remaining assignments,
prices, profile and measured fuel without a routing request. The summary cache and
bounded current-leg cache do not retain every truck's full fuel geometry. Explicit
searches are serialized per truck and limited to two per process; read-only map
requests do not take those gates.

FuelRoadDependencies persists the exact consumed road versions after the strict
publication comparison. SavedRoadVersion separates road identity from progress:
publication checks both, while durable reuse ignores tracking-only changes.
ISavedRoadValidation reuses the compact Infrastructure readers; read-time checks
share IExecutionReadScope and do not request geometry or providers. TruckFuelPlans
and the automatic fuel refresh cycle check remaining road dependencies without
trusting the summary cache to certify them. Missing legacy evidence or a
mismatch marks display stale; automatic plans retry through the existing
revision guard while manual plans remain preserved. Completed earlier blocks
are excluded, and onward connections remain included. This is operational
validity evidence, not an immutable financial ledger.

FuelHistoryDependencies also retains the original historical lookup seeds and
their accepted selection signatures. IFuelSavedInputsValidation checks saved
roads and replays relevant historical batches in one read snapshot, including
when the truck summary is cached. Original batch membership is preserved, but
completed earlier blocks and an already traversed incoming connection no longer
invalidate remaining fuel. Missing legacy history requires refresh without
discarding saved choices. No predecessor geometry is persisted or fetched.

After fuel commit, route-cache invalidation includes the execution-leg identity.
A cached pre-commit route cannot survive publication by using the legacy
dispatch-only key for a native route.

IFuelWorkInputsReader supplies fresh itinerary captures for search/edit/save
and bounded cached inputs for display and automatic-refresh eligibility.
PlanningReadService shares its captured itinerary with TruckFuelPlans. Fuel
scope includes overdue assignments independently of screen filters, preserving
existing native transfer boundaries. FuelHorizon and FuelRegionPlanner project
captured facts without loading assignments again. Persisted fuel signatures
retain their existing pure DTO compatibility format.

Fuel reads a fresh complete itinerary before committing and rejects a changed
content signature. Price reads and schedule evaluation run after the capture
transaction ends. Saved predecessor roads, settings and profiles retain their
separate validation/cache policies. PlanningWorkPublication compares canonical
work again inside the transaction that replaces both fuel copies. Expected
result revisions still guard concurrent result replacement. Before changing
the profile, fuel compares its initially observed effective profile against
uncached stored profile, fleet preferences and exchange-rate reads. The route
copy also validates its effective profile after the write and its saved truck
identity for legacy and native work. These reads do not refresh providers or
populate display caches. Truck/fleet settings stay protected until commit;
the automatic exchange-rate writer joins the same scope. Saved-road versions,
telemetry and prices retain separate policies.

A live route build captures its observed effective profile before provider work
and revalidates it inside publication before writing its requested profile.
Automatic builds also reject a stale supplied profile before provider work.
Manual builds may intentionally change dimensions, provided no intervening
stored-profile or effective fleet-setting change occurred. Automatic route
progress and reroutes validate routing inputs inside publication without
rejecting fuel-only changes. Explicit profile edits retain their existing
last-accepted-write contract; no client edit revision is introduced.

FuelHorizon returns historical dependencies with saved connecting roads;
FuelRegionPlanner returns the onward connection dependency with its arrival
policy. Automatic search, manual save and reset carry both into the shared
publication guard before any profile or fuel write. Connections derived solely
from captured native work continue to use the canonical-work guard. These
historical batches protect publication; their compact lookup seeds and selection
signatures also persist with fuel-road dependencies in the existing truck summary
JSON, independently of geometry. The summary retains its existing byte limit.

FuelPlanningService, FuelHorizon and FuelRegionPlanner use saved-route reads, not
routing/geocoding requests or route-repair operations. New snapshots declare
estimated station access, retain only unchanged baseline geometry, and keep each
station's baseline occurrence separate from estimated access mileage.
FuelPlanProjection and FuelPlanMemory select the declared geometry basis; legacy
checked snapshots remain separately readable. FuelAccessEstimate owns the nearby
allowance and transient schedule timing projection. It must not replace saved
road mileage or imply a checked truck approach. See
[fuel planning rules](features/fuel-planning-rules.md).

FuelPlanMemory keeps only one leg/version per truck. RouteDisplayCache keeps only
one revision per dispatch. Each singleton permits two concurrent cold geometry
loads; hot reads bypass these limits. Their loaders must remain direct persistence
reads, never recursively enter either cache. Request-scoped FuelSearchGeometry
uses bounded coarse blocks with exact local refinement over the retained baseline;
it does not replace provider mileage or the detailed GPS route.

TomTom releases only its own tracked audit reservation after each provider call.
Response bodies are streamed with byte/point bounds and a body-inclusive timeout.
Its private versioned cache stores leg geometry once while reading compatible
legacy rows. Base/deadhead storage also omits duplicate aggregate coordinates.
Base-route reads do not attach saved geometry; owned writes detach after saving,
while caller-owned tracked work is preserved. Fuel-horizon joins share leg data
without repeatedly copying a flat path and reject totals above 200,000 leg points
before candidate search.

RoutePreviewService provides fleet previews and the per-truck saved preview at
`GET /api/fleet/trucks/{truckId}/planning/preview`. Both share captured
assignment facts, saved ownership/input checks against the current effective
truck profile, and completed-stop selection. Missing or stale geometry does
not skip the first remaining dispatch. Preview progress reuses the existing
in-memory telemetry snapshot and exact saved geometry with the normal server
progress matcher, including freshness and completed-stop bounds. It
accompanies geometry in the initial response; it does not advance tracking.
Missing or stale GPS keeps progress unavailable. These reads do not fetch HOS,
telemetry, ETA or providers, enqueue work, advance tracking, or write
persistence. Preview fuel recommendations are omitted until the normal
planning read validates them.

TruckPlanningInputsReader captures the complete itinerary and selected driver
identity in one IExecutionReadScope. Preview and live planning share these
inputs, immutable RouteWorkProjection and explicit PlanningWorkPolicy. An
unresolved candidate blocks selection; filtering screen loads cannot select a
later current load. Captured facts use the existing bounded ReadCache
lifetime, invalidated by UTC date and board/dispatch/execution generations.
Warm reads reuse those facts without querying assignments again. Live planning
attaches HOS after this capture finishes, using its driver identity; previews
omit HOS. This display cache is not a fresh calculation-publication check.

The per-truck path reuses work, profile and display caches without waiting on
a fleet-preview gate. Fleet previews have one clone-safe serialized cache
entry of at most eight MiB, a 30-second TTL and a dedicated gate; they must
not hold a shared ReadCache stripe while loading other cached reads. UTC date
and board, dispatch, execution, settings and preview generations invalidate
this entry. Committed route/profile writes invalidate previews, including the
existing post-transaction invalidation. These caches are per-instance, not a
distributed invalidation guarantee.

RouteChoiceService uses the captured work queue and the existing stop tracker
to preview a current truck's remaining route from fresh GPS. The durable draft
binds that origin, full baseline, current plan revision and remaining stop IDs.
Saving preserves the full baseline and commits the separate remaining choice and
live RoutePlan in one transaction through RoutePlanStore. BaseRouteService reconnects
to that remaining choice; the historical full route must never be mistaken for a
new GPS-start alternative. Future-load choices retain stop-to-stop semantics.

The shared dispatch board keeps unfinished in-transit loads and loads with recorded
pickup activity ahead of unstarted assignments regardless of scheduled delivery
date. UTC midnight or a missed appointment cannot change the current dispatch.
Actual final delivery/departure and terminal statuses still remove completed work.
The date filter applies only to unstarted loads; `IncludeOverdue` can additionally
include those older assignments. Preview, live planning and ETA chain selection
consume this same ordering rather than selecting independently.

Dispatch import matches stop identities by operation and original source location,
not sequence alone. Distinct appointments disambiguate repeated visits; ambiguous
replacements receive new IDs rather than inheriting manual completion or truck-start
state. Verified addresses do not affect source matching. A removed manual anchor
still requires confirmation. Automatic planning can connect fresh GPS to a single
effective stop only for the authoritative current assignment; future preparation
retains stop-to-stop semantics and normal GPS freshness/recalculation guards apply.

Infrastructure groups external adapters under Integrations by provider, including
Google/Gmail, Google/Places, Ifta, Samsara, TomTom, and Torque. Shared HTTP mechanics
live in Integrations/Http. Persistence queries with special scaling requirements
remain behind Application interfaces: IDeadheadHistoryReader retrieves current
loads, at most two predecessor candidates per load, and an undated-history flag.
PostgreSQL selects predecessor IDs and the undated-history flag in one
parameterized correlated top-two batch (LATERAL), followed by one predecessor
projection read. Callers can supply their already-loaded current dispatches. SQLite has
a bounded per-load fallback because it does not support LATERAL. SQL translation
and SQLite semantics are tested; production query plans and latency remain unmeasured.
Application retains the assignment, chronology, and ambiguity checks.
DeadheadHistoryService applies native execution ownership to those bounded
history reads, including explicitly referenced completed predecessors. Production,
fuel, next-load geometry and ETA consumers share this interpretation. Provider
SQL remains in IDeadheadHistoryReader's Infrastructure implementation; source
truck fields cannot replace a native predecessor's saved assignment.

Fuel owns Gmail watch registration, renewal, retry timing, and periodic notification
recovery. IGmailWatchStore persists typed lifecycle state in a dedicated existing
SynchronizationCheckpoints row. Its Infrastructure adapter shares the lease storage
primitive with fleet synchronization, but not its row, owner, or schedule. Background
Gmail work requires an explicit Admin registration and uses non-interactive credentials.
See [Gmail watch operations](operations/gmail-watch.md) for activation and recovery boundaries.

Fuel also owns the dated CAD-to-USD rate fallback. Its Application service reads
the stored observation without provider requests and refreshes it through
`IFuelExchangeRateProvider` and `IFuelExchangeRateStore`. Infrastructure supplies
`BankOfCanadaExchangeRateProvider` and a dedicated checkpoint lease row. The
fleet scheduler runs the refresh independently from truck fuel jobs. Explicit
fleet preferences take precedence; automatic rates never rewrite them. Rate saves
own an IPlanningPublicationScope transaction and commit before cache
invalidation. Provider requests and lease acquisition/release stay outside it.
A failed publication preserves the previous observation; it does not expose
an uncommitted candidate through the read cache. No schema or additional hosted
worker is required. The fleet scheduler treats planning exceptions with an
explicit retry time as deferrals, preserving prior success/failure state and
avoiding failure logs for expected publication contention.

Fuel station lookup attempts also use dedicated deterministic checkpoint rows,
through IFuelStationLookupStore. Application owns query identity, retry policy,
and location replacement. Infrastructure commits attempt/lease state in independent
scopes. Lookup preparation finishes before the message transaction or import lock
is acquired; only matching prepared results are applied under that transaction.
An active attempt or failure cooldown defers the email without marking it imported.
Unchanged verified locations remain reusable; changed identifying queries cannot
reuse an old point. A durable per-attempt revision is rechecked through the existing
import connection: the latest prepared lookup wins, and stale location writes are
skipped while historical prices can still import. This does not infer which email
is newest. No provider-specific persistence or optional fallback service
is constructed in Application.

Documentation and comments are English-only. Comments explain non-obvious
invariants, not routine statements. See [operational diagnostics](operations/diagnostics.md) for logging rules.

- API controllers use MediatR requests and the existing `BaseController.HandleRequest` response contract. They do not call planning services, EF, or background worker implementations directly. The legacy synchronization status endpoint keeps its existing unwrapped JSON response, but obtains that response through its query handler.
- Application owns Commands, Queries, validators, models, interfaces, and business logic. Routing orchestration services are under `Features/Routing/Services`; geometry and optimization are under `Algorithms`. They are shared by request handlers without duplicating calculations. FluentValidation runs through the existing pipeline. Expected planning errors and settings conflicts are translated into `RequestResponse` by `PlanningExceptionBehavior`.
- Infrastructure implements provider interfaces, persistence, and hosted workers. Hosted workers access application operations through Application interfaces; command dispatch, when needed, remains inside Application. Worker status is exposed through an Application interface. Application services register in `AddApplication`; concrete external implementations and hosted workers register in `AddInfrastructure`. Configuration binding stays in API's composition root.
- Domain remains persistence/business entities. HTTP request and response models are not Domain entities.
- `ICurrentUser` is an Application interface implemented as a scoped Infrastructure adapter over the authenticated HTTP principal. It exposes identity only, never cached roles or profiles. Handlers query active profiles and enforce account self-protection. Controllers must not query EF or extract claims. Authentication middleware still validates credentials; endpoint policies still guard administrative access. `auth/me` and synchronization status preserve their unwrapped responses via `HandleUnwrappedRequest`.
- Users owns the authenticated `settings/appearance` read/write requests.
  Handlers derive the target from `ICurrentUser` and require an active profile;
  clients cannot choose another user ID. `Users.Theme` stores only `light` or
  `dark`, defaulting to Light through the additive `AddUserTheme` migration.
  Appearance is not a company preference, role, token claim or routing input.
  The same authenticated contract includes optional temperature/distance units.
  Omitted write fields preserve saved units, including older theme-only clients.
  `AddUserDisplayUnits` adds per-user columns and seeds existing accounts from
  the former shared settings; new accounts default to Both. Shared unit columns
  remain for compatibility but no longer drive the Client's display choices.
  The Client's root appearance provider restores once per account lifetime,
  applies the shared theme roles and rejects stale account reads/saves.
  Settings mounts company forms only for Admins; their API policies are unchanged.
- Client has its own DTOs under `Models/DTO/Planning`, matching the project's separate client/server model pattern. It does not reference Server or a Shared project. Server-only validation and planning operations stay on the server. Preserve existing JSON field names when adding contract fields; new durable fuel snapshots use their own additive schema migration.
- Provider adapters map route sections into Application contracts; `IRouteSectionValidator` preserves provider mileage and geometry and adds access warnings without blocking financial calculations. Map and Dispatch planning views display these warnings; mileage acceptance is not permission to drive a restricted road. Invalid or incomplete route responses still fail validation. ETA responses expose `RouteUpdatePending`; UI must not infer calculation state from message text. Previous estimates follow the bounded display-only grace below.
- Razor contains markup. Component state, injection, handlers, and helper methods are in the matching partial `.razor.cs` files. SCSS stays in the existing Styles hierarchy.
- Fleet Map's page owns selection and route coordination. `FleetMapSession<T>` owns
  JS startup and resource disposal; `FleetStationLayer` owns station requests and
  cancellation. See [Client ownership](architecture/fleet-map-client.md). These
  component-owned objects do not add DI services or duplicate planning policies.
- Next-load selection and base-route freshness checks belong to Application. The map receives ready geometry or an explicit pending status in one response. Existing base-route background preparation supplies missing routes; normal fleet refresh retrieves completed results without rebuilding unchanged map layers.

During recalculation, ArrivalEstimate may display the previous estimate for the same stop for up to 15 minutes after its validity deadline. Keep the saved text, values and status colors unchanged without Previous/Updating labels until a complete replacement arrives. This display-only grace does not extend server cache validity or certify that the retained estimate was recalculated. Identity, completion and expiry checks still clear ineligible snapshots.

ETA owns a bounded, immutable route-timing projection cache in `EtaMemory`.
Compilation samples regional rules once for each geometry version; replay uses
saved leg durations and merged country segments without retaining coordinate arrays.
HOS clocks, appointment waiting, fuel and progress remain calculation inputs, not
part of the static timing projection. Coalesced new-view demand wakes the ETA worker;
the periodic sweep and provider freshness/retry policies remain in effect.

FuelScheduleEvaluator reuses the road-only ETA calculation against one captured
driver-clock/history snapshot. Preview timing uses bounded uniform road-mile
sampling and is not inserted into the live timing cache; candidates do not launch
HOS requests individually. Saved schedule impact is explicitly dated historical
information, not continuously revalidated driver feasibility.

Cycle feasibility is a separate pure ledger under Eta/Algorithms. The road-only
daily-HOS replay emits chronological work/rest events. Current ELD Cycle anchors
signed balances; reconciled home-day history supplies bounded recap credits.
Unreconciled totals do not discard a usable ELD anchor or grant historical credits.
The ledger preserves earlier driving shortages.
Recap-wait and restart are separately replayed conditional alternatives, never
client-side business calculations or automatic driver-intent assumptions. The
same prepared route chain and existing forecast JSON carry all scenarios.
Facility service and appointment waits are planned sleeper time for current and
future loads. The clock charges driving plus once-per-new-shift PTI/fuel allowances;
it does not infer actual ELD changes or charge each saved fuel recommendation again.

`EtaChainInputsService` validates the authoritative truck/load sequence and saved
base/deadhead revisions. `EtaService` carries one clock across that chain;
`EtaForecastService` publishes exact dispatch/stop identities and owns snapshot
freshness. `IEtaForecastStore` is the Application persistence boundary;
Infrastructure implements transactional, newer-only writes to `DispatchEtaForecasts`.
Reads reuse saved forecasts but reject changed assignments, predecessors and
schedules. ETA retains immutable metadata for each root considered during
selection, including skipped completed roots, and future saved-road versions.
Cold timing compilation requires the loaded geometry to match that capture.
Final metadata reads inside publication validate it again without transferring
geometry. The actual current plan must also match the selected root metadata;
a stale cached plan cannot publish against a newer description. Fuel-only root
writes leave the road token unchanged. Version metadata includes geometry
presence so clearing a connection does not retain its old timing identity.
A database snapshot is not an actual stop event or proof of HOS compliance.
Internal board reads disable ETA enrichment to avoid recursive orchestration.

Next-load revision checks use saved-route metadata and input signatures before reading geometry. `INextLoadRouteReader` owns the joined persistence projection;
unchanged and label-only reads do not retrieve route JSON, and cold reads skip
the redundant metadata query. The returned revision describes the actual loaded
geometry even if a route changes between reads. Pending routes reserve their stop
count and can enqueue deduplicated background demand, but never call a routing
provider synchronously. Renumbering updates marker text without rebuilding paths;
route appearance uses current, future and deadhead roles rather than color literals
as identifiers.

`RouteDisplayCache` keeps clone-safe serialized display and metadata-only
projections alongside exact geometry for GPS matching. Matching plan/version reads
deserialize metadata without coordinate arrays; full background reads preserve
exact geometry. Both projections and retained geometry count toward the cache
bound. Each keyed-gate owner uses a separate fixed stripe set: do not hold one
owner's stripe and recursively acquire another key from that same owner.

On-demand `PlanningRefreshOperation` has a bounded configurable consumer count
(`Synchronization:PlanningConcurrency`, default two, allowed one through four).
The existing per-truck automatic-planning gate still serializes related mutations;
provider concurrency/reservation limits and queue deduplication/cooldowns remain
unchanged. Background job duration remains a diagnostic stage.

`BaseRouteOperation` owns source-road preparation through ISourceRoadStore.
Map and Next Loads reads persist missing-geometry demand before returning.
Rotating bounded scans capture source/native work, effective routing profiles and
historical predecessors in a shared read snapshot. Their durable signatures do
not depend on process cache generations. SourceRoadRequests separates observed
inputs from explicit geometry demand, coalesces retries and retains cooldowns.
PostgreSQL claims skip locked rows; only the current unexpired lease can complete
its captured version. Shutdown leaves unfinished work for lease recovery.

`RoutePreparationQueue` retains bounded local mutation hints. The worker transfers
one hint at a time into persisted demand; periodic scans repair missed hints.
Hints can reopen completed repair without resetting a pending provider deadline.
Explicit missing-route demand may exceed the speculative preparation horizon.
Verification provenance travels with stop coordinates; raw imported street
points are not equivalent to verified addresses. Saved base/deadhead signatures
and endpoint anchoring both gate geometry reuse. Completed demand is pruned
only after its retention interval. The preparation queue is not calculation
input and does not narrow the publication lock boundary.

API's `OptionsRegistration.AddApplicationOptions` groups binding and startup
validation. Options and policies remain in Application; external adapter DI remains
in Infrastructure. This grouping does not move ownership between layers.

Fleet Map keeps a component-owned Next Loads display cache keyed by truck and
current dispatch: at most 12 complete serialized snapshots, eight MiB in total,
with five-minute expiry. A returning selection restores its saved geometry and
latest stop metadata before waiting for planning HTTP, then revalidates the revision.
Read access does not extend expiry; successful validation can. Metadata-only
updates stay metadata-only on the live JS bridge but merge into the complete
replay snapshot. Selection changes do not clear other trucks' snapshots; disposal
does. An unknown current dispatch must first be resolved by the server, never
guessed from a client-side load order. On a cold automatic truck selection,
the Client uses the per-truck saved preview described above. It bounds this
optional first read to two seconds, then falls back to
normal planning. A successful preview starts Next Loads before the live planning
read completes; the subsequent request supplies the saved geometry revision.
Late, cancelled or same-key superseded previews cannot replace newer data;
optional fleet preloading must also yield to explicit reads and newer no-plan
responses. Next Loads reads saved routes and does not invoke route providers.
Missing or invalidated geometry still depends on background preparation.

Future-stop clicks pin a separate inspected load in Fleet Map while the truck's
current route remains authoritative. All selected-load roads and markers
stay highlighted until dismissal; future-stop hover does not select anything.
Dispatch detail reads are bounded and cancellation-safe. Current-stop click cards
and selected future-stop cards receive server ETA metadata by exact stop/dispatch
identity, and expire locally at the server validity deadline. Current and future
stops use numbered markers without floating text labels. Stop cards reuse UI theme and body typography tokens;
ETA-only updates do not rebuild road geometry.

Dispatch owns optional [load number display settings](features/load-numbering.md)
through a separate entity and MediatR read/write handlers. Authenticated users can
read the prefix; only Admins may change it. The existing response and write
contract retain legacy unit fields for compatibility, but current company forms
edit only the prefix. Personal units belong to Users, not Dispatch settings.
Display settings do not participate in
fuel or route signatures. The authenticated Client layout cascades the small
prefix snapshot. The root account provider cascades personal units independently;
map text updates do not rebuild or recalculate routes.

Integrations owns Admin credential management for TorqueAI, Samsara and Google
email. Application selects complete saved bundles or untouched deployment values
through `IIntegrationCredentials`; storage and deployment configuration are behind
Application interfaces. Infrastructure protects saved bundles with the durable
Data Protection key ring and provider-specific purposes, with one context per
operation and optimistic revision checks. Reads expose presence metadata only;
blank edits preserve values and explicit restoration retains a revision tombstone.
Provider requests capture their own credentials without shared header mutation.
See [integration settings](features/integration-settings.md) for OAuth tuple rules,
the existing key-ring protection limitation and rollback behavior.

Boundary refactors preserve existing URLs and financial formulas. Behavior fixes,
the additive role-uniqueness migration and their verification status are tracked in
`archive/2026-09/stabilization-work.md` and `operations/security-rollout.md`. Checks cover the MediatR pipeline,
validation, HTTP conflict/error responses, saved routes, fuel optimization,
synchronization and authentication. Changes to planning payloads require updating
both server models and client DTOs.

### Immutable calculation and read boundaries

Domain Dispatch.ForExecution, ExecutionRouteProjection and the screen-model
ExecutionWorkProjection have been removed. Native reads capture
ExecutionLoadSnapshot with immutable RouteWorkSnapshot calculation facts and
separate immutable commercial/display details. The Dispatch API projects those
facts only at its response boundary. Source entities remain persistence/edit
inputs; calculations capture them before asynchronous work.

ETA descriptions, live route preparation, route choices, progress, fuel horizons,
base roads and historical connections use immutable work. Shared Domain path,
operation and completion policies serve source editing and captured facts.
Synchronous ordering and fuel dependency policies accept the data-only IWorkFacts
contract; they do not depend on Dispatch screen DTOs. Historical fuel replay
retains its input capture without reconstructing mutable Dispatch entities.
Financial persistence takes its explicit minimal DispatchRateInputs contract.

Accepted operation and cargo state reach the display unchanged. In particular,
receiving a loaded trailer must retain Hook/Loaded; source-operation inference
must not replace its accepted state with Unknown. Native saved fuel signatures
based on the former incorrect projection become stale and require recalculation.
The pending clean reset discards those operational results. This does not change
fuel optimization or ETA timing policy.

### Shared planning eligibility

`PlanningWorkPolicy` owns GPS eligibility, blocking source-read problems and
saved-route completion proofs for route display, automatic planning, route
choices, ETA and fuel. Accepted ordinary planned work can be calculated without
activating its execution. An unconfirmed incoming handoff cannot use truck GPS,
including when its status is inconsistent with the receipt facts.

A completed-route proof includes truck, load, execution leg, assignment revision
and current input validity. ETA uses the same proof on its metadata-only batch;
route consumers use hydrated plans. Neither path changes execution status or
resource custody. New consumers must reuse this policy instead of testing
`active` or `AllStopsPassed` alone to decide current-work eligibility.

Fuel reset failures display the API's actionable reason and retain the editor's
existing draft. They do not claim a saved plan exists when the first calculation
failed. Existing durable background queues own transient retries; manual reset
is not replayed automatically because its expected revision and user edits must
remain authoritative.

### Native deadhead continuity

Deadhead preparation and next-load display support accepted execution scopes as
well as legacy loads. Connections retain both load and execution-leg identities,
assignment revisions and verified endpoint geometry. The shared predecessor
reader captures each current execution section, and source-road demand includes
its predecessor signature so assignment changes reopen preparation.

The native background preparation branch builds both the load road and its
eligible incoming empty road. Metadata-only map polling validates the same
connection signature before reusing geometry. Publication reloads the current
accepted section and its predecessors inside the owned transaction; a late route
cannot publish after either assignment changes. Ambiguous order and transfer
boundaries do not imply an empty connection. Native connection mileage belongs
to its execution scope and does not overwrite the legacy load-wide rate row.

Regression coverage follows native deadheads through background preparation,
persistence, map payload, unchanged polling, predecessor changes and concurrent
publication rejection. Legacy-only fixtures cannot establish native parity.
