# Consistency auditor and completion evidence

## Purpose and health boundaries

The auditor reports business-state inconsistencies. It complements process
liveness and service readiness; it does not replace their probes. A disputed
assignment must not restart the API or make unrelated work unavailable.
Expose operational findings separately from infrastructure probe responses.
A healthy process does not prove correct work; an empty partial scan does not
prove a healthy fleet. Runtime checks are deterministic, not an LLM loop.

## Required change review

For every changed stateful workflow, record:

- The invariant, authoritative owner, company scope and dependency versions.
- How the change affects existing persisted rows, not just newly created rows.
- How already-invalid records recover: ordinary reconciliation, a migration,
  an explicit owner command, or manual review with an actionable explanation.
- Commit, invalidation and publication order; rejection of late results.
- A regression starting from the previous invalid state, and an interleaving
  regression where concurrent work could republish that state.
- Verification evidence and remaining gaps, separating local tests from
  production verification. Do not call a production incident resolved merely
  because code was merged, tests passed or a revision became healthy.

Publication requires the user's release authorization. After an authorized
release, verify the affected record and a replacement or unrelated control
record through application reads. Record the revision, synchronization time,
expected result and observed result. Never delete history to make a check pass.

## Runtime contract

Reuse existing owners and canonical eligibility predicates. The auditor must
not become a second implementation of assignment or planning business rules.

- Company-scoped, bounded batch reads with cancellation and a continuation
  cursor or another explicit coverage boundary; no per-card database reads.
- No provider calls, route reconstruction, fuel optimization or large payloads
  in an audit. Run outside display reads with bounded cadence and work budgets.
- Findings identify rule, entity, dependency versions, observation time,
  severity and owner action. Exclude credentials, raw provider data and bodies.
- Distinguish an invariant violation from an expected unresolved review.
  Cancelled work with historical movement can require review without being
  eligible for current planning. Do not mark it completed to hide the review.
- Report checked scope, partial coverage, skipped rules and errors. A failed
  or incomplete scan is unknown, never a clean result.
- If findings are persisted, use stable company/rule/entity identity and
  version-aware updates. Resolve only after a successful authoritative recheck;
  absence from a partial page must not resolve an older finding.
- Publish only after commit. Deduplicate unchanged findings and notify on
  meaningful transitions, not every poll. Keep diagnostics bounded.
- API exposes results through MediatR under the existing administrative policy.
  SQL/provider details remain in Infrastructure behind Application interfaces.

## Recovery boundary

Detection is read-only by default. Automatic recovery, if implemented, must
use an existing owner command, recheck dependency versions and authorization,
be idempotent, targeted, rate-limited and observable. A stale calculation may
request preparation; it may not alter physical execution or resend a message.
Never silently close started work, modify real assignments, erase history,
reset fuel choices or send external messages to repair an audit finding.
Repeated failure becomes actionable review rather than an endless retry loop.

## Initial scope and extension

Start with cancelled source work remaining eligible for planning, unresolved
cancellation review and the planning version or pending-state evidence that
existing owners can expose cheaply. Each implementation must enumerate its
actual supported rules and limitations; this guide is not proof that all
route, ETA, fuel, messaging or document invariants are already monitored.
Add rules alongside future workflows using the same contract, not a parallel
scheduler, cache or page-specific read path.

Required checks include existing broken rows, repair through the owner,
company isolation, repeated scans, partial coverage, cancellation, bounded
query counts and late observations. Select affected and full checks using
[the testing guide](../testing.md). PostgreSQL tests require a safe isolated
fixture; never substitute the production database.

## Reconciliation design

This is the target design, not a statement of implemented coverage. Borrow
small control loops from established systems, not their infrastructure stack.
No Kubernetes, Kafka or separate AI service is required for this application.

### Rules and evidence

Each rule declares an ID/version, owning module, eligibility predicate,
minimal evidence projection, dependency vector, tolerance window, severity,
maximum batch cost and permitted recovery action. Module-owned readers supply
facts through contracts; a coordinator schedules checks without depending on
concrete services across forbidden module boundaries.

A finding contains company, rule/version, entity and stable occurrence key,
expected versus observed facts, source and execution revisions, observedAt,
firstSeenAt, lastSeenAt and lastTransitionAt. Keep evidence small. Use separate
fields for impact (warning/critical), condition (violation/review/unknown) and
workflow (open/acknowledged/resolved). Acknowledging is not resolving.
Reopening creates a new occurrence or increments its recurrence count.

Read related facts from a coherent snapshot or recheck their dependency vector
before reporting a violation. If the vector changed, schedule another check;
do not label ordinary concurrent updates as corruption. A result can resolve
only the same occurrence/version it examined. Never let an older clean result
resolve a newer failure. Time alone does not prove a version mismatch is safe.

### Triggering and bounded coverage

Use committed changes to mark affected entities dirty, coalescing by company,
entity and latest dependency version. Reuse existing durable demand where it
fits. If a trigger must survive process failure, commit its durable marker or
outbox record with the owner's write; an in-memory post-commit event alone is
not durable. Consumers tolerate duplicate delivery.

A slow recovery sweep catches lost notifications and already-invalid records.
Use keyset pagination, a captured high-water mark and a persisted checkpoint
where restart continuity is needed. Cycle through companies fairly so one
large tenant cannot starve another. Do not reset to page one every pass.
Work limits cover rows, database calls, elapsed time and concurrent passes;
no unbounded tasks per truck. Proposed limits must be configurable and measured
before claiming fleet capacity. Respect existing worker scheduling and leases.

Separate scope coverage from findings: cycle ID, scanned rows, high-water mark,
continuation, last successful pass, last complete sweep, errors and rule set.
A page with zero findings is only a clean page. A delayed or failed auditor
reports unknown/stale coverage, not green health. Track sweep lag, backlog,
query duration/count and repair attempts without per-entity metric labels.

### Transient versus persistent failures

A new assignment may legitimately await route preparation. A pending-state
rule needs queued/running evidence, expected completion deadline and the same
dependency version, not simply an old timestamp. Apply bounded grace windows
and rechecks for eventually consistent projections. Definitive invalid facts
such as forbidden cross-company publication do not need a grace window.
Known provider outages can annotate related symptoms; never suppress them
indefinitely or assume every simultaneous error has the same root cause.

### Repair and verification

Keep detection, repair authorization, owner command execution and verification
separate. Initial release may implement detection only. If safe requeue is
added, key it by finding occurrence and dependency version, limit retries with
backoff/jitter, and stop retries when newer evidence supersedes it. A successful
command means requested, not repaired. Resolve only after an authoritative
read confirms the invariant. Conflicting physical execution stays actionable
review. No direct table edits bypassing the owner.

Preserve evidence of failed verification and escalate to a human after a
bounded number of attempts. Expired leases do not guarantee an old worker
stopped; fence local publications and never use blind external retries.

### Current case: cancelled work with movement

For AMF1399-like cases, source cancellation and recorded movement are separate
facts. History remains accessible and unfinished physical obligations require
review. Shared eligibility must keep this cancelled source work out of normal
planning unless a supported explicit local authority overrides that decision.
The audit reports unresolved review separately from an incorrectly published
active assignment. Verify the replacement load remains selected and that old
route/ETA/fuel results cannot reappear after a delayed calculation.

A legacy-row regression must seed cancelled source plus a retained planned
leg and movement, run normal reconciliation, inspect board/map/planning reads,
repeat reconciliation, then recheck history and review. Do not substitute a
newly-created happy-path record for this test.

## Audit layers and release acceptance

1. Write-time invariants prevent invalid commits through the authoritative
   owner, database constraints and optimistic concurrency where appropriate.
2. Runtime reconciliation detects legacy and cross-projection inconsistencies.
3. Release review checks the regression, migration/reconciliation path and
   affected production record after an authorized rollout.

AI review assists layer 3; deterministic code owns layers 1 and 2. No claim of
universal correctness follows from a clean dashboard. For each rollout record
what was tested, what is deployed, what existing data was repaired, and what
requires review. A deployment can succeed while the incident remains open.

## Sources and adaptation

- [Kubernetes controllers](https://kubernetes.io/docs/concepts/architecture/controller/):
  reconcile observed and desired state through focused owners. We apply the
  pattern, not Kubernetes infrastructure or a second business-rule engine.
- [AWS transactional outbox](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/transactional-outbox.html):
  durable database/event coordination still requires idempotent consumers.
- [Google SRE monitoring](https://sre.google/sre-book/monitoring-distributed-systems/):
  prioritize actionable user-visible symptoms and separate them from possible
  causes. Avoid repeated alerts with no action and avoid an overly elaborate
  dependency model before there is evidence it is needed.

References reviewed on 2026-09-23. Budgets, schema and supported rules require
implementation evidence and measurement; these sources do not establish them.


## Continuous coverage review

Auditor coverage evolves with the product. Every feature, workflow change,
incident fix, migration and provider integration must review the dimensions
below. Not every dimension requires a runtime rule; mark it applicable,
inapplicable with a reason, or deferred with an owner and completion criterion.
Update affected rules and regression tests in the same change when practical.
Do not silently defer a newly introduced violation of an existing invariant.

| Dimension | Required review |
| --- | --- |
| Company and access | Ownership, cross-company references, roles and isolation |
| Source and execution | Cancellation, replacement, handoff, overrides, history |
| Derived results | Route, ETA and fuel dependencies, stale display, late writes |
| Background work | Lost demand, stalled leases, retries, restart and starvation |
| Persistence | Legacy rows, migration, rollback and post-commit publication |
| External effects | Send attempts, duplicate events, unknown delivery outcomes |
| Documents | Durable file state, links, missing objects, access and retention |
| Financial work | Currency, totals, attribution and revisions when implemented |
| User experience | Actionable errors, pending deadlines and recovery visibility |
| Cost and capacity | Bounded reads, payloads, queue size and no per-card work |
| Auditor itself | Coverage age, failed scans, late results and alert noise |

Maintain an implementation coverage register beside the actual rules or their
owning guide. For each rule record its ID/version, owner, invariant, detector
and test locations, trigger/sweep scope, budget, recovery path and status:
implemented, test-only, planned or unsupported. Link evidence rather than
copying rules into several documents. A domain listed in this table is not
therefore monitored. Release reports list changed coverage and remaining gaps.

After a real incident, review why prevention, detection or verification missed
it. Add a regression for the observed failure and a cheap runtime rule when
its evidence is reliable. Track false positives, repeated findings, missed
cases, detection delay and audit cost; adjust thresholds from observations,
not to hide unresolved work. Never broaden automatic repair merely to reduce
the number of open findings.

Cross-cutting changes require review of all affected domains and their shared
owners. Extending coverage must preserve normal synchronization and isolate a
problem to its affected entities. Audit findings must not globally disable
updates, trigger full-fleet recalculation or restart the service.

## Durable journal and technical escalation

Required by the product decision on 2026-09-23; implementation must explicitly
report which parts are complete. In-memory findings alone are insufficient.
Persist a finding before any recovery attempt. Keep durable occurrence and
attempt records, including requested action, dependency versions, outcome and
verification result. Crash recovery must preserve uncertainty about unfinished
actions. Record recurrence after verified resolution as a new occurrence; do
not overwrite previous evidence or count every repeated scan as a recurrence.

Policy-based recovery is a separate component with an explicit allowlist of
safe owner commands. An administrative diagnostic API exposes bounded journal
and incident pages with stable cursors and company authorization. An external
agent may poll this API when configured; the application cannot assume that a
Codex session is always running or that diagnostic credentials never expire.
Never embed agent credentials or a public journal endpoint in the application.

Repeated verified failures or unsuccessful recovery create a deduplicated
technical incident. Thresholds and observation windows must be declared per
rule and configurable; scanning the same open finding must not create repeated
incidents. Store delivery/acknowledgement separately from finding resolution.
A polling consumer checkpoints only after processing a page and tolerates
repeated pages. Failure of the alert consumer must not stop synchronization.
Operational problems requiring dispatcher action remain visible to dispatch;
technical escalation must not hide user-impacting errors.

Include restart, duplicate observations, late attempt results, recurrence,
failed alert consumption and cross-company journal access in regression tests.

## Implementation coverage register

State on 2026-09-23, local only: not deployed, and the migration
`AddConsistencyJournal` is not applied anywhere. This lists what the code
does; everything else in this guide is target design.

| Rule (v1) | Owner | Condition | Recovery | Status |
| --- | --- | --- | --- | --- |
| `execution.cancelled-source-runnable` | Execution reconciliation | violation, critical | none; sync repair pass | implemented |
| `execution.cancelled-source-held` | `CloseCancelledExecution` | review, warning | none; dispatcher | implemented |
| `execution.planning-change-overdue` | `ExecutionPlanningOperation` | violation, warning | none | implemented |
| `routing.planning-refresh-overdue` | `PlanningRefreshOperation` | violation, warning | requeue, allowlisted | implemented |
| `routing.source-road-overdue` | `BaseRouteOperation` | violation, warning | none; the wait line names the step | implemented, detection tested |
| `messaging.unread-arrival-behind` | `InboxRecorder` | violation, warning | none; next driver message | implemented, detection tested |
| `messaging.outbound-overdue` | `OutboundMessageOperation` | violation, warning | none; the outbox's next pass | implemented, detection tested |
| `messaging.kept-status-unapplied` | `KeptStatusReconciliation` | violation, warning | reconciled by the outbox once a minute | implemented, detection and repair tested |
| `fuel.import-message-skipped` | `ImportFuelDiscountsHandler` | review, warning | none; fix the cause within the mailbox window | implemented, detection tested |
| `messaging.accepted-without-status` | `DriverMessagingWebhookHandlers` | review, warning | none; delivery unknown, never marked failed | implemented, detection tested |
| `dispatch.filed-document-unavailable` | `FileMessageAttachment` | violation, warning | none; stored file's owner | implemented, detection tested |
| `routing.base-road-leaves-country` | `BaseRouteService` | violation, warning | none; rebuild the load | implemented, detection tested |
| `routing.base-road-border-unverified` | `BaseRouteService` | review, warning | none; dispatcher | implemented, detection tested |
| `storage.file-on-disconnected-storage` | `FileStore`, `DisconnectStorageCommand` | violation, warning | none; reconnect or move the files | implemented; detection tested under PostgreSQL only, SQL translation offline |
| `routing.fuel-handover-uncertain` | `FuelIssueSender` | review, warning | none; dispatcher asks the driver | implemented, detection tested |
| `routing.fuel-handover-unrecorded` | `FuelIssueSender` | violation, warning | send the plan again; recorded from the accepted attempt | implemented, detection tested |
| `routing.route-passed-work-open` | `TruckPlanningInputs` | review, warning | none; dispatcher | implemented, detection tested |
| `execution.source-closed-work-open` | `AcceptExecutionSourceChanges` | review, warning | none; dispatcher | implemented, detection tested |
| `execution.source-review-open` | `ExecutionImportAcceptance` | review, warning | none; dispatcher | implemented, detection tested |
| `routing.summary-names-current-work` | `PlanningSummaryCache` | violation, warning | none; next preparation | implemented, detection tested |

Detectors live in `Application/Features/Execution/Audit` and
`Application/Features/Routing/Audit` behind `IConsistencyRule`; the refresh
rule reads through `IPlanningRefreshStore` and the road rule through
`ISourceRoadStore`. Tests:
`Server.Tests/Dispatch/ConsistencyAuditTests.cs`,
`Server.Tests/Dispatch/HeldExecutionTests.cs` and
`Server.Tests/Persistence/ConsistencyAuditSqlTests.cs`. The border rules
read the verdict stored with each base road, never its geometry, and report
only work that can still run. A road that could not be fully placed is
`unknown` and reviewed, never counted as staying. Roads saved before
migration `RecordBaseRoadBorderCheck` (not applied anywhere) are covered only
after `BaseRoadBorderCheck` has judged them; until then they are unchecked
(`Server.Tests/Routing/BaseRoadBorderAuditTests.cs`).

The current-work rules (not released) are described in
[current-work.md](current-work.md#auditor-rules). The route-passed rule
asks the planning inputs' owner for the whole fleet per page rather than
one statement, and the summary rule reads this process' summaries only.
Tests: `Server.Tests/Dispatch/SourceAheadAuditTests.cs` (and
`Server.Tests/Persistence/SourceAheadAuditPostgresTests.cs` on the
isolated fixture) and
`Server.Tests/Routing/PlanningSummaryRefreshTests.Audit.cs`.

Implemented behavior:

- `ConsistencyAuditOperation` sweeps every active company every
  `ConsistencyAudit:IntervalMinutes` (10). A pass reads key-ordered pages of
  `PageSize` (100), at most `MaxPagesPerRule` (5) per rule and
  `BudgetSeconds` (20) in total, one statement per page, resuming from the
  rule's cursor; the starting rule rotates. Skipped, continuing and failed
  rules are reported per pass. There is no dirty-entity trigger yet: detection
  delay is up to one interval plus the sweep length.
- Findings, events and incidents are durable (`ConsistencyFindings`,
  `ConsistencyEvents`, `ConsistencyIncidents`). A finding is written when first
  seen, before any repair. An unchanged re-observation updates `LastSeenAt`
  only and writes no event; an older observation changes nothing.
- A finding resolves only when a complete sweep that began after it was last
  seen no longer observes it; the event records the sweep start and repair
  attempts. A partial sweep resolves nothing. Sweep cursors and coverage are
  in memory: after a restart every rule reports `never-run` and the report
  status is `unknown` until each rule completes a sweep. History is kept.
- A finding seen again after resolution is a new occurrence and opens or
  updates the one incident for that company, rule and entity (`recurred`).
- Recovery (`ConsistencyRecovery`) runs after detection with the remaining
  budget. Only allowlisted rules (`RepairRules`, default the refresh rule)
  are repaired, at most `MaxRepairsPerPass` (5), once per
  `RepairCooldownMinutes` (30), `MaxRepairAttempts` (3) times. The attempt is
  saved as `pending` before the owner is asked; a crash leaves it `pending`.
  The requeue re-checks the requested version and lease. Exhausted attempts
  escalate the finding and open a `repair-exhausted` incident.
- Journal writes of one company are serialized end to end: each operation
  clears its context's tracking, opens a transaction and takes
  `LockConsistencyJournalAsync` (a PostgreSQL transaction advisory lock per
  company) before reading finding state, then decides, assigns sequences from
  `ConsistencyJournalHeads` and commits. A stale pass therefore cannot close a
  finding observed after it read, move `LastSeenAt` back or lose a repair
  attempt; repair steps re-read the finding and re-check eligibility and the
  attempt number under the lock.
- Every rule and the recovery step run in their own unit of work (scope and
  database context). A failed save discards that unit; the next operation
  starts from committed state.
- Admin API (`Admin` policy, serving company only):
  `GET /api/diagnostics/consistency` (coverage, open findings, allowlist,
  unchecked invariants), `GET .../consistency/events?after=&limit=&kind=`
  (per-company sequence cursor, limit 1-500; `next` and `hasMore`; sequences
  commit in order, so resuming from `next` misses nothing and re-reading a
  page is safe), `GET .../consistency/incidents?after=&limit=` (Id keyset
  over current incident rows; incident changes arrive as events) and
  `POST .../consistency/run` (one bounded pass now).
- Transitions log once with the pass id; unchanged passes log nothing.
  Liveness and readiness are unaffected; the auditor has no heartbeat.

Known gaps: incident thresholds are global rather than per rule; consumer
delivery and acknowledgement are not stored (a poller keeps its own cursor);
per-company fairness is one budget per company per pass, not measured at
scale; the planning demand rules do not distinguish superseded demand. On
the isolated PostgreSQL fixture, `ConsistencyJournalPostgresTests` shows a
second journal writer waiting on the advisory lock and sequences committing
in order, and `MessagingPostgresTests` runs the newer rules' reads; the
older rules' SQL is checked for translation offline.
The unchecked list in the report names the invariants this version does not
cover at all.
