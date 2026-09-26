# Application architecture and work audit — September 26, 2026

## Scope and evidence

This review was performed directly by Codex, without delegating the audit.
The request was application-wide: clear ownership, extensibility, redundant
work, correctness boundaries and more efficient verification.

The starting and finishing HEAD was
`186ce56225edd0c9e6c805df9dc330de98e042b8`. The working tree was not a release
candidate: another task was changing Fleet/Fuel UI and duty-status reads.
Those changes were inspected where relevant but are not certified complete
by this report. Findings below refer to the source read during the audit;
recheck them against later changes. The companion
[evidence manifest](application-audit-2026-09-26-evidence.json) identifies
the critical source snapshots and diagnostic output.

This is a broad architecture review with selected deep reproductions. It is
not a line-by-line proof of every method, a penetration test, a legal HOS
validation, or a production performance measurement. Unverified areas are
explicitly listed below. No application behavior, database schema, deployed
configuration or production data was changed by the audit.

The tracked-source inventory counted 2,563 maintained source files. Application
has 548 files / 60,790 lines; Domain 292 / 16,060; Infrastructure 171 / 14,877;
API 39 / 2,637. Client C#/Razor, browser sources and styles account for another
646 files. Generated migrations and current untracked work are outside these
inventory counts. Size is a review signal, not a failing rule.

## Assessment

The existing modular monolith has useful foundations: server-owned formulas,
company-scoped persistence, explicit write owners, retained planning summaries,
revision guards, recorded send attempts and separate provider adapters.
The evidence does not justify replacing the whole database or introducing
distributed services. The first work is to close isolation and ownership
gaps, then remove expensive reads and narrow dependency cycles.

Two defects were reproduced locally against real production classes with
synthetic inputs. Several additional issues are confirmed by source tracing;
their production frequency and cost are unmeasured. None is marked fixed.

## Findings

### A1 — P1: camera cache does not carry company/credential identity

**Status:** reproduced with synthetic provider responses.

`Server/Infrastructure/Integrations/Samsara/SamsaraTruckCameraProvider.cs:72`
uses `camera-latest:{vehicleId}` in shared `IMemoryCache`. A hit returns before
the second credential context reaches its provider. `RequestAsync` removes
the same global key. Company and credential revision are absent.

The probe creates two adapters with distinct credentials, one shared cache
and the same external vehicle ID. The first adapter fetches company A's fake
image; the second returns that image without making its own HTTP call:

```json
{"firstCalls":1,"secondCalls":0,"secondReceivedFirstImage":true}
```

This demonstrates the missing isolation invariant when external IDs overlap
or the same vehicle is accessible through different integration contexts.
It does not establish that two real providers issued overlapping IDs or that
a production image was disclosed.

The asynchronous retrieval path has a related boundary gap:
`RequestTruckCameraCommand.cs` caches `CameraRetrieval` without company
identity; `GetTruckCameraQuery.cs:24` checks the request/truck IDs but does
not independently verify the current company owns the truck. Opaque IDs and
provider authorization are not substitutes for the application boundary.

**Owner and closure:** Fleet camera contract and Samsara adapter. Scope cache
and request records to company and integration identity/revision; recheck
ownership on reads. Prove same-ID isolation, integration rotation, inactive
or reassigned truck rejection and no extra provider calls on valid hits.
Existing cached entries are short-lived; deployment/expiry handling must
discard entries under the old identity. Runtime auditor detection is not
an appropriate first defense for a read-side disclosure: boundary tests and
cache-key review cover this invariant.

### A2 — P1: exclusive integration channel claim is not atomic

**Status:** reproduced with real service/store and in-memory SQLite.

`IntegrationSettingsService.cs:108` checks `HeldElsewhereAsync`, then later
calls `TryWriteAsync`. `IntegrationCredentialStore.cs:49` checks only the
current company's provider revision. Persistence has a key on
`(CompanyId, Provider)` and no exclusive channel claim constraint.

The controlled interleaving lets both companies observe an unowned number,
commits A, then commits B using its earlier decision. Both report success:

```json
{"firstSaved":true,"secondSaved":true,"records":2}
```

SQLite writes do not overlap in this reproduction. The defect is the gap
between ownership observation and publication, not a database lock timeout.
This contradicts the service's explicit rule that a newly claimed number
cannot already belong to another company. It does not prove the cause of
the earlier production WhatsApp incident.

**Owner and closure:** Integration settings and its Infrastructure store.
Claim/release a canonical channel identity atomically with credential
publication, enforced across processes. A protected identifier or suitable
non-secret fingerprint and database uniqueness can avoid scanning/decrypting
every other tenant's settings. Do not rely only on a process-local lock.
Keep provider-specific conflict handling in Infrastructure. Tests must cover
concurrent first claims, changing/releasing claims, stale revisions and token
rotation on an existing claim. PostgreSQL constraint behavior still requires
a safe isolated fixture.

Before enabling uniqueness, inventory existing duplicate claims through an
authorized bounded diagnostic. Resolve ownership explicitly; preserve message
history and never silently migrate conversations. Add an Integration auditor
rule or document the operational detector with an owner and completion test.

### A3 — P2: planning work limit is applied after materialization

**Status:** confirmed source path; production cost unmeasured.

`TruckPlanningInputsReader.cs:80` loads every active/planned leg and every
unlinked legacy in-transit load, then groups, sorts and takes `limit` in
memory. `PlanningSummaryOperation.cs:42` invokes this discovery every
30 seconds, with `RunningWorkLimit=128`.

The output is bounded; database rows transferred and intermediate allocations
are not bounded by that limit. Many legs per truck make the distinction
important even with fewer than 128 trucks.

**Owner and closure:** planning input discovery, with persistence-specific
query work behind its existing boundary. Push distinct truck priority and
the final bound into the query while retaining active-before-planned ordering.
Measure foreground and background work separately. A large synthetic fixture
must assert equivalent selection and bounded returned/materialized rows.
Runtime business auditing does not detect this cost; query/row-count evidence
and background resource metrics are the appropriate checks.

### A4 — P2: cache invalidation crosses company boundaries

**Status:** confirmed source path; amplification not benchmarked.

`ReadCache.cs:105` reads a generation by group alone, although cached values
are correctly keyed by company. `Invalidate` and its publication queue also
use the group alone. A settings save in `PlanningSettingsService.cs:96`
invalidates the global Settings group. Both `TruckPlanningInputsReader` and
`PlanningSummaryReader.Signature` use that generation.

Thus a company A settings change invalidates B's settings-dependent reads
and changes B's planning signature. This is wasted work and potential display
churn, not evidence of cached data being returned to the wrong tenant.

**Owner and closure:** shared read cache and invalidation relay. Distinguish
explicitly global reference data from company-scoped groups, including relay
events and generation readers. Keep the existing late-result guards. Prove
that A's change reloads A but not B, and that an in-flight old A result cannot
be published after invalidation. Do not introduce a second per-page cache.

### A5 — P2: failed delivery persistence is reported as another active send

**Status:** confirmed catch path; non-conflict failure not reproduced here.

`DriverTextDelivery.cs:105` catches every `DbUpdateException` from recording
the send attempt and returns `InProgress`. Only the expected uniqueness race
justifies that answer. A different constraint/storage failure can therefore
look like a send already in progress, even though this attempt never saved.

**Owner and closure:** Messaging delivery and Infrastructure conflict
classification. Recognize the specific idempotency conflict; let unexpected
failures reach the operation boundary once. Test both duplicate admission and
an unrelated persistence failure, with zero provider sends for either failed
insert. Preserve the existing recorded-before-send and uncertain-result rules.
An auditor cannot find a row that was never inserted; boundary error evidence
and regression coverage are required.

### A6 — P2: messaging business policy leaks into Fleet/Routing

**Status:** confirmed coupling; changing channel is not implemented.

`Domain/Rules/Fleet/DriverWhatsApp.cs` is a pure recipient-selection rule,
not a Meta API adapter. Domain code may legitimately contain channel policy;
the word WhatsApp alone is not a layer violation. Its important invariant is
that an explicitly invalid WhatsApp number never silently falls back to a
different phone number.

However, `Driver.WhatsAppPhone`, `DriverRecipients`, and
`Routing/Services/FuelPlanning/FuelIssueChannel.cs:48` make the fuel delivery
path choose a WhatsApp recipient itself. The nominally generic
`IDriverTextDelivery.ReadinessAsync(phone)` also exposes a reply-window model.
Changing to another WhatsApp provider is mostly an adapter concern; changing
to in-app messaging or another channel is not isolated by that contract.

**Owner and closure:** Messaging owns channel selection, recipient resolution
and readiness. Fleet provides driver identity/contacts; Routing provides the
versioned fuel message intent. Keep existing history and explicit-contact
safety. Introduce only the capabilities needed for the next supported channel,
not a speculative universal messaging framework. Verify a fake second channel
can accept the same fuel intent without editing Routing's recipient policy.

### A7 — P2: core feature dependency cycles remain

**Status:** dependency baseline inspected; .NET architecture tests not rerun.

`Server.Tests/Architecture/ModuleDependencyTests.cs:17` records 20 existing
feature edges, including Dispatch ↔ Eta, Dispatch ↔ Routing,
Eta ↔ Routing, Execution ↔ Routing and Fleet ↔ Synchronization.
Dispatch, Eta, Execution, Routing, Fleet and Synchronization form one connected
cycle of responsibilities. The test prevents adding edges but does not remove
this existing cost of change. It inspects compiled signatures, not every call
inside method bodies.

**Owner and closure:** module owners in the maintained ownership guide.
Start from an actual change path: execution owns accepted work, routing owns
road preparation, ETA owns time forecasts, and shared display readers project
their results. Break a concrete back-reference through the owner's contract,
then remove its recorded edge. Do not weaken the test, merely relocate types,
or turn this into a microservice split. Transaction/history boundaries remain.

### A8 — P2: some presentation policy still has parallel implementations

**Status:** current unfinished UI work; not a final-change regression verdict.

`FleetFuelStations.razor.cs:151` selects the price by date and IFTA preference,
while `stationPrices.ts:16` independently performs the same selection for
the map. Their station-inclusion behavior also differs: the browser selector
requires active discount evidence and valid coordinates, while the list can
show an unavailable price. That difference may be intentional and needs an
explicit product contract rather than an accidental copy.

Repeated `Rows` getter work was observed during the review; a later read found
memoization already added by the active UI task. It is not counted as an open
defect here. Its correctness depends on replacing or versioning mutable inputs.

**Owner and closure:** one station display projection for chosen price,
currency/unit/date and availability; views format it and apply their explicit
inclusion rules. Do not copy financial formulas into a screen. Test that the
same station/date/settings yields the same chosen price in card, list and map,
including unavailable and expired values. Separately measure list construction
and map updates before claiming a performance improvement.

### A9 — P2: verification includes avoidable rebuild and brittle checks

**Status:** source inspection and one measured JS architecture run.

- `Client.csproj:21` runs `npm run js:build` before builds without an
  Inputs/Outputs incremental stamp; styles already use one. Cache reuse for
  .NET alone does not prevent this work. Actual time saved is unmeasured.
- `test.sh` includes architecture and JavaScript type checks in each invocation.
  Its existing multi-category mode should be used for one coherent boundary,
  rather than separate overlapping invocations on an unchanged candidate.
- Several style tests assert precise Razor markup/CSS layout structure.
  This can force test rewrites for approved cosmetic rearrangements. Preserve
  semantic/accessibility and interaction checks; review purely structural
  assertions rather than deleting their whole suites.
- `Client.Tests/Messaging/MessagingNoticesTests.cs:45` and related assertions
  use 20/50 ms real waits after fake-time changes. This is scheduler-dependent
  evidence for absence/coalescing. Controlled completion signals are preferable
  to increasing waits or repeating until green.

**Owner and closure:** build/test infrastructure and affected test owners.
An incremental JS stamp must include sources, package lock, compiler/build
configuration and deleted files, and fail when an expected output is missing.
Record build, database, browser and test execution durations separately.
Use one affected/dependent check group during iteration and one required full
gate on a stable shared-contract or release candidate. Do not drop mandatory
tenant, version, idempotency, persistence or architecture coverage.

### A10 — P2: fast messaging is still constrained by process-local events

**Status:** inspected design limitation, not a reproduced latency incident.

`MessagingSignals.cs:33` explicitly describes a mailbox on one API instance
and periodic repair for commits made on another. `RepairEvery` is 60 seconds;
the comment estimates visibility within about 90 seconds across instances.
`deploy-server.sh:60` currently caps the service at one instance, but this
review did not inspect the live deployment.

**Owner and closure:** Messaging change delivery. Before scaling horizontally,
provide company-scoped durable/shared change signaling with bounded cursors,
resync and reconnect behavior. Test a write on instance A and subscriber on B.
Keep the durable inbox as truth; notifications are hints. Do not promise
subsecond sender-to-browser latency from a single-instance local test or from
the long-poll timeout alone.

## Cohesion and positive boundaries

FleetMap spans roughly 3,280 C# lines over 13 partial files, plus Razor.
Messages has a roughly 1,253-line code-behind with inbox, conversation/history,
composer/attachments, templates, context, navigation and subscription state.
FuelPlanningService and RoutePlanningService also combine substantial lifecycles.
These are change-coupling signals, not line-limit failures. A future extraction
must transfer a real state/lifecycle owner, not just split methods into files.

Preserve the mechanisms already doing useful work:

- PlanningSummaryReader and batched planning inputs provide a shared display
  path; having multiple screens does not by itself mean ETA is recalculated
  multiple times. Trace cache misses, background refresh and dependency versions.
- Current duty-status work uses summary-only reads to avoid carrying route
  geometry into a status card. Final behavior and visual checks belong to its
  still-active implementation task.
- DriverScopeReader shares a request's driver-group choice across consumers.
- Messaging records a send attempt before contacting a provider, retains an
  uncertain outcome, and does not claim exactly-once external delivery.
- Expense/mileage mutations retain revision and attribution history; totals
  separate currencies. This is not financial or tax readiness certification.
- Shipment saves validate references and use receipts/revisions; Border drafts
  keep their own preparation state. This audit does not authorize real filings.
- File storage separates persistence, provider work and bounded reconciliation.
- Company-scoped query filters, write guards and session checks are useful
  foundations; they do not exempt caches and detached operations from isolation.

## Coverage and remaining verification

| Area | Reviewed here | Not established here |
| --- | --- | --- |
| Layers / composition | Guides, project boundaries, owners, dependency test source | Fresh full .NET architecture gate |
| Auth / users / company | Session and persistence boundaries, selected reads/writes | Penetration testing, every bulk/raw-SQL path |
| Integrations | Credential store, ownership, camera adapter; two probes | Live credentials, all providers, PostgreSQL races |
| Dispatch / Execution | Ownership and shared planning/read/write paths | Exhaustive transition replay on historical data |
| Routing / ETA / HOS | Summary ownership, discovery cost, versions, current read changes | Every algorithm, legal rules, live 11007 incident |
| Fuel | Delivery owner, display price selection, planning cohesion | Full optimizer proof, reachability and price replay |
| Messaging | Inbox/outbox boundary, mailboxes, context, UI state | Multi-instance load benchmark, real delivery timing |
| Fleet / groups / weather | Camera, shared driver scope, weather freshness/cache path | Every fleet workflow and provider edge case |
| Costs / mileage | Revision/history and bounded read patterns | Full financial policy audit, Driver Pay branch |
| Shipments / Border | Validation, scoped references, receipts, bounded inputs | External customs interoperability |
| Storage | Ownership, upload/reconciliation lifecycle | Provider failure matrix and production restore |
| Client / browser / styles | State owners, duplicated projection, structural checks | Full rendered desktop/mobile visual acceptance |
| Audit / operations | Coverage register, retention and gate structure | Live detector coverage, backup restore, deployment |

The runtime consistency register names nine implemented rules; it is not a
claim that every route, fuel, financial or tenant invariant has a detector.
Its dated deployment statements must not be used as current deployment proof.
For every remediation above, decide whether prevention, runtime detection or
both are needed, and include existing-data recovery where applicable.

## Checks and next completion boundaries

- `node scripts/source-inventory.mjs`: completed; static inventory only.
- `node --test Client/tests/architecture/*.test.js`: 65 passed, 0 failed,
  3,679.056375 ms. These include script/stub checks, not an actual release.
- `ArchitectureAuditProbe`: compiled and ran against the local production
  assemblies. Both isolation/claim findings reproduced as recorded above.
- Pinned CSharpier formatted the diagnostic source. No production source was
  reformatted by this audit.
- Full .NET suite, PostgreSQL checks, visual acceptance, live incident checks,
  load benchmarks and deployments: **not run by this audit**. No suitable
  isolated PostgreSQL fixture was established for these reproductions.

Implement A1/A2 first, with their invariant regressions and existing-state
assessment. Then A3/A4/A5 remove cost amplification and misleading outcomes.
Use channel extensibility and the next real feature change to address A6/A7;
do not add framework abstractions solely to reduce a count. Complete the active
UI task's price contract and visual evidence separately. Measure A9 before
publishing speedup percentages. A10 is a prerequisite to increasing instances
while retaining the low-latency objective.

The audit added the application-wide shared-ownership rule to `AGENTS.md`,
this report, a small evidence manifest and the reproducible diagnostic tool.
The listed application defects remain open. Implementation, tested behavior,
deployed behavior and repaired production state are separate milestones.
