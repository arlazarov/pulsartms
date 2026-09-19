# SaaS evolution plan

Status: proposed implementation roadmap; no phase is implemented or approved for
deployment by this document. TorqueAI remains the active dispatch source. PulsR
continues to support daily AMF operations throughout the transition.

This is a maintained forward-looking plan, not a description of current SaaS
capabilities. Record implementation and release evidence separately under
`docs/archive`, and update this plan as decisions are made.

The proposed execution-leg, coordinated switch and contract-based compensation
model is defined in [dispatch execution and settlements](
dispatch-execution-and-settlements.md). Its operational storage migration must
precede payroll and must not be replaced by a transient active-route slice.

## Objective and boundaries

Prepare PulsR for multiple carrier companies, independently scalable background
work, and eventual independence from TorqueAI without replacing the working TMS
workflow now.

- Keep TorqueAI synchronization enabled. Do not introduce native load creation,
  transfer source ownership, or disconnect Torque as part of this refactor.
- Reuse existing Application business logic and Infrastructure adapters. Do not
  rewrite fuel/ETA formulas or change routing policy during a host migration.
- Keep one repository and PostgreSQL initially. A database-provider move is not a
  prerequisite and must not be bundled with these changes.
- Start with a modular application and separate execution hosts, not a service
  for every entity or provider. Independent microservices remain a later option.
- Preserve local IDs, stop visits, manual confirmations, operation overrides,
  selected routes, prices and saved fuel plans, subject to their existing validity
  rules. Invalid or stale results must not become current merely to avoid errors.
- Preserve existing responsive UI, SCSS ownership and shared components. Tenant
  context and explicit freshness/pending states are the only initially expected
  presentation changes; no general redesign is included.
- Planning does not authorize infrastructure purchases, live provider experiments,
  production migrations, deployments, credential changes or destructive cleanup.

Follow [architecture](../ARCHITECTURE.md), [test selection](../testing.md),
[release operations](../operations/release.md), [diagnostics](../operations/diagnostics.md)
and [UI controls](../ui-controls.md). Existing unrelated working-tree changes must
be preserved and kept distinguishable from this migration.

## Target responsibilities

| Boundary | Owns | Initial execution |
| --- | --- | --- |
| TMS core | Companies, memberships, loads, visits, assignments, actuals and manual business commands | API host |
| Integrations | Provider connections, ingestion schedules, cursors, normalization and recovery | Existing host first, then Integration Worker |
| Planning | Saved roads, route options, ETA, fuel calculations, input validation and durable results | Existing host first, then Planning Worker |
| Documents and notifications | Scoped files, import notifications, explicit camera/document requests | Feature modules with durable background work where needed |
| SaaS operations | Activation, limits, usage attribution, support access and operational status | API/control module initially |

The API serves ordinary reads from saved data and bounded caches. Integration
workers collect external facts. Planning workers consume versioned inputs and
publish results. Browser navigation is not responsible for keeping ingestion alive.

Application continues to own commands, policies and operation interfaces;
Infrastructure owns adapters, SQL and storage implementations. Each host is a
composition root; Worker must not reference API. A worker writing shared tables
through the same Application module is not yet an independently owned microservice.
Later extraction requires explicit data ownership and versioned commands/events,
not direct cross-service writes into another module's tables.

## Delivery rules for every phase

Each phase contains several small releases, not one large commit or migration.
For each release record its scope, schema compatibility, input/output contract
versions, verification evidence, image digest, role configuration and minimum safe
rollback revision.

1. Add compatibility support before switching readers or writers.
2. Exercise fixtures and failure recovery before production activation.
3. Switch one responsibility at a time; keep other behavior unchanged.
4. Observe normal operations against thresholds established in phase 0.
5. Remove compatibility paths only in a later, separately reviewed release.

Use additive schema changes, bounded resumable backfills and validated constraints.
Review migration locks, index-build strategy and restart behavior on isolated
PostgreSQL. Do not promise uninterrupted operation: document any required pause,
its observed duration and the recovery procedure before approval.

## Phase 0 — Baseline, recovery and acceptance fixtures

**Deliverables**

- Inventory actual API/worker roles, deployed images, applied migrations, provider
  connections, schedules and persistent data. Reconcile documentation, option
  defaults and deployed overrides without logging secrets.
- Record a known rollback artifact and configuration. Verify backup recovery into
  a safe isolated database; a backup existing is not proof it can be restored.
- Arrange an approved isolated PostgreSQL fixture. Do not use application or
  production databases, SQL containers, Testcontainers, or an automatically
  installed host database. Provisioning requires separate authorization.
- Create sanitized fixtures for repeated visits, partial completion, driver-only
  travel, bobtail/empty legs, next-load assignments, selected route options, fuel
  arrival values, provider outages and concurrent manual edits.
- Measure API p50/p95, payload bytes, provider wait and request counts, database
  time, calculation CPU/memory, queue age and source-data age separately. Define
  accepted freshness, recovery, error and cost budgets before any cutover.
- Define load-test scenarios using active trucks, companies, concurrent map users,
  history windows and peak calculations. Do not equate registered users with load.

**Exit gate:** reproducible baseline, named recovery procedure, acceptance
scenarios and an isolated PostgreSQL test path. Analysis and pure-code preparation
can continue without the fixture; persistence/ownership cutovers cannot pass their
database gate without it. No capacity or speed improvement is assumed in advance.

## Phase 1 — Tenant foundation while AMF is the only company

**1A. Define and expand ownership**

- Classify data as platform-shared, tenant-owned or integration-connection-owned.
  Station geography and permitted reference data may be shared; negotiated prices,
  fuel purchases, drivers, routes, documents and commercial information are private.
- Introduce a company identity, user membership with company-scoped roles, and
  provider connections. A user may belong to multiple companies. Platform support
  privileges must be separate, explicit and audited.
- Create the AMF company and map existing users, integrations and records to it.
  Preserve current approved access; do not grant platform privileges implicitly.
- Add compatible ownership columns, then backfill in bounded batches. Derive child
  ownership from authoritative parents. Stop on orphaned or conflicting records.
  A temporary missing-company-to-AMF default, if necessary for old writers, is an
  AMF-only bridge with an explicit removal gate, never permanent behavior.

**1B. Make every execution path tenant-aware**

- Resolve company context from authenticated membership, not a trusted request
  field. Fail closed on missing or unauthorized context. Background jobs require
  explicit trusted tenant/connection context without an HTTP principal.
- Scope queries, updates, natural keys and foreign keys to prevent cross-company
  associations. Equal truck/load numbers must be possible in different companies.
- Scope private caches, telemetry snapshots, signatures, queues, cursors, leases,
  deduplication, storage paths and result identities. Do not reuse old unscoped
  entries. Drain, migrate to AMF, or safely recreate old in-flight work.
- Migrate credentials with format/version compatibility and connection-specific
  protection. Preserve the persistent key ring; verify settings edits during
  transition. Legacy credential fallback must be explicit and AMF-only.
- Scope provider-specific mailbox, label, topic and validation configuration to
  the intended connection. Resolve incoming notifications to that connection
  through validated configuration, not an untrusted tenant ID in the payload.
- Inventory raw SQL and bypass paths before choosing PostgreSQL row-level security
  as defense in depth. Runtime roles must not silently bypass the selected policy;
  pooled connection context must not leak between companies. Filters alone are
  not sufficient evidence of isolation.
- Establish minimum compatible runtime/schema versions. End old unscoped readers
  and writers before enforcing the final ownership model. Validate backfill,
  tenant-aware foreign keys and non-null ownership; add scoped uniqueness before
  removing global uniqueness. Disable creation/activation of a second company.

**Exit gate:** AMF behavior is unchanged; two-company isolation fixtures pass for
API, jobs, credentials, caches and files. No second customer is activated yet.
Tenant-aware authentication changes require full regression checks; a separate
session-storage/security-hardening release must not be hidden in this migration.

## Phase 2 — Provider boundary and safe concurrent imports

**Deliverables**

- Keep TorqueAI as the source of existing imported business facts. Preserve
  imported actuals versus manual overrides and their current precedence.
- Add explicit source/connection mappings without replacing local IDs. Human load
  numbers are not global identity. Use external IDs where actually supplied;
  retain documented sequence matching where Torque lacks a stable visit ID.
  Do not invent a provider identity or merge same-address visits.
- Make ownership/reconciliation policy explicit for each imported field group.
  Import creation and updates use TMS Application rules; UI and planning continue
  consuming provider-independent models.
- Protect import/manual-write concurrency in persistence. Recheck a durable
  aggregate/source revision or use a suitable database transaction/locking boundary,
  including parent and related-stop conditions. A process semaphore is not enough.
- Route scheduled sync, manual sync and notification-triggered imports through
  consistent tenant-aware ownership. Fence stale owners at business-data commit,
  not only at checkpoint save. Replayed imports must remain idempotent.
- Introduce explicit host-role registration while keeping the existing host the
  active owner. Split shared persistence/adapters from HTTP infrastructure and
  background operation registration; preserve architecture boundaries.

**Exit gate:** repeated, reordered and concurrent fixture imports cannot erase
manual work or attach another company's records. Existing sole-owner Torque
operation continues. No native load editor or new source-of-truth mode is enabled.

## Phase 3 — Durable snapshots, demand and change notification

Introduce these capabilities within the existing process before moving hosts.

**3A. Data reads**

- Persist latest telemetry and necessary recent points, HOS clocks, relevant
  history and driver settings. Distinguish measurement time, successful provider
  observation time and local ingestion time. Reading a checkpoint must not refresh
  those timestamps. Preserve separate GPS/fuel/engine freshness.
- Commit feed progress with the corresponding saved data. Apply newer-only
  observation rules and retain last-known values without presenting stale HOS as
  current calculation input. Define bounded retention and cleanup at creation.
- Initially publish the old and new read projections from the same normal fetch;
  this is not permission for a second poller or second business-data writer.
- Move ordinary API/planning reads onto Application snapshot-reader interfaces;
  a cache miss queues refresh or reports the established unavailable state rather
  than silently falling back to a live provider. Preserve existing display grace.

**3B. Work and invalidation**

- Persist coalesced requests for imports, route preparation, ETA, history and other
  demand-driven work. Include tenant, connection where relevant, object identity,
  input/schema version, deduplication identity, retry deadline, bounded attempts,
  lease/fencing state, expiry and terminal failure handling.
- Commit business changes and their change-journal/outbox entries atomically.
  Publishing after a database save without recovery is not sufficient.
- Each API instance independently observes shared revisions and invalidates its
  own cache. A competing-consumer queue acknowledgement cannot invalidate every
  replica. Keep bounded TTL/repair scans as recovery, not immediate consistency.
- Retain existing durable ETA/route/fuel stores; add shared history results and
  explicit camera request mappings if those operations cross processes. Do not
  collect camera captures automatically.
- Preserve demand semantics: ordinary movement updates progress; valid prefix
  completion reuses geometry; route-relevant input changes or sustained deviation
  can require new roads. HOS/price refresh alone must not rebuild road geometry.

**Exit gate:** independent service providers/processes can read each other's
committed snapshots and results. Crash-before/after-commit, duplicate delivery,
lease expiry, restart, superseded inputs and lost acknowledgement tests pass.
The durable consumer is not enabled in production before isolated PostgreSQL
migration, claiming and fencing checks pass. Exactly-once execution is not assumed.

## Phase 4 — Extract Integration Worker

**Preparation**

- Add a separate host/artifact using existing Application operations. Separate
  ingestion scheduling from the planning loop currently bundled with fleet sync.
- Define explicit roles per pipeline. `Synchronization.Enabled` must not double
  as a general host-role switch or enable provider reads in the API accidentally.
- Use scoped service identities and minimum required database/provider permissions.
  Keep one controlled schema-migration owner. Worker must not inherit HTTP session
  startup behavior or impersonate an administrator. Preserve credential/key-ring
  compatibility without assuming this alone provides cryptographic separation.
- Keep existing Cloud Run hosting unless a separately approved decision changes
  it. Select appropriate continuous/background execution and record cost/freshness
  implications; do not copy HTTP scaling settings blindly.
- Preserve explicit Admin Gmail watch registration and the compatible OAuth
  credential bundle. A deployment must not activate an unregistered mailbox.
  Validate push identity, audience, service account and mailbox before persisting
  import demand; successful acknowledgement follows durable acceptance. Preserve
  idempotent import/recovery and coordinate any external watch-renewal owner.
  Follow [Gmail watch operations](../operations/gmail-watch.md).

**Cutover, one pipeline at a time**

1. Verify compatible API snapshot reads, fresh saved data, worker status and a
   rollback revision that understands the new roles and contracts.
2. Stop new claims by the old ingestion owner; drain or cancel bounded work and
   verify release/expiry and commit fencing.
3. Enable the new Integration Worker as the sole owner for that scope. Manual
   sync and push notifications enter the same durable coordinator.
4. Observe the next normal successful cycles: assignments, dispatch counts,
   protected manual values, cursor progress, request volume, queue age and freshness.
5. Leave planning ownership unchanged until phase 5.

Rollback stops/drains the new owner before restoring the compatible previous
owner. Retain additive storage and queued work. Never reset cursors, delete jobs,
restore old credentials or run down migrations as a routine rollback.

**Exit gate:** API restarts do not stop ingestion; Worker restarts do not erase
displayed state; recovery meets the measured budget without duplicate imports.
Torque stays connected and active.

## Phase 5 — Extract Planning Worker and optimize measured bottlenecks

- Move route/ETA/fuel work only after durable demand and shared results work across
  processes. Preserve selected alternatives, manual vias and current validation.
- Capture input revisions for each calculation. A slower result from old inputs
  must not replace a newer manual choice, completion or calculation.
- Apply tenant-aware fair scheduling, coalescing and separate resource/provider
  budgets. One customer's bulk calculation must not monopolize other customers'
  work. Shared provider credentials also need an aggregate limit across workers.
- Keep TomTom/geocoding routing work owned by planning where appropriate; external
  HTTP calls do not all belong to one generic ingestion service.
- Transfer ownership with the same stop-claims/drain/fence/start sequence. Verify
  API requests eventually see results after API/worker recreation.
- Optimize large route-option display payloads, cache reuse and candidate work in
  separate measured releases. Display simplification must not reduce the exact
  geometry required for matching or change financial/safety rules incidentally.

**Exit gate:** accepted peak scenarios meet agreed response, queue, freshness,
resource and cost budgets. Rolling back the worker retains valid demand/results
and uses a compatible owner. No unmeasured throughput target is declared achieved.

## Phase 6 — Controlled SaaS activation

- Verify every deployed API, worker, scheduled trigger and replay path is
  tenant-aware. Old unscoped revisions must not remain routable or able to restart
  against the shared database. Enforce a minimum runtime/schema compatibility gate.
- Complete two-company tests with identical truck/load numbers, hostile object-ID
  access, simultaneous imports, retries, cache reuse, file downloads, company
  switching and platform-versus-company administrator access.
- Review session security, key access, audit retention, backup/restore, company
  export/deletion and incident procedures. Record unresolved launch blockers.
- Establish activation/deactivation, connection setup, per-company limits and
  usage attribution. These do not require automatic paid billing in this phase.
- Start with one explicitly approved pilot company and monitor isolation, costs
  and responsiveness. Do not enable signups as a migration side effect.

**Exit gate:** explicit approval to activate another company after security,
recovery and capacity evidence. Dedicated deployment/database pilots are a
separately operated alternative, not evidence that shared-database isolation works.

## Deferred — Native TMS and selective microservices

This work requires a separate product decision and is not a prerequisite for
continuing AMF operations or finishing Integration Worker extraction.

- Add native load creation/editing through the same TMS rules, initially in an
  explicitly selected scope. Native and imported loads can coexist with clear
  provenance and one authoritative writer per fact.
- Transfer ownership per defined company/module/load set only after reconciliation.
  Disable Torque writes for transferred facts before native edits become
  authoritative. Resuming import after native edits requires reconciliation, not
  blind overwrite. Torque remains enabled for all untransferred scopes.
- Split a module into an independently deployed microservice only when measured
  scaling, reliability or development needs justify its operational cost. Define
  owned storage, contract versions, replay and failure behavior first.
- Do not create a service per carrier, database per entity, or mandatory Kafka/
  Redis/Kubernetes infrastructure without a measured requirement and approval.

## Verification and shadow-mode requirements

Use the existing runners and keep architecture checks intact:

| Change | Required checks |
| --- | --- |
| Tenant ownership, authentication, persistence, shared contracts or DI | `bash test.sh all` |
| Feature-only iteration | Relevant union of `identity`, `synchronization`, `fleet`, `dispatch`, `routing`, `fuel`; dependencies follow the test guide |
| Client C#/Razor/contracts | Client build in addition to affected/full suites |
| Styles or JavaScript | Their documented builds, categories and relevant visual scenarios |
| Every deployment | `bash verify-release.sh` plus phase-specific acceptance evidence |
| PostgreSQL migration/claims/fencing/RLS | Real isolated PostgreSQL fixture; SQLite is not a substitute |

Client build: `dotnet build Client -warnaserror -p:UseSharedCompilation=false`.
All xUnit classes require feature `Category`; new/substantially changed classes
also require the appropriate `Kind`. Keep fixtures in the owning test project's
Support directory. Never relax architecture tests to enable a host split.

Shadow comparisons use sanitized fixtures or the same already-produced normalized
snapshot. Compose them with read-only stores and disabled/stubbed providers; do
not start normal hosted services accidentally. They must not fetch extra provider
data, advance cursors, acknowledge jobs, send messages or publish business results.
Test that unexpected writes/outbound calls fail. Missing input means comparison
unavailable, not permission to obtain it from a live provider.

Offline UI smoke is provider-free. Authenticated browser checks are separately
opt-in and may call live providers. Report every check not run; do not substitute
real daily operations for automated concurrency and isolation coverage.

## Rollback boundaries and stop conditions

- Before a second company has data, an AMF-only rollback is possible only if schema,
  credentials, queues and host ownership are compatible with that exact revision.
  Even here, a pre-role-control binary may start duplicate workers and is unsafe.
- After another company has any rows, credentials, files or queued work, rollback
  to pre-tenant code is prohibited. Disabling that company does not remove its
  data. Use a tenant-aware previous revision or a forward fix.
- Restoring a pre-activation backup is destructive recovery, not normal rollback.
  It requires explicit authorization and reconciliation of all later user writes,
  provider progress and external side effects. Never claim zero loss.
- Stop activation on cross-tenant access, protected-field changes, unstable IDs,
  missing committed work, stale-owner writes, unexpected provider duplication or
  failure to meet agreed freshness/recovery budgets. Preserve evidence and use the
  documented compatible rollback; do not mask failures with fabricated values.

For every completed slice, archive: exact scope, migrations/applied status, test
commands and results, PostgreSQL/browser limitations, release identities, relevant
metrics, observed handoff/recovery and rollback eligibility. Log stable operation
and trace identifiers without credentials, bodies, raw provider payloads or profile
data. Keep high-cardinality tenant attribution in access-controlled usage/audit
records rather than unbounded metric labels.

## First implementation slice

Start with phase 0 and an ownership inventory for phase 1. The first code slice
should prepare the single-AMF tenant model and compatibility tests, not move
workers, enable another company or change fuel/routing behavior. Review its
additive migration and rollback contract before implementation/deployment.
