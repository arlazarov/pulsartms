# Architecture audit conclusion — September 26, 2026

## Scope and disposition

Codex performed this audit directly in `codex/architecture-guardrails`, based
on root commit `ccb0838f`, with the corrections recorded in
[the implementation record](architecture-guardrails-2026-09-26.md).
Claude's later uncommitted map/design work and the isolated Driver Pay branch
are not part of this candidate. Neither root files nor production were changed.

This closes the architecture assessment and its finding inventory, not every
remediation. The application is not certified defect-free or ready for horizontal
scaling. The historical [initial audit](application-audit-2026-09-26.md) remains
unchanged; its statements that nothing was fixed describe that earlier snapshot.
This conclusion and the correction record give the current local disposition.

The inventory now includes 2,592 maintained files: Application 552,
Domain 291, Infrastructure 171 and API 39. Inventory is not line-by-line review.
The work combined ownership/caller tracing, source and compiled boundary checks,
controlled concurrency regressions, query-count checks and automated suites.
Visual acceptance, production timing, penetration testing, restore exercises
and exhaustive proofs of financial/HOS formulas were not performed.

## Results by original finding

| Finding | Local disposition | Remaining obligation |
| --- | --- | --- |
| A1 camera isolation | Corrected and regression-tested | Authorized release and production verification |
| A2 channel admission race | New writes serialized and tested | Existing duplicate ownership inventory and recovery |
| A3 unbounded planning discovery | Query bounded before materialization | Production foreground/background measurements |
| A4 cross-company invalidation | Scoped generations and relay corrected | Drain old versions at release |
| A5 hidden send persistence failure | Exact conflict classification corrected | Normal operational error monitoring |
| A6 delivery policy in Routing | Messaging owns recipient/channel readiness | No second real channel implemented |
| A7 feature cycles | One real back-reference removed | Concrete cycles below remain |
| A8 duplicated station price selection | Server-selected display inputs reused | Claude owns final rendered design verification |
| A9 repeated build/read work | Incremental JS and count coalescing corrected | No end-to-end speedup or universal test optimum claimed |
| A10 cross-instance notifications | Limitation confirmed, not implemented | Shared durable delivery before horizontal low-latency use |

Three additional Messaging defects were corrected locally: non-atomic overflow
acknowledgement, a held old-account read suppressing new-account signals, and
retention of empty company subscriber groups after the last reader left.
The last fix synchronizes publish, subscription admission and removal. Queue
writes do not wait; no database/provider call or awaited work runs in the lock.
The mailbox owner's existing admission bounds remain unchanged. Tests cover
repeated company churn, duplicate disposal, overlapping release/admission,
company isolation, overflow and account changes with a held response.
These are transient state invariants; there are no persisted rows to repair.
Runtime business auditing cannot replace their concurrency/lifecycle tests.

## Dependency conclusion

There are 19 recorded compiled-signature edges and 31 distinct cross-feature
`using Application.Features.*` pairs. These are different measurements. Imports
can be unused, while signature inspection misses calls made only inside method
bodies. Neither count alone is the full executable call graph. The existing
architecture test was not weakened or expanded to accommodate new edges.

Source tracing identified these distinct ownership problems and retained uses:

| Path | Assessment | Owner and completion criterion |
| --- | --- | --- |
| ExecutionSourceAssignment -> DispatchWorkspaceData/proposals | Accepted execution decodes a commercial workspace representation | Dispatch supplies normalized source assignment facts; Execution consumes those facts, preserving source reconciliation/history tests |
| Routing -> SynchronizationOptions | Routing lifetime/retry settings are owned by the scheduler's configuration object | Routing owns its effective policy contract; preserve current configuration compatibility and timeout/lease relationship |
| Addresses/Shipments -> ExecutionCommandSupport.ActorAsync | Shared access checks are reached through an execution-specific helper | Identity owns a scoped actor resolver; preserve active-user, company and Admin/Dispatch checks |
| Costs -> MileageAccess | Expense access reuses another feature's private policy helper | Identity owns actor resolution; keep expense-specific permission decisions with Costs |
| Dispatch -> Execution | Accepted assignment edits participate in the execution transaction | Retain owner transaction; remove screen-data coupling, not the atomic boundary |
| ETA -> Routing/Execution | Forecasts require authoritative roads and captured work | Keep one forecast owner and dependency versions; narrow contracts without copying formulas |
| Mileage -> Routing/Execution | Recorded mileage validates saved road and execution identity | Preserve historical evidence and revision locks; no independent road-validity formula |
| Synchronization -> Fleet/Fuel/Routing/Dispatch | Scheduler orchestrates owners | Prefer owner operation contracts; do not move provider work into consumers |
| Border -> Shipments | Border uses shipment facts for crossing preparation | Keep shipment identity and revisions; this is not automatically an erroneous dependency |

Examples such as `IExecutionPlanningOperation` importing Synchronization while
inheriting the neutral `IBackgroundOperation` show why an import count is not
proof of runtime coupling. Moving namespaces or adding forwarding interfaces
only to lower a number would not close these findings.

The shared actor helpers also repeat authenticated identity, role and active
user reads. Their whitespace handling is not identical. A consolidation must
first preserve explicit denied/active/foreign-company cases and prove work
counts within one operation; blanket static caching of authorization is unsafe.
This is a remaining remediation, not a claimed security exploit.

## Messaging multi-instance conclusion

`MessagingEvents` is intentionally process-local. `MessagingMailboxes` bounds
admission, replaces unknown instance-local mailbox IDs with a resync response,
and serializes readers. `MessagingSignals.RepairEvery` is 60 seconds; this is
repair, not a low-latency distributed event mechanism. The checked deployment
script still sets `--max-instances 1`; live settings were not read in this audit.

The write chain was traced through inbound messages, replies, conversation
creation/driver changes, claims, broadcasts, outbox results and inbound media.
Notifications follow persistence. A notification after commit can still be lost
on process failure, so a reliable shared design cannot merely relocate Publish.
Per-conversation revisions are not a company-wide ordered cursor; inbound arrival
sequences do not cover claims, statuses, media and other changes.

Messaging owns the next implementation. Closure requires committed company-scoped
change evidence, bounded reads, retention/resync, reconnect authorization and
tests with separate instances, reordered commits, restart and disconnected readers.
A database-generated ID alone is not sufficient: transaction A may allocate a
lower ID and commit after B, behind a reader's cursor. The selected implementation
must prevent that gap or explicitly repair it. Cache invalidation rows must not
become an unrelated untyped messaging bus. No schema or transport was invented
here without closing those ordering and lifecycle requirements.

Horizontal scaling also affects process-local planning/HOS/provider state.
Messages alone cannot certify the whole server for multiple instances.

## Cohesion and repeated work

FleetMap remains one lifecycle across 13 partial files; FuelPlanningService and
RoutePlanningService each coordinate several stages. File sizes are signals,
not failures. Splitting partial files again does not create independent owners.
The meaningful next boundaries are source-assignment translation, effective
planning policy, authenticated actor resolution and durable message signaling.

Shared planning display remains owned by PlanningSummaryReader, with background
preparation and metadata-only reads. Multiple screens are not themselves proof
of duplicate ETA calculations. The concrete eliminated work is recorded with
call-count/isolation tests in the correction record; no claim that every request
is now calculated exactly once is made.

Verification should remain: focused regression while editing, one combined
affected/dependent run at a coherent boundary, then one full stable gate for
shared contracts/persistence/authentication or release. Keep existing build output
and isolated database fixtures. Separate test execution from provider/database
latency and browser rendering; do not fix flaky timing by retrying until green.

## Release and existing-state boundaries

All corrections are local. No migration, production data repair, external send,
deployment or merge into Claude's checkout occurred. Existing channel ownership
needs an explicit bounded inventory and authorized resolution; conversation
history must not be moved silently. Old process caches disappear with the normal
release drain, not by resetting operational data.

The remaining remediation owners and closure criteria are explicit above.
They must not be converted to a claim that passing tests completed them.
The final gate passed: server 3,773, Client 1,240 and JavaScript 660 tests,
with no failures or skips. TypeScript and architecture checks were included.
The [source manifest](architecture-audit-conclusion-2026-09-26-evidence.json)
records the reviewed boundary hashes and all lexical feature-import pairs.
The final gate details are recorded in the implementation record.
