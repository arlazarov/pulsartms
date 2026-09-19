# Project architecture

The layer boundaries in `AGENTS.md` are mandatory. Existing modules may contain
violations and must not be treated as permission to repeat them.

## Dependency boundaries

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

ReadCache retains at most 4,096 generation identities. Its monotonic eviction epoch
prevents an evicted identity from reviving an older cached result. Cache byte limits
are accounting bounds, not process working-set guarantees. Cached values are JSON
copies unless a reader opts into `GetSharedAsync` with a caller-safe copy for
immutable results; fuel station lists share their records and copy only the list.

TruckFuelPlans owns the rolling fuel snapshot lifecycle through ITruckFuelPlanStore.
Infrastructure stores the compact itinerary/purchases separately from the checked
and baseline geometry and guards replacement against older concurrent results.
FuelPlanningService commits the route compatibility copy and truck snapshot in
one transaction. FuelPlanProjection validates the ordered remaining assignments,
prices, profile and measured fuel without a routing request. The summary cache and
bounded current-leg cache do not retain every truck's full fuel geometry. Explicit
searches are serialized per truck and limited to two per process; read-only map
requests do not take those gates.

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
`GET /api/fleet/trucks/{truckId}/planning/preview`. Both share server assignment
resolution, saved ownership/input checks against the current effective truck
profile, and completed-stop selection. Missing or stale geometry does not skip
the first remaining dispatch. These reads do not fetch HOS, telemetry, ETA or
providers, enqueue work, advance tracking, or write persistence. Preview fuel
recommendations are omitted until the normal planning read validates them.
The per-truck path reuses board, dispatch, profile and display caches without
waiting on a fleet-preview gate. Fleet previews have one clone-safe serialized
cache entry of at most eight MiB, a 30-second TTL and a dedicated gate; they must
not hold a shared ReadCache stripe while loading other cached reads. UTC date
and board, dispatch, settings and preview generations invalidate this entry.
Committed route/profile writes invalidate previews, including the existing
post-transaction invalidation. These caches are per-instance, not a distributed
invalidation guarantee.

The shared dispatch board keeps unfinished in-transit loads and loads with recorded
pickup activity ahead of unstarted assignments regardless of scheduled delivery
date. UTC midnight or a missed appointment cannot change the current dispatch.
Actual final delivery/departure and terminal statuses still remove completed work.
The date filter applies only to unstarted loads; `IncludeOverdue` can additionally
include those older assignments. Preview, live planning and ETA chain selection
consume this same ordering rather than selecting independently.

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

`HostingOptions.Role` (`All`, `Workers`, `Api`) decides which workers a process hosts;
an `Api` process follows the synchronization checkpoint instead of owning it, so the
request tier can scale out while one `Workers` process owns provider polling.
`HostingRoleRegistrationTests` checks the gating.

Per-key concurrency stripes come from the `ProcessGates` singleton (`For<TOwner>()`),
not static fields, so tests and any future multi-instance hosting own their gates
explicitly; `Single<TOwner>()` and `Slots<TOwner>(n)` serve whole-operation and
budgeted gates, and `SynchronizationGates` is an injected singleton over them.
`ProcessStateTests` allows only the per-process request counter to remain static, and `FeatureDependencyTests` freezes the current
cross-feature reference map (Routing, Eta, Dispatch, Fleet and Synchronization
still form cycles) so it can only lose edges.

Fuel discounts enter through `IFuelDiscountProvider`; `FuelDiscounts:Source` picks the
Infrastructure implementation at startup (`bvd-gmail`, or `none` for customers without
a fuel card), so another card program is a new provider and source name, not a change
to import, station or planning code. Fuel owns Gmail watch registration, renewal, retry
timing, and periodic notification recovery for the BVD mailbox source. IGmailWatchStore persists typed lifecycle state in a dedicated existing
SynchronizationCheckpoints row. Its Infrastructure adapter shares the lease storage
primitive with fleet synchronization, but not its row, owner, or schedule. Background
Gmail work requires an explicit Admin registration and uses non-interactive credentials.
See [Gmail watch operations](operations/gmail-watch.md) for activation and recovery boundaries.

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

- API controllers use MediatR requests and the existing `BaseController.HandleRequest` response contract. They do not call planning services, EF, or background worker implementations directly. Response JSON uses the source-generated `ApiJsonContext` metadata with reflection as the fallback; `ApiJsonContextTests` requires every `RequestResponse<T>` shape to be listed and to serialize identically. `DependencyInjection.AddApiHttp` registers controllers, JSON and compression; `ApiContractTests` hosts exactly that registration in process to check routing, revalidation and compression without the rest of the composition root. The legacy synchronization status endpoint keeps its existing unwrapped JSON response, but obtains that response through its query handler.
- Application owns Commands, Queries, validators, models, interfaces, and business logic. Routing orchestration services are under `Features/Routing/Services`; geometry and optimization are under `Algorithms`. They are shared by request handlers without duplicating calculations. FluentValidation runs through the existing pipeline. Expected planning errors and settings conflicts are translated into `RequestResponse` by `PlanningExceptionBehavior`.
- Infrastructure implements provider interfaces, persistence, and hosted workers. Hosted workers access application operations through Application interfaces; command dispatch, when needed, remains inside Application. Worker status is exposed through an Application interface. Application services register in `AddApplication`; concrete external implementations and hosted workers register in `AddInfrastructure`. Configuration binding stays in API's composition root.
- Domain remains persistence/business entities. HTTP request and response models are not Domain entities.
- `ICurrentUser` is an Application interface implemented as a scoped Infrastructure adapter over the authenticated HTTP principal. It exposes identity only, never cached roles or profiles. Handlers query active profiles and enforce account self-protection. Controllers must not query EF or extract claims. Authentication middleware still validates credentials; endpoint policies still guard administrative access. `auth/me` and synchronization status preserve their unwrapped responses via `HandleUnwrappedRequest`.
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
base/deadhead revisions. Board enrichment reuses the last description per truck
while the `board`, `dispatch`, `profile:{truck}`, `route:{load}` and `chain:{load}`
generations are unchanged, for at most `EtaMemory.DescriptionLifetime`;
`BaseRouteService` and `DeadheadService` bump `chain:{load}` after each write.
Refresh workers always describe the chain fresh. `EtaService` carries one clock across that chain;
`EtaForecastService` publishes exact dispatch/stop identities and owns snapshot
freshness. `IEtaForecastStore` is the Application persistence boundary;
Infrastructure implements transactional, newer-only writes to `DispatchEtaForecasts`.
Reads reuse saved forecasts but reject changed assignments, predecessors and
schedules. A database snapshot is not an actual stop event or proof of HOS compliance.
Internal board reads disable ETA enrichment to avoid recursive orchestration.
`IDispatchBoardReader` (rows, details, HOS, financials) is the board read that
planning, fuel, preview, synchronization and ETA services call directly;
`DispatchBoardService` adds forecasts for `GetDispatchBoardHandler` and the
planning reads. Only the HTTP request passes through the MediatR pipeline, so
request metrics count browser reads rather than internal ones. Services do not
inject `ISender`; `MediatorUsageTests` lists the remaining debt, which may only shrink.

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

`RoutePreparationQueue` is a bounded, per-instance dirty-work scheduler, not
persistence. `BaseRouteOperation` reconstructs demand with rotating bounded scans,
prioritizes current/next assigned loads and respects the preparation horizon;
explicit missing-route demand may exceed that horizon. A successful input
fingerprint suppresses repeat preparation until invalidation or the repair interval.
Changed assignments also invalidate affected successor connections. Verification
provenance travels with stop coordinates; raw imported street points are not
equivalent to verified addresses. Saved base/deadhead signatures and endpoint
anchoring both gate geometry reuse. See [server optimization controls](archive/2026-09/server-optimization-2026-09-08.md).

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
read the prefix; only Admins may change it. Display settings do not participate in
fuel or route signatures. The authenticated Client layout cascades the small
snapshot, and map text updates do not rebuild or recalculate routes.

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
