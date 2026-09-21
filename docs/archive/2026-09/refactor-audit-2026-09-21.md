# Refactor audit — 2026-09-21

## Scope and conclusion

Reviewed HEAD `680f186` against restored baseline `9f35f5e`: 239 commits,
1,360 changed files, 95,794 insertions and 21,984 deletions. Generated migration
designers account for a substantial share of the added lines. These counts
measure change volume, not architectural quality.

The refactor improved maintainability and executable verification. It did not
complete multi-company isolation. The newly introduced company boundary is
stronger for ordinary EF reads than for shared memory, credentials and writes.
Do not onboard a second company until the P1 findings below are resolved.

Judgment, not a measured quality metric: approximately 7/10 overall, 8/10 for
automated engineering discipline, and 4/10 for readiness to host independent
carriers together. Single-carrier operation does not exercise most isolation
failures, but the expense defects also affect one carrier.

This was a broad source and test audit, with targeted runtime reproductions.
It was not a line-by-line proof of every changed file, a penetration test, a
live deployment inspection, a visual browser audit or a performance benchmark.

## Verification performed

`bash test.sh all` completed successfully with warning-as-error .NET builds:

- Server: 3,074 passed, zero skipped.
- Client C#: 1,052 passed, zero skipped.
- JavaScript: 623 passed, zero skipped.
- TypeScript strict checking passed.
- Total: 4,749 passing tests.

The configured PostgreSQL fixture tests ran without skips. The fixture selects
a recorded `pulsr_core_fixture*` database and uses a separate schema per run.
No local database server was started and the application database was not used
as a disposable fixture.

Important limit: `PostgresFixture.FillAsync` creates the current model with
`GenerateCreateScript`. Passing its queue and concurrency tests does not prove
that the entire historical migration chain upgrades a populated old database.
This audit did not independently rerun a migration or backup-restore rehearsal.

Additional disposable diagnostics used production classes and SQLite in memory.
Evidence is pinned in:

- `artifacts/managed/diagnostic-U7PHp2/result.txt`
- `artifacts/managed/diagnostic-Es1oTz/result.txt`

The second directory contains the console reproduction source. It exercises
the real expense handlers with a fake authorized caller and role service.
It does not claim an end-to-end HTTP exploit was executed.

## Confirmed findings

### P1 — Fleet snapshots cross the company boundary

`GetFleetLocationsHandler.Handle`, lines 36–46, returns
`ServerTelemetry.Current` or `FleetTelemetryCache.Latest` directly. Both are
singletons. `FleetTelemetryCache` also uses the constant `fleet-telemetry`
cache key. Neither snapshot has a company partition, and the handler does not
filter the returned in-memory trucks by the current company.

Reproduction: load company A's truck through the cache, then request company
B's response with a different loader. The B loader is not called and the
response contains A's truck. The synchronized branch has the same structural
problem through `ServerTelemetry`.

Impact: with two companies, authenticated fleet reads can return another
company's truck locations and associated driver/resource information.

Fix: partition snapshot publication, lookup and freshness by company. Review
`DriverHosSnapshot`, stream cursors and provider caches in the same pass;
filtering database queries alone cannot secure singleton snapshots.

### P1 — Integration credentials remain global and company-admin writable

`IntegrationCredentialSetting` has no company owner. Its primary key is
`Provider`. `IntegrationCredentialStore.TryWriteAsync`, lines 60–76, selects
and updates only by provider. `IntegrationSettingsController` requires Admin,
but has no separate platform-administrator boundary.

Consequently, an Admin from a second company would operate on the same saved
TorqueAI, Samsara or Google email credentials. Presence metadata is shared,
and changing the bundle changes the provider account used by other companies.
This follows directly from the storage and authorization path; no live
credentials were accessed or changed during the audit.

Fix: distinguish platform credentials from carrier connections. Carrier-owned
connections need company-scoped identity, revisions and provider caches.
Deployment fallback must be explicitly limited to its intended company.

### P1 — Expense writes accept cross-company resource and load references

`RecordExpenseHandler`, lines 68–71, copies supplied truck, driver, trailer
and execution-leg IDs without validating them against the current company.
`SetExpenseAttributionHandler`, lines 56–64, validates shares mathematically
but never checks that the target loads belong to the caller's company.

The EF relationships use resource IDs without company membership. There is
also no execution-leg foreign key in `ExpenseConfiguration`.

Runtime reproduction confirmed:

- RecordExpense succeeds with another company's truck and a nonexistent leg.
- SetExpenseAttribution succeeds with another company's load.
- The database accepts the resulting cross-company links.

This does not prove that foreign load details are returned, but it does prove
invalid financial attribution can be persisted. An expense may appear assigned
while the referenced load is invisible to its owner.

Fix: validate all optional references and every target load through scoped
queries before writing. Add suitable database relationship constraints where
practical, and tests for missing IDs and cross-company references.

### P2 — Write stamping is not a company write guard

`AppDbContext.Companies.cs`, lines 95–101, fills CompanyId only for new rows
whose owner is empty. It accepts explicitly foreign owners, does not validate
modified/deleted rows, and returns silently when no company is selected.

Runtime reproduction: an A-scoped context successfully inserts a B-owned
truck. This is a missing persistence invariant, not evidence that every API
allows clients to submit CompanyId. The expense handlers above are a separate
concrete path around relationship ownership.

Fix: reject inconsistent ownership in ordinary application writes. Explicit
bootstrap/migration operations should have a narrow privileged path. Audit
raw SQL separately because EF save guards and query filters do not cover it.

### P2 — Fuel price freshness uses another company's cached signature

`TruckFuelPlans.cs`, lines 157–160 and the later calendar key, omit company
identity. `FuelPlanMemory` is a singleton; its price cache accepts these keys
unchanged. The company-aware general ReadCache does not protect this cache.

Runtime reproduction: the same key loaded for A returns A's signature when B
requests it; B's loader does not run. Two companies with matching profile,
date and cache generation can therefore compare plans against the wrong
pricing signature for the cache lifetime.

Impact: false stale-plan warnings or incorrect freshness acceptance. This
probe does not establish that the optimizer charges another company's price.

Fix: partition both the ordinary and calendar signature keys by company and
the applicable pricing context. Add alternating-company tests.

### P2 — Attribution metadata corrections silently disappear

`SetExpenseAttributionHandler`, lines 74–75, skips an existing share when its
amount is unchanged. The comparison ignores Basis, Reason and ManualOverride.

Runtime reproduction: change a share from `manual` / `first` to `contract` /
`corrected reason`, retaining its amount. The handler returns success but the
saved share still says `manual` / `first`. The expense revision advances while
the intended metadata correction and its audit event are absent.

Fix: compare the complete editable attribution meaning before skipping a row.
Preserve the no-new-history behavior only for genuinely unchanged requests.

## What improved

- Fuel publication now centralizes its dependency checks in
  `BeginVerifiedPublicationAsync`. It refreshes telemetry before opening the
  transaction and uses `WithoutProviderWait` inside it. Road, profile and
  optimistic plan replacement checks remain in the publication sequence.
- Immutable work snapshots and shared rules are used across more planning
  paths. Several large orchestration files were split by responsibility.
- Browser sources are TypeScript with strict checking across `Scripts/**/*.ts`.
  The build has an explicit entry-point list and the Node tests still execute.
- UI components and styles have clearer ownership and shared theme tokens.
  Automated coverage increased; visual correctness was not independently
  checked.
- PostgreSQL-specific queue, lease and serializable tests now exist and ran.
- Request rate limiting, structured diagnostics and background liveness checks
  provide useful operational protection, though this audit did not validate
  the cloud deployment or its actual limits and health configuration.
- Costs now separate an expense from attribution, retain revisioned history,
  reject over-allocation and keep currencies separate in totals.

## Architecture work still incomplete

- FuelPlanning remains under Routing. `ICarrierFuelPrices` is a useful seam,
  but its implementation currently delegates to GetFuelStations, and the
  optimizer still consumes `station.Discounts`. The target separation of
  general station prices, carrier programs and pricing provenance is not
  completed simply by introducing that interface.
- Dispatch and DispatchStop still contain temporary NotMapped state and
  MemberwiseClone-based projections. Legacy route preparation and signature
  paths still call TruckItinerary. Native immutable paths are an improvement,
  but the entity/projection cleanup is partial.
- Company ownership tests classify tables and verify selected read/index
  behavior. They did not cover the singleton or write-reference cases above.
  More structural tests alone will not close these gaps.
- Domain now contains more reusable rules and calculation models. That is
  useful, but moving namespaces is not evidence of independent module ownership.
- Expense read totals are calculated after the 200-row cap in GetLoadCosts.
  The response exposes Truncated. Consumers must not present these as complete
  lifetime totals; complete totals should be aggregated independently if needed.
- Data Protection keys are persisted to the database without an explicit
  ProtectKeys configuration in the inspected DI. This is an existing security
  limitation, not a newly proven production compromise or a claim about cloud
  encryption settings.
- Module ownership documentation explicitly describes an older observation.
  Do not treat its counts or completed-plan claims as current verification.

## Recommended order

1. Close company snapshot, integration credential and expense-reference gaps.
   Add tests that alternate two companies in the same process and service
   provider, including requests and background work.
2. Enforce write ownership and fix the expense metadata correction bug.
3. Complete company partitioning of HOS, telemetry, fuel and provider caches.
   Define platform-wide versus carrier-owned configuration explicitly.
4. Rehearse upgrades on a populated isolated PostgreSQL fixture, including
   old/new-version overlap and recovery. Current-model creation is insufficient.
5. Continue FuelPricing/FuelPlanning and legacy projection cleanup in small
   changes. Avoid another broad structural rewrite before these invariants hold.
6. Measure representative map, ETA and fuel workloads. Passing tests or shorter
   files do not establish faster production behavior.

No application source was changed for this audit. No deployment was performed,
localhost was not started, and no application migration was applied. Only this
report, its archive link and disposable diagnostic evidence were created.
