# Dispatch execution, switches and settlements

Status: the operational scope below was deployed on 2026-09-14. This guide
distinguishes the target model from release evidence; it does not authorize data
changes. Payroll rules vary by contract;
there is no single owner-operator percentage or mileage formula.

Current design scope: Trucks, Trailers and Drivers configuration, execution
legs, independent transfers and mileage attribution. Pay is deferred: do not
implement compensation screens,
calculations, contract editors or payment flows in the operational release.
The settlement sections below record future compatibility requirements only.

This proposal extends the [SaaS evolution plan](saas-evolution-plan.md).
It follows the existing [layer boundaries](../ARCHITECTURE.md) and
[verification rules](../testing.md). TorqueAI remains an import source during
the transition. Native dispatch must use the same Application commands and
business entities as imported dispatch.

## Current implementation boundary

The resource settings and mileage API/UI are implemented in source. Native
Trip, ExecutionLeg, transfer participants, independent Release/Receive and
parked-trailer custody are persisted by Application commands. Native board,
route preparation, route choices, caches and queues preserve execution identity.
Current-leg ETA and fuel use that leg's assignment and driver; they stop before
an unconfirmed successor instead of inferring a commercial-load connection.

Switch now has a dispatcher section, read-only preview, explicit planning,
independent Drop/Hook confirmation and pre-actual cancellation. Requests retain
their retry identity and opened assignment revisions. Cancellation preserves
native history and restores the original outgoing itinerary; it does not
resurrect an ambiguous imported truck assignment.

Exact-source ordinary actuals fill missing native facts without completing
handoffs or replacing recorded timestamps. Future ordinary address/schedule
changes have a separate preview and acceptance action. Changed stop identities,
order, cargo operations and historical facts require explicit reconciliation;
they are not guessed or silently overwritten. The additive
[load workspace](../features/dispatch-workspace.md) implements future ordinary
visit editing and same-segment reordering in source. Corrections of completed
transfers remain separate future work. Workspace rollout is not implied by the
earlier operational deployment.

Transactional planning notifications survive process restart. Saved full native
routes also enqueue planned-mileage capture. An independent OBD odometer cursor
captures verified intervals inside confirmed resource/cargo boundaries, with
bounded pending samples and explicit gaps. This is partial observed coverage,
not a claim to reconstruct a complete trip from missing telemetry. Full future
cross-leg ETA/fuel continuity and native trip/resource creation screens are not
included. The model below is not a statement that every future workflow is live.

Migrations `20260914022232_AddExecutionAndMileage` and
`20260914031300_FinishSwitchAndAutomaticMileage` were applied on 2026-09-14.
See [rollout restrictions](../features/mileage-attribution.md#api-and-rollout).
The AMF1377/AMF1383 production assignments were not rewritten or activated by
this implementation. Ambiguous provider Pick Up rows are not converted to
confirmed Drop/Hook events automatically.

Deployment and database evidence are recorded in [the release record][release].
Earlier test results and remaining checks are recorded in
[the verification checkpoint][verification]. Earlier source-only evidence is in
[the implementation checkpoint](../archive/2026-09/execution-checkpoint.md).

[verification]: ../archive/2026-09/execution-verification-2026-09-13.md
[release]: ../archive/2026-09/execution-release-2026-09-14.md

## Dispatcher actions

Open a load's full Details page and expand Switch. This lazy read does not
change assignments, build routes or call a fleet provider. Plan one load's
handoff, or search the second load by number for a paired exchange. Choose the
exact existing source boundary visits and a location with confirmed coordinates.
Repeated Pick Up labels alone never establish a transfer.

Review outgoing and incoming trucks, trailer custody and driver/crew roles.
Preview shows the proposed split without saving. Plan records future work only.
Each participant's Drop and Hook is confirmed separately. Exact actual time is
optional; omit it when unknown. Confirmation identity, not timestamp presence,
controls release, receipt and open trailer custody. Known times must respect
recorded chronology. Unknown times cannot supply an odometer boundary or actual
mileage evidence. The receiving resources must be available at receipt.
Arrival, a source header
change and another participant's confirmation cannot perform these actions.

For imported work with no native legs, `This transfer already happened` records
all explicitly selected participants as completed in one transaction. It requires
exact consecutive source transfer visits, unchanged source fingerprints and
available, distinct incoming resources. It retains completed source history and
creates a completed outgoing leg plus an active incoming leg for each load.
Source visit timestamps remain unchanged; native event times remain unknown.
This is explicit reconciliation, not automatic inference from provider labels.

Cancel is available before actual work and dependent user changes. It restores
the outgoing native itinerary, preserves cancellation history and supersedes
automatic planned mileage. It cannot erase actual distance or custody events.
If an HTTP response is uncertain, Retry retains the same request key and payload.

Source Review is separate from a Switch. It previews future ordinary address or
appointment changes and requires explicit acceptance. Already recorded visits,
native transfer boundaries, stop identities/order and cargo/assignment changes
are not part of that action. A rejected change retains the native itinerary and
explains what needs reconciliation; it does not fall back to a different truck.

The Switch editor selects transfer locations from existing verified stops.
The load workspace can add ordinary future pickup/delivery visits within an
eligible segment. It cannot create a new transfer boundary or correct an already
completed handoff through an ordinary stop edit.

## Why the current assignment model is insufficient

Current live routes, base routes, route choices, deadheads and ETA snapshots
are keyed by DispatchId. Multiple trucks cannot safely save their respective
portions of the same load: one result would replace another. Selecting only
the currently active portion does not solve future planning or compensation.

A temporary active-leg projection was drafted during investigation, then
removed before activation. It is not the target storage model. Do not mark
imported pickups as switches automatically or rewrite historical assignments
to satisfy the existing single-truck validator.

## Core identities and responsibilities

### Resource configuration: Trucks, Trailers and Drivers

Provide three distinct configuration sections backed by the existing fleet
identities, not parallel catalogs with unrelated IDs. Separate resource
configuration from assigning resources to live work.

- Trucks: unit number, VIN, active/archive state and the existing per-truck
  planning profile. Reuse effective fleet defaults and profile validation.
  Show current and planned driver/trailer assignments as separate information.
- Trailers: unit number, VIN and active/archive state. Keep room for versioned
  equipment specifications without inventing unknown dimensions or capacities.
- Drivers: name and active/archive state, with appropriately restricted existing
  fuel-card configuration. Show current/planned assignments separately. Driver
  identity and its verified ELD mapping remain independent of truck identity.

Use searchable lists, explicit Edit/Save/Cancel and conflict-aware updates.
Reuse shared controls, responsive layouts and named style tokens. Admin owns
catalog/configuration edits; Dispatch owns authorized assignment and switch
operations. Do not expose compensation controls in these sections now.

Native creation and imported records share the same Application validation.
Retain source provenance and provider mappings separately from local identity.
Provider-controlled identifiers are not editable display fields. Define field
ownership before enabling edits: supported local overrides survive import and
have an explicit return-to-import action. Do not edit imported values only to
have the next synchronization silently replace them.

Do not create a replacement identity when an imported unit is renamed. Detect
duplicates within company ownership and reconcile source/native collisions.
Changing an ELD mapping requires an explicit audited operation; a driver rename
must not attach another person's HOS history.

Historical work references stable IDs and recorded assignment snapshots, not
today's mutable display fields. Archive instead of deleting referenced records.
Deactivation must resolve or reject active/planned assignments; it cannot
silently detach equipment or a driver from running work.

Current resource assignments are derived from confirmed execution events.
Editing a resource profile must not execute a switch, rewrite previous legs,
transfer fuel inventory or move HOS between people.

### Order, load and visits

The current Dispatch represents the commercial load: customer, cargo, revenue
lines and original pickup/delivery obligations. Its identity survives all
resource changes. A future Order can group commercial loads without owning the
physical truck mileage ledger. Do not require Trip.OrderId or duplicate trips
when a movement serves several orders.
Visits retain stable IDs, original provider facts and explicit local overrides.
Repeated visits at the same address remain separate visits.

### Trip and dispatcher workflow

A Trip groups operational work, including travel before pickup, after delivery,
home time, yard visits and maintenance. It may serve several loads or none.
Delivery completes a cargo obligation, not necessarily a trip. Midnight does not
end a trip. Stable truck, trailer and driver identities belong to its assignment
legs; a trip is not a second source of current resource ownership.

Dispatchers work with loads, destinations and explicit resource changes. They
do not manually build legs or allocate every routine movement. A confirmed
pickup approach is attributed to the load being collected. Existing validated
deadhead routes supply planned distance without another provider request.

Show Empty, Loaded and Total with an optional source/participant breakdown.
Keep planned and observed distances visibly separate. Bobtail is a subset of
empty distance in this summary, not another quantity to add to Total.
Accounting uses the same work records and allocation explanations. Attribution
to a load is neither customer billing nor approval of driver compensation.

Company rules select previous load, next load or unallocated for home, yard,
maintenance and other repositioning. A missing required target stays unallocated
with its reason; do not select another truck's load or erase the movement.
Manual exceptions preserve the actor, reason and previous allocation. Policy
edits do not silently rewrite historical allocations.

For delivery A -> home -> pickup B, preserve two travel intervals and the
intervening stationary period. Apply the configured home policy to the first
interval and attribute the confirmed pickup approach to B. Travelling home in
a personal car is not truck mileage. Destination purpose does not classify HOS.

### ExecutionLeg

An execution leg is a persisted physical-work interval with a stable ID,
start/end boundary, revision and resource assignment. It is not a routing
provider's geometry leg between consecutive points.

- Assignment records identify the truck, trailer and driver/crew roles.
- A confirmed change to any assigned resource starts a successor leg.
- Ordinary stops and road recalculation do not create new execution identities.
- Planned and actual times are separate. Future legs may have incomplete
  assignments without invalidating an already confirmed current leg.
- Empty movement and bobtail work can exist without a commercial load.
- Completed assignments and actuals are retained. Corrections are versioned,
  attributed and linked to the facts they supersede.

Movement sections inside a leg record changes in cargo/trailer state between
operational events. Pickup and delivery can change loaded/empty state without
changing the crew. Work purpose (home, pickup approach, yard, maintenance) is
independent of equipment state (loaded, empty trailer, bobtail, unknown).
Road geometry subdivisions must not create independent payable work records.

### LoadLeg

A LoadLeg links a load's carried portion to an ExecutionLeg and its boundary
visits. Initially one physical leg may carry one load. Keep the relationship
explicit so carrying multiple loads later does not duplicate physical mileage.
Load responsibility and truck movement must not become competing mile ledgers.

### Switch

Switch is a native coordinated operation, not a special spelling of Pick Up.
It links all affected outgoing and incoming legs, their load relationships,
resource assignments, boundary visits and independent actual transfer events.

Support changes to trucks, trailers, drivers, or a selected combination.
Support one-sided replacement as well as a two-party exchange. Explicitly name
the before/after resources; do not infer the selected assets from missing data.
Changing a crew's driver roles must be distinguishable from moving the load.

The UI previews before/after assignments for every participant. Planning the
switch does not update the active fleet assignment or mark any visit complete.
Cancellation removes only the future effect and retains the operation history.

Drop and Hook are independent actions, potentially on different days. Dropping
a loaded trailer closes the outgoing custody interval and opens custody at the
confirmed site. Only a confirmed later Hook closes that parked interval and
starts the receiving responsibility. Planned time and equal coordinates do not
prove either action. A crew-only change is not a trailer drop.

Native visits exist independently of provider visits. Optional source mappings
support import reconciliation without requiring two provider Pick Up records
or rewriting the provider's original operation labels.

## Lifecycle and invariants

Execution legs have planned, active, completed and cancelled states. A switch
has planned, in-progress, completed and cancelled states. Arrival/readiness may be
recorded independently, but is not completion of the exchange.

1. All operation participants belong to the same authorized company context.
   References and background work use the ownership rules of the SaaS plan.
2. A load's execution sequence has explicit continuity and no overlapping
   confirmed responsibility. Resource conflicts are checked across loads.
3. A truck cannot perform incompatible active work simultaneously. Crew roles
   and legal trailer combinations require explicit policies, not generic
   uniqueness constraints forbidding every multi-driver arrangement.
4. Each release or receipt validates its expected participant, resource and leg
   revisions. Its assignment/custody changes commit atomically. Different
   participants may finish at different times; one truck's release must not
   activate another truck or falsely complete the entire exchange.
5. Duplicate commands use an idempotency key. Reusing a key with a different
   payload conflicts; retries return the recorded operation result.
6. GPS proximity, pickup arrival, planned time or changed provider fields do
   not silently complete a native switch. Actual confirmation has provenance.
7. Missing participants or ambiguous imported visits require reconciliation.
   Quarantine that operation; retain unrelated confirmed work and its reads.
8. A completed switch is corrected through a compensating operation. Do not
   erase history or retroactively move approved earnings to another person.

Use durable transaction/concurrency protection, not only a process semaphore.
Commit a durable work notification with the business transaction. Planning
consumes revisions idempotently and rejects results computed from old inputs.

## Routing, ETA, HOS and fuel

Persist current and future routes independently by execution-leg identity and
input revision. Route selection, preview, cache, queue, geometry and forecast
keys must migrate together; adding a leg table alone does not fix overwrite.

The truck view selects its active physical leg. The load view assembles all its
LoadLegs, preserving each owner. A future participant cannot overwrite the
current participant's route, progress, fuel recommendations or ETA.

HOS belongs to the driver identity and clock snapshot, never the truck number.
A driver change binds a new clock source and forecast chain. Do not carry the
outgoing driver's remaining hours into the receiving driver's forecast.
Future estimates depend on assignment certainty and switch readiness, including
the other participant's arrival. Unknown dependencies remain unavailable, not
invented as zero travel or immediate transfer.

Fuel belongs to the physical truck/tank. Exchanging trailers or loads does not
transfer its fuel inventory or purchase history. Future fuel planning follows
that truck's leg chain, with explicit boundaries for incomplete assignments.
Forecast updates do not count as actual purchases, deliveries or payable work.

## Distance evidence and payable work

Keep four distinct values:

- Planned distance: a versioned routing estimate, free to change before work.
- Observed distance: attributed odometer/telemetry or approved manual evidence.
- Allocated distance: the explicit portion attributed to a commercial load.
- Payable distance: the approved quantity selected by the applicable contract.

Retain observations with physical leg, truck, driver/crew attribution, source,
time, units and quality. Preserve evidence references without embedding raw
provider payloads in logs. Normalize calculations to a canonical distance unit;
display miles/km only through Client formatting.

Odometer resets, missing samples, GPS gaps and contradictory observations are
reconciliation cases. Do not quietly replace actual mileage with planned
mileage. A contract may explicitly allow an approved fallback, with its basis
recorded in the payable-work snapshot.

Loaded, empty, bobtail and other payable work categories remain distinct.
Physical distance is recorded once, even with multiple loads or team drivers.
Any allocation across loads or payees is an explicit, auditable rule. GPS at a
point cannot prove which team driver drove the preceding interval.

## Contract-based compensation

Settlement ownership is a payee identity: an employee, contractor or business.
A payee may be associated with drivers or equipment, but is not identified by
a truck number. An owner operator may own multiple trucks and employ drivers.

CompensationContractVersion contains effective dates, currency, applicability,
payee, work categories, rate rules, allocation basis and approval requirements.
Define which business event selects the effective version. Later contract
edits must not silently change the version used for completed work.

Use a bounded set of typed rules, not arbitrary executable formula strings:

- Rate per approved distance unit, with category-specific rates if contracted.
- Percentage of explicitly selected revenue components.
- Fixed payment per load, leg, stop, day or approved work event.
- Approved extras and combinations with explicit precedence and stacking.
- Minimums, limits and rounding only where the agreement specifies them.

Keep revenue allocation separate from compensation percentage. If the agreement
uses a share of the load's revenue, record the eligible revenue lines, chosen
distance basis, numerator, denominator and allocation version. Do not assume
every percentage means a share of total billed revenue or total physical miles.
Line haul, fuel surcharge and accessorials may have different treatment.

Rules must define treatment of unpaid invoices, negative corrections, empty
distance, detours and missing evidence. Zero denominators block allocation.
Allocate rounding remainders deterministically so distributed amounts reconcile
to their selected revenue basis. Use decimal money and currency-aware rounding;
do not add unlike currencies or use the latest exchange rate retroactively.

This module prepares compensation and settlements. Statutory payroll, tax
withholding, employment classification and payment-provider execution are
separate future scopes requiring their own requirements and authorization.

## Immutable settlement evidence

Settlement lines reference the payee, work/leg IDs, contract version, approved
distance evidence, revenue/allocation version and calculation policy version.
Store inputs, quantities, rates, currency, rounding and resulting amount.
The server computes financial values; the Client only formats them.

Drafts may be recalculated explicitly. Approval freezes the calculation.
Corrections to approved or paid work create linked adjustment/reversal lines,
not silent edits. Prevent duplicate inclusion of the same payable-work/rule
identity in concurrent settlement runs.

Payment execution is separate from calculation and approval. A settlement
status cannot by itself prove funds were sent. Payment attempts, provider
references and reconciliation must eventually have their own idempotent records.

## Source reconciliation

Torque imports proposed facts through an adapter. Native commands own confirmed
switches and execution history. Import does not overwrite confirmed native
assignments, completion facts, approved distance or settlement lines.

Retain source mappings and source revisions. A conflicting provider change
creates a scoped reconciliation item. Matching by equal address alone is
forbidden. Removed or reordered confirmed visits cannot be silently replaced.

## Delivery sequence

1. Define operational identities, command contracts and acceptance fixtures.
   Add Trucks, Trailers and Drivers configuration with explicit import/local
   field ownership. Resolve migration compatibility before activation.
2. Add persisted execution legs, load links and coordinated switches. Backfill
   unambiguous single-assignment work; explicitly reconcile mixed assignments.
3. Move route/choice/preview, ETA, fuel, caches and workers to leg identities.
   Preserve old reads until their replacements are ready. Activate the incident
   exchanges only after this operational slice is complete.
4. Add operational distance evidence and automatic, explainable attribution.
   Approved payable work remains a separately authorized future feature.
5. Future, separately authorized: compensation contracts and settlement drafts.
6. Future, separately authorized: approval, adjustments, export and payments.

Each release needs additive migrations, rollback compatibility and bounded
backfills. Rollback must not reactivate an old single-assignment writer against
data that already contains active multi-leg work.

## Required acceptance scenarios

- Two loads exchange at one site without cross-writing routes or assignments.
- Truck-only, trailer-only, driver-only and combined changes retain actuals.
- Planning, cancellation and GPS arrival never execute the exchange.
- A release and later receipt retain the parked interval, including overnight.
- Duplicate actions and concurrent edits cannot partly commit one action.
- Completed history and current work survive repeated/reordered Torque imports.
- One ambiguous future operation does not fail the entire Dispatch board.
- Future and active route versions coexist; stale jobs cannot replace either.
- Driver HOS and truck fuel stay with their correct identities after a switch.
- Empty work, team drivers and shared movement do not duplicate physical miles.
- Pickup approach is attributed to its destination load without another routing
  request. Home/yard rules and manual exceptions preserve an allocation history.
- Policy changes and provider refreshes do not rewrite recorded actual mileage.
- Missing GPS/odometer evidence requires review rather than fabricated mileage.
- Contract changes reproduce old settlements from their original snapshots.
- Revenue allocation reconciles, including fractional rounding and reversals.
- Duplicate calculation/payment retries cannot produce duplicate compensation.
- Tenant boundaries and authorization apply to commands, reads and background
  work, including source mappings and compensation information.

Checks are planned, not run. Existing user instructions require permission
before executing tests. Database checks require an approved isolated fixture;
production is not a test fixture. No deployment is implied by this proposal.
