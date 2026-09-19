# Core rebuild acceptance scenarios

Status: acceptance specification with partial implementation coverage.
Synthetic examples below are design probes, not approved pay agreements.
Read the [core specification](core-rebuild.md) for ownership and scope.
Phase reports identify checks actually run; these scenarios are not a claim that
the entire canonical itinerary or financial workflow is implemented.

## Operational cases

### C01: ordinary imported load

Given load L1, truck T1 and pickup A followed by delivery B, importing twice
retains the same local identities and creates no duplicate work. The canonical
reader returns A then B, with the same assignments used by all calculations.

### C02: repeated address

Given A1 -> B -> A2 where A1 and A2 share coordinates, confirming A1 does not
complete A2. Route progress, documents and evidence retain occurrence identity.

### C03: local appointment override

After a local appointment edit, an older import cannot erase it. An accepted
new source proposal changes only fields allowed by the ownership policy.
Returning the field to source control is explicit and versioned.

### C04: unfinished overdue and future work

An overdue unfinished L1 remains ahead of its known successor L2. Changing the
Board date/search/page does not remove L1 from calculation inputs. Ambiguous
successor order yields an explicit unresolved dependency, not arbitrary order.

Phase 1C covers the resolved ETA subset: a unique started root retains its ETA
when future order is unknown; tied appointments and multiple started legacy
loads do not acquire precedence from load numbers. Full overdue scope remains
pending in the existing consumers. Phase 1D's complete snapshot includes overdue
and planned assignments and retains unresolved truck paths with their visits.
Phase 1E makes ETA consume this snapshot. Screen ordering/filtering cannot
choose
its root; typed exclusions preserve the existing calculation scope. Unstarted
overdue work is still excluded from calculation, but remains in the snapshot and
its input signature. Native continuation remains unsupported and explicit.

Phase 1F makes saved preview and live planning select work from that complete
snapshot too. Their existing eligible display subset remains explicit. A test
removes current work from the Board row and verifies both readers still select
the stored current load; malformed/source-review native roots block fallback.

Current PostgreSQL rehearsal additionally verifies database writer revisions,
old/new truck membership, normalized number-only sources, deleted native links,
initially absent profiles and global resource changes. Separate connections
confirm independent truck publications progress while same-truck writes,
new membership and rate changes wait. Unrelated checkpoint writes remain
independent. Empty upgrade/downgrade and the populated clean transition exercise
trigger installation/removal. These checks supersede the PostgreSQL-unverified
limitations in the earlier phase records below; production throughput remains
unmeasured.

Phase 1G verifies fuel includes overdue assigned work hidden by display date
filters while retaining planned legacy work only in the complete capture.
Saved native/legacy signature parity is tested. Horizon assembly keeps captured
visit facts even when the database changes before assembly starts.

### C05: driver-only change

D1 hands responsibility to D2 on the same T1. Historical work remains linked
to D1; subsequent work uses D2. Fuel stays with T1 and HOS switches to D2.
Planning the change alone does not activate D2.

Phase 1F captures the selected driver with the itinerary and attaches HOS after
the read scope finishes. Tests cover an active native driver overriding the old
truck driver, a native assignment with no driver, and a driver change during
the HOS request. A single response cannot mix captured work with the new driver's
clocks. The next invalidated read observes the new assignment.

### C06: overnight drop and hook

T1 drops trailer R at site S on day one; T2 receives it on day two. The parked
interval remains explicit. T2's arrival or scheduled receipt is not actual
receipt. T1 release does not complete T2's independent action.

Phase 1C reads both transfer confirmations for incoming ETA readiness. Timestamp
presence alone does not confirm either action; confirmed actions with unknown
times remain valid. Native successor forecasting remains outside this slice.
Phase 1D additionally retains boundary visit IDs, planned and actual transfer
times, completed predecessor references and native successors in the complete
read result. This does not enable successor ETA calculation.

### C07: exchange of two loads

Two trucks exchange confirmed responsibilities. Each participant validates its
revision; one failed action cannot partly overwrite assignments. Each truck's
planning results remain independent and each load preserves both portions.

### C08: duplicate command and conflicting retry

Retrying the same confirmed action key/payload returns its prior result.
Reusing the key with changed assets conflicts. Neither creates another event.

### C09: competing assignment

Two processes try to confirm incompatible work for the same resource. At most
one conflicting transition commits; the other reports conflict and retains
its original facts. Requires isolated PostgreSQL verification, not SQLite proof.

### C10: empty and personal travel

Truck travel from delivery A to home and then pickup B remains two intervals.
Attribution follows the selected policy with its version. Personal car travel
creates no truck mileage. Bobtail is not added twice to empty plus total.

### C11: missing odometer evidence

A reset or gap retains uncertainty. Planned 500 miles does not silently become
observed 500 miles. Any approved fallback for compensation records the contract
rule, actor and chosen evidence separately from observed distance.

### C12: shared physical movement

One physical 100-mile movement serving two loads remains one movement. An
explicit 60/40 allocation yields 60 and 40, not two 100-mile observations.
This probes future extensibility; it does not enable multi-load editing now.

### C13: correction after completion

Correcting a confirmed event retains previous evidence and the correction
reason/actor. Derived current projections may change; approved compensation
and issued invoices remain unchanged until an explicit adjustment is created.

### C14: stale calculation and retained display

Calculation at revision 7 finishes after revision 8 is accepted. It cannot
replace revision 8 results. During same-identity refresh the last valid display
remains available; it is not reused as current after switching to another load.

Phase 1E invalidates ETA when excluded itinerary work changes, preserves
geometry
reuse for schedule-only changes, and rejects revalidation inside an older caller
transaction. Cached assignment reads cannot override the fresh snapshot.
Existing
publication guards remain necessary; this does not prove cross-process atomicity
for every legacy change.

Phase 1F verifies board/dispatch/execution invalidation of captured display work.
Updated commodity/notes refresh without changing saved geometry, and existing
known-revision responses still omit geometry. Display caches retain bounded
freshness; they are not used to certify fresh calculation publication.

Phase 1G verifies fresh fuel capture bypasses a warm display cache and rejects an
older caller transaction. Automatic search and manual-save tests change visit
metadata during a price read without invalidation; both reject publication and
retain the previous route compatibility copy and truck-owned fuel plan. The
read-to-write gap identified here is addressed for canonical work by phase 1I.

Phase 1I injects changes immediately before publication for base/live routes,
progress, route choices, fuel search/edit and ETA. The final comparison runs
inside the result transaction. Independent SQLite connections verify that both
row updates and new queue membership cannot pass an active publication scope.
Disposal rolls back all written result parts. ETA commit failure does not leave
an uncommitted memory result. Unsupported providers and existing transactions
fail closed. The PostgreSQL source-table lock protocol and closure inventory are
checked statically; real PostgreSQL execution and contention remain untested.

Phase 1J covers standalone profile saves at the same boundary and rolls profile
changes back with both fuel copies on price, work, write and commit failures.
Uncached validation rejects changed effective profiles or fleet preferences even
with warm display caches. Fresh saved-rate reads retain validity and explicit
preference rules without calling a provider. These cases do not establish
atomic settings/rate version ownership across processes or a profile edit token.
Legacy and native cases repopulate the route cache before fuel commit and verify
that a subsequent read returns the committed fuel copy for the exact scope.

Phase 1K changes endpoints, current price, predecessor cancellation, new
intermediate work and unknown pickup history at the publication boundary. Both
reservation and final road publication validate captured history in their
owned transaction. Failed financial writes or commit roll back final road and
rates while retaining the committed retry reservation. Retrying or saving the
same context cannot persist rolled-back values. Provider calls have no open
publication transaction. Completed native history hydrates in the candidate
read transaction; later native revisions invalidate the token. Supplied
current facts survive caller mutation during lookup. Pure projections retain
completion, handoff and unknown-start guards without sharing mutable entities.

Phase 1L carries historical batches through ETA and fuel publication. Tests
change completed-load endpoints, completion revisions, cancellation and unknown
history after the earlier input check. The remaining-work signature stays equal,
but publication rejects the result and preserves previous forecasts/profile/fuel
copies. ETA removes the uncommitted memory entry; history-only changes can reuse
geometry timing. Unknown history, onward arrival policy and original native
lookup batches remain explicit dependencies. These cases do not certify
PostgreSQL execution or production throughput.

Phase 1M changes stored dimensions and fleet preferences immediately before
live route or ETA publication without invalidating display caches. Stale
builds cannot restore an older profile; rejected ETA retains saved forecasts
and drops its uncommitted memory entry. Automatic route builds reject an
already-stale profile before provider calls. Manual builds can intentionally
change dimensions. Automatic progress/reroutes reject late dimension changes,
while fuel-only changes do not reject road ETA. Independent SQLite connections
check settings/profile updates and inserts while a publication holds its scope.
The exact source/lock inventory now includes both settings tables. These checks
do not prove PostgreSQL behavior, checkpoint-rate ownership or saved-road
version ownership.

Phase 1N runs the production rate store through the shared publication scope.
Regressions cover changed and initially missing rates at automatic/manual fuel
publication, retained profile and both fuel copies, explicit fleet-rate
precedence, provider work outside the write transaction, commit/cancellation
failure, unchanged caches after failure and lease release/retry. Independent
SQLite connections check exclusion between result publication and rate saves.
The architecture check requires protected rate writes while keeping the
checkpoint table outside the source-lock inventory. Scheduler coverage checks
that an explicit planning retry defers work without success/failure changes.
PostgreSQL execution and unrelated-row concurrency remain unverified.

Phase 1O changes saved roots, future roads and connections immediately before
forecast publication. Regressions cover replacement, deletion, cleared geometry,
initially missing roads appearing, native root assignment metadata, a completed
root becoming active and a stale cached root against newer metadata. Saved
forecasts remain unchanged and unpublished memory entries are removed. Cold
geometry/version mismatches cannot fill the timing cache, and unchanged roads
and fuel-only root writes still publish. Final checks transfer metadata only.
Independent SQLite connections test updates/inserts for all three saved-road
tables; the architecture inventory verifies the fourteen-table source closure,
including Infrastructure SQL. These checks do not establish PostgreSQL runtime
behavior, fuel road validity or production contention limits.

Phase 1P extends road dependencies through fuel publication. Regressions inject
late root, base and connection changes during automatic calculation, manual
save and reset. They preserve the previous profile and both fuel copies without
cache invalidation or provider calls. Native row/plan revisions and ownership,
stale cached roots, fallback replacement, newly present base roads and onward
connections without eligible exit prices are covered. Repeated reads cannot
discard an earlier conflicting observation. Unchanged roads, fuel-only writes
and unused current baselines still publish. ETA and fuel both ignore visited
dictionary key order. Final metadata checks exclude geometry columns; the
transaction requirement and validation-before-writes ordering are checked.
These tests do not establish PostgreSQL runtime behavior or post-commit fuel
refresh after historical/road corrections.

Phase 1Q rejects stale routing profiles in base preparation and route-choice
preview/save, including warm-cache misses and changes during provider work or
at publication. Checks preserve previous drafts, choices and roads; native
choice revisions do not advance on failure. Completed native base sections
remain completed. Fuel-only settings allow publication, unassigned work retains
default routing settings, and manual dimension changes remain supported when
stored restrictions are unchanged. Standalone base preparation rejects an
outer transaction before provider work. Commit-failure tests roll back the base
and native planning requests. Architecture checks require settings validation
after publication begins and before writes. PostgreSQL contention and remaining
standalone work-input capture are not established by these SQLite checks.

Phase 1R validates persisted fuel-road dependencies after a valid commit. Warm
fuel summaries cannot hide a changed/deleted current road, native assignment,
future base, fallback or connection. Restoring a valid calculation replaces its
dependencies. Summary reads retain compact evidence without geometry, exclude
completed earlier blocks and retain the onward connection. Tracking-only and
fuel-only updates remain reusable; strict publication still rejects changed
progress during calculation. Missing legacy evidence is retained as stale, and
automatic refresh runs without waiting for quote changes. Manual choices are
preserved; failed refresh retries without deleting the previous result. SQLite
and unit checks do not establish historical correction detection before road
metadata changes, PostgreSQL runtime behavior or production performance.

### C15: historical actor

Deleting or disabling a login does not hide its documents or confirmations.
The historical actor label remains explainable and the file is discoverable.

Document uploads now capture the historical author label, and list/retry reads
use it without joining the current account. Regression checks cover rename,
disable/delete, stable successful retry, retained download and metadata-only
listing. Immutable execution revision facts also retain the actor label after
account changes. Activity/workspace records already capture their own labels.
These local checks do not mean the pending schema has reached production.

### C16: provider outage and process restart

Saved reads do not fetch a provider. An outage preserves known facts with
honest freshness. A worker restart/replayed job does not duplicate a command
or publish results from superseded inputs.

Phase 1F retains the provider-free saved-preview checks and cold/warm database
query budgets. HOS is fetched separately by live planning, once per requested
batch, and is never requested for saved preview. Multi-process replay remains a
later persistence/work-delivery slice.

## Financial design probes

### F01: effective contract version

For this synthetic agreement, completion selects the rate version: 100 approved
miles at USD 0.60 produces USD 60.00. Changing the rate to USD 0.65 afterward
does not alter the approved USD 60.00. Real selection policy remains to be set.

### F02: eligible revenue components

A synthetic 25% agreement includes USD 1,000 line haul and excludes USD 200 fuel
surcharge. The result is USD 250, not USD 300. Snapshot selected lines and their
versions; do not infer eligible revenue from the load's displayed total.

### F03: correction and duplicate settlement run

An approved USD 60 earning needs a USD 5 correction. Retain USD 60 and create
one linked USD 5 adjustment. Retrying the run creates neither another original
earning nor another adjustment. Approval is not proof of payment.

### F04: invoice and partial payment

An issued USD 1,000 invoice receives USD 400, leaving USD 600. A USD 100 credit
then leaves USD 500. Replay of either event leaves USD 500. Payment allocation
has its own identity; editing the source load cannot rewrite the issued invoice.

### F05: mixed currencies and crew

USD and CAD are not summed without an explicit conversion basis. A team
agreement explicitly chooses earnings allocation; two drivers do not create
two copies of physical miles. No crew share is inferred from GPS alone.

### F06: toll quote identity and missing coverage

A quote for route revision 7 finishes after revision 8 is selected. It cannot
become revision 8's toll estimate. Changed vehicle/payment assumptions require
a new quote. Unsupported or unpriced route sections remain unknown rather than
zero; the view identifies incomplete coverage, including estimated fuel access.

### F07: refueling across trip boundaries

A synthetic trip opens with 100 gallons, buys 100 and closes with 120 under
consistent evidence. Its consumption is 80 gallons and its purchased quantity
is 100. A chosen valuation policy prices consumption separately from purchase
cash outlay; adding both as trip fuel expense would double count. Missing tank
or opening valuation evidence leaves actual consumption cost incomplete.

### F08: owner/operator payment and included expenses

For a synthetic equipment-owner agreement, USD 1,000 includes the driver's
USD 300 labor component paid by that owner. Company cost remains USD 1,000,
with an optional component breakdown; it does not become USD 1,300. A different
agreement may require separate company-paid labor. Payer and included items
must decide the result. A fuel deduction from an owner's settlement must not
create a second fuel expense in the company trip view.

### F09: toll and fuel transaction reconciliation

A trip's USD 40 toll estimate is followed by a matched USD 43 actual charge.
Show the original estimate, USD 43 actual and USD 3 variance. Do not report
USD 83 actual expense. Reimporting that charge or the same fuel purchase leaves
one source transaction; corrections retain a link to the superseded evidence.

### F10: shared cost allocation and currency

A synthetic USD 50 toll transaction serving two loads uses an explicit 60/40
allocation: USD 30 and USD 20 reconcile to USD 50. Adding their parent trip
view must not count the parent and its allocations twice. A CAD transaction
retains its original amount and conversion basis; changing today's rate does
not rewrite an approved historical cost or compensation amount.

### F11: incomplete forecast and approved history

Fuel is estimated, one toll section is unsupported and a driver's agreement is
unresolved. Known line items remain visible, but the total is marked incomplete.
Later receipt or contract evidence updates the current projection without
erasing its baseline. Approved compensation and issued accounting records
change only through linked adjustments, not an automatic forecast refresh.

### F12: delayed actual fuel import

A trip first displays estimated fuel cost with actuals unavailable. A purchase
arrives after completion and a subsequent driver/truck assignment change.
Match against historical work at the purchase time, retaining source identity
and import time separately. Show matched purchase actuals alongside the
original plan; an uncertain match remains unallocated. Reimporting does not
duplicate the expense. Actual trip consumption cost still requires the chosen
valuation and allocation evidence, and approved records require adjustments.

### F13: delayed actual toll import and correction

A toll passage occurs during trip A, but its charge arrives during trip B
after a vehicle/transponder reassignment. Historical passage and assignment
evidence links the charge to A; posting time alone cannot allocate it to B.
Missing evidence leaves the allocation unresolved. Reimporting the transaction
creates no duplicate. A later refund links to the original charge and adjusts
the actual projection while preserving the quote and approved history.
A charge without a prior estimate remains valid evidence; incomplete feed
coverage does not imply zero actual toll expense for the remaining work.

## First-slice test mapping

- C01/C02/C04/C14: characterize existing TruckItineraryTests,
  ExecutionItineraryReadTests and ExecutionBoardIndexTests, then assert parity
  against the new reader's identities, ordering and revisions.
- C03: preserve DispatchWorkspaceImportTests and ExecutionSourceFactsTests.
- C05-C08: preserve ExecutionTransferBoundaryTests and workspace transfer tests;
  the initial reader must expose their facts without changing write semantics.
- C10-C13: retain MovementIntervalTests, OdometerEvidenceTests and allocation
  tests; extend only when their owning slice changes.
- C09/C16: add multi-process/relational fixtures in the persistence/work slice.
- C15: separate document lifecycle regression, not a reader responsibility.
- F01-F05: design examples only until agreement and accounting scope decisions.
- F06-F13: trip-cost design examples only; toll, purchase, allocation and
  compensation implementation and regression checks remain pending.

This mapping identifies existing test homes, not a claim that each scenario is
already covered completely. Record actual executions in dated stage reports.
