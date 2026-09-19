# Core rebuild specification

Status: the operational reset and compatible production cutover are applied.
Live acceptance found a native address-verification gap; its follow-up is being
verified. See the [production record][production], [final local record][readiness]
and
[cutover procedure](../operations/core-storage-cutover.md). This specification
owns current scope; dated [implementation records][record] explain earlier
slices without imposing obsolete compatibility requirements.

## Data retention and transition

The user permits replacement of all existing business data. Only users must
survive. This supersedes the old operational backfill strategy and removes the
need to preserve old loads, routes, execution, mileage, forecasts or audit rows.

Preserve the complete identity boundary: Users, AspNetUsers, AspNetUserClaims
(including application roles), AspNetUserLogins, AspNetUserTokens,
AspNetUserRoles, AspNetRoles, AspNetRoleClaims and DataProtectionKeys. Preserve
password hashes, identity links, security stamps, active flags and preferences.
Never export their values into diagnostics. Retain integration credentials;
resetting operational data does not require changing integration configuration.

New accepted work still needs durable history. Data from before the reset is
not a reason to retain the old ownership model. Do not infer actual timestamps,
resource responsibility or transfer confirmation during source reimport.

The applied `20260917055902_RebuildExecutionStorage` migration replaced the four
unapplied September 17 intermediate migrations. It creates typed accepted stops
and immutable leg revisions, and removes mutable stop JSON and the separate
transfer-visit table. It requires prior operational cleanup and refuses to
backfill populated execution. Downgrade refuses to discard new accepted work.
Previously deployed migrations and identity storage remain unchanged.
The same migration stores historical document author labels; execution revision
JSON captures its actor label independently of later account changes.

The same migration also persists on-demand refresh and source-road
requests with their lease/retry state. Its downgrade refuses pending requests
before deleting any
tables, alongside the accepted-execution guard. Keeping this clean transition
in one migration prevents an earlier step of a multi-migration downgrade from
committing before another step rejects it.

Prepare and verify the replacement before the working-database cleanup. The
schema requires a compatible API cutover with old readers/writers stopped.
Deployment follows the explicit permission rule in AGENTS.md. The user already
selected the working database; the approved transition is now applied.

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
- Ordinary cargo: Shipments. Border declarations, crossings and filing evidence:
  Customs. See the
  [customs data preparation](customs-preparation.md) extension. BorderConnect
  remains an optional Infrastructure adapter, not an execution owner.

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

## Implementation gates

### 1. Accepted visits, assignments and import ownership

Implemented locally:

- ExecutionLeg owns truck, crew, trailer and the accepted revision.
- ExecutionLegStop stores ordinary and transfer visits in one collection.
  Position is the sole accepted order. Consumer Sequence derives from Position
  plus one; imported source Sequence cannot reorder accepted execution.
- SwitchParticipant owns independent release and receipt confirmations and
  optional actual times. ExecutionTransferVisit is a detached projection from
  that participant and its accepted stop, not another persisted owner.
- Transfer source references live on accepted stops. Location, operation and
  sequence come from those stops. Missing boundaries block planning.
- ExecutionLegRevisions records final accepted facts, source provenance, load
  links and transfer facts in the owning mutation transaction. Commands retain
  actor and retry correlation; system synchronization has no invented actor.
  Repeated successful commands do not create duplicate accepted history.
- Recording time is not actual work time. Confirmed actions with unknown actual
  timestamps remain explicitly unknown. Persistence rejects history rewrites.
- ExecutionAcceptance owns new and existing accepted work from synchronization,
  source review, manual editing and the complete transfer lifecycle. Revision
  locking, stop replacement, obsolete planned-mileage decisions, immutable
  history and durable planning demand share the owning transaction. New legs
  begin at revision one. Recorded movement paths protect transfer splits as well
  as stop edits; unchanged adjacent paths remain usable. Cancellation and
  independent release/receipt retain their operation-specific checks.
- Source review identity includes header resources and unmatched resource names.
  A resource change while a review is open cannot reuse its earlier identity.
  This is a stale-review guard, not automatic assignment reconciliation.

- TorqueAI is now an optional adapter. Native load creation, internal numbering,
  provider-scoped source links and independent import scheduling no longer require
  it. Native create/assign/complete and PostgreSQL concurrent creation are verified.
  See [provider independence](../features/dispatch-import.md).
- Explicit ordinary assignment now creates accepted ExecutionLeg work through
  InitialExecutionAssignment. The correction editor and complete truck-start
  confirmation share transactional acceptance, immutable history and planning
  demand. Mixed resources, driver-only prefixes and invalid actuals remain under
  review. Unambiguous ordinary imports now use this owner with system provenance.
  Completion and operation commands use accepted visits when present. Transfer receipt remains mandatory
  before incoming work, and legacy actions cannot confirm a handoff.

Accepted source and review boundaries:

- Ordinary imports and native edits must use one accepted-work owner. Source
  obligations and proposals remain separate from accepted resource assignment.
  Existing-leg stop acceptance is shared; source-only loads still use imported
  DispatchStops until unambiguous acceptance. Automatic ordinary bootstrap and
  correction stop writes now use the shared owners. The Assignments comparison
  retains normalized source header/visit resources independently of local edits
  and shows them alongside accepted work. Review-only observations request
  planning revalidation without inventing accepted history or advancing its
  revision. Explicit transfer boundaries can now bootstrap planned source work
  without inventing active movement, release or receipt. Driver-only prefixes
  still require an explicit truck-start correction before truck transfer work.
- Ambiguous resources or missing transfer evidence remain reviewable problems;
  importing a changed truck cannot manufacture confirmed release or receipt.
  Source assignment signatures now block changed-resource actuals. Competing
  active candidates both remain under review, and accepted-source replay is
  idempotent across a fresh process.
- Idempotent reimport retains occurrence identity, including repeated addresses,
  additions and source corrections. Explicit local overrides retain ownership.
- Source-only review and explicit transfer bootstrap remain entry points to the
  common acceptance owner. They are not alternate accepted-work stores. Missing
  facts still require review; the reset must not manufacture them. Multi-load
  editing and native successor ETA remain outside the existing product policy.

### 2. Custody, movement and actual evidence

Use common accepted visit/leg identities for transfer and mileage references.
Keep parked trailer custody distinct from truck travel. Source actuals, explicit
confirmations and odometer observations retain provenance and uncertainty.
Recorded revisions support correction; they do not justify guessing an earlier
assignment interval or treating planned distance as observed distance.

Verify replay, resource changes, unknown times, duplicate observations, gaps,
corrections and movement allocation. Shared travel has one physical observation;
load attribution must not duplicate it. New accepted facts must survive later
resource edits in immutable history.

### 3. Calculation inputs and removal of adapters

The complete immutable itinerary is already shared by current Route/ETA/Fuel
selection. Publication validates accepted work, saved roads, effective settings
and historical connection dependencies in the result transaction. Saved fuel
plans replay their remaining historical dependencies without geometry or
provider requests. Same-identity recalculation retains the previous display.

Standalone base roads and historical connection inputs now share the immutable
RouteWorkSnapshot contract. Base preparation freezes caller inputs and revalidates
current work, routing dimensions and the observed saved-road version before
publication. Independent PostgreSQL writers cannot make an older calculation
replace changed work. Historical fuel lookup seeds retain their JSON shape.

Dispatch.ForExecution has been removed. Accepted standalone roads and planned
mileage validation now capture immutable RouteWorkSnapshot inputs directly.
RouteWorkProjection.ToLoad has also been removed. Base calculation, coordinate
policy, hashes and historical connection selection use immutable inputs end to
end. Historical persistence accepts/returns those facts directly, and saved
fuel-history replay does not reconstruct a source entity. Shared Domain path
and completion policies serve both source editing and immutable calculations.
DispatchRates accepts a separate, minimal immutable financial input contract.

ExecutionRouteProjection and ExecutionWorkProjection have now been removed.
Native reads capture immutable calculation facts and separate commercial/display
labels. ExecutionWorkReader builds immutable selection directly; Dispatch DTOs
exist only at their response boundary. ETA descriptions, live routes, route
choices, progress and fuel horizons retain the same captured facts through
asynchronous work. Synchronous dependency policies use Domain read-fact
interfaces without depending on screen models.

The accepted Hook/Loaded state is now retained by the screen projection. Its
previous source-operation inference incorrectly replaced Loaded with Unknown.
Saved native fuel signatures based on that incorrect state become stale; the
clean reset discards old operational plans. The fuel and ETA algorithms themselves
remain unchanged. Local verification and cutover evidence are recorded separately.

### Fuel consumer boundary

Fuel follows the physical truck and captures its complete remaining itinerary.
Driver HOS and cargo responsibility retain separate owners. Fuel eligibility,
route dependencies and historical connection validity use the same accepted
facts through calculation and publication. Future transfer readiness remains
explicit; unresolved work cannot be skipped to reach a convenient next load.

### 4. Publication ownership and durable work

PostgreSQL publication now owns a truck revision and shares a global guard.
Database triggers cover the complete writer inventory, advance old and new
membership and coordinate missing settings, number-only work, historical
connections and saved roads. Global catalog/settings changes and unresolved
source publication own the global guard exclusively. Independent unrelated
trucks can progress concurrently. Provider waits and calculation stay outside
transactions. Production contention and performance gains are not measured.

Cross-process delivery retains explicit retry and lease ownership. A worker's
completion cannot acknowledge a newer request. Splitting application hosts
follows measured need, not a prescribed service count.

The native execution outbox now uses atomic PostgreSQL claims that skip locked
rows. Completion and retry require the original unexpired lease. Isolated
PostgreSQL checks cover concurrent owners, abandoned-work recovery, retry
deadlines and preservation of a newer request when older work completes.
On-demand route refresh is now persisted separately with coalesced demand,
version-specific acknowledgement, durable retry/cooldown and lease recovery.
Changing work, complete effective profile or route choice requests another
version even during calculation. The local signal is only a wake-up hint;
polling recovers work without a browser or the originating process. Obsolete
assignments complete without provider work. Source-road preparation now persists
explicit geometry demand, observed input versions, leases and retry/cooldown.
Restart recovery does not require a browser or the originating local hint queue.
Fleet scheduling retains its checkpoint contract. Unrelated checkpoints remain
independent of the planning guards.

### 5. Clean reset, source bootstrap and cutover

CoreMigrationProbe creates an isolated PostgreSQL fixture on an existing server.
It seeds old identity and operational records, rejects migration without reset,
explicitly resets known operational tables, applies the new schema, compares
protected data, and verifies password/role behavior and new accepted work.
It never uses the working database as a disposable fixture or starts SQL
servers.

Before cutover, complete source-bootstrap and replay checks for the unified
owner, the full suite, release gate and required authenticated flows. Prepare an
explicit reset inventory, protected backup and compatible release. Retire all
old readers/writers before cleanup, preserve authentication/configuration, then
apply the schema and resume imports with fresh synchronization checkpoints.
Verify identity, source counts, unresolved assignments and active planning after
restart. A database-only migration is not a compatible release.

Financial modules, agreements and actual expense imports remain future work;
core completion does not mean those features are implemented.

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
[acceptance catalog](core-rebuild-scenarios.md#financial-design-probes).
These are planned requirements, not implemented tables, APIs or UI.

## Deferred product decisions

- Agreement examples, settlement periods and approval roles are required before
  compensation rules, not before core cleanup.
- Trip membership, owner/operator responsibility, fuel valuation and toll
  coverage must be decided before their affected calculations.
- Actual fuel/toll sources and formats remain later integrations.
- General ledger scope and multi-company activation are separate decisions.
  Do not invent default tenancy or an unused financial framework.
- Collect a permitted performance baseline before claiming production gains.

## Verification and reporting

Follow [test selection](../testing.md). Shared contracts and persistence require
both .NET assemblies and all JavaScript suites. Client changes require their
build and relevant browser checks. SQLite cannot prove PostgreSQL migrations or
cross-process concurrency.

Report implemented behavior, removed paths, checks actually run, remaining
adapters and unverified assumptions. Keep dated evidence in the archive.
Local verification does not imply deployment or working-database migration.

[record]: ../archive/2026-09/core-rebuild-implementation-record-2026-09-17.md

[readiness]: ../archive/2026-09/immutable-consumers-and-cutover-readiness-2026-09-17.md

[production]: ../archive/2026-09/core-cutover-2026-09-18.md
