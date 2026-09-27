# Test selection

Run from the repository root with `bash test.sh CATEGORY`. With no argument the
command runs all .NET and JavaScript automated tests. Pass multiple categories,
such as `bash test.sh fuel identity`, to run their union. Browser tests remain separate
because they require a running application and an authenticated session; follow
`Client/tests/browser/README.md`. No deployment or database changes are performed
by this runner.

## Verification stages

Choose checks by the changed invariant and its dependencies, not by the number
of edited files. Record the owner, risk and intended checks before starting.

1. While editing, run a specific regression or class using the narrow filters
   below. Reuse the existing build cache. A narrow run is feedback only.
2. When a coherent change is ready for review, run focused affected and
   dependent checks, including relevant architecture checks. Combine overlapping
   selections. If a category is expensive, select the relevant classes rather
   than automatically running the entire category. Build changed Client code
   and inspect the affected UI flow; do not repeat the full browser matrix.
3. Reserve the full suite and long browser/release checks for the final candidate
   being published, once before deployment. Shared contracts, persistence, DI,
   authentication and test infrastructure still require focused safety and
   dependency regressions during development. They do not independently trigger
   a full suite. The release gate already includes it; do not run a duplicate
   full suite immediately before the gate.
4. If proving a particular fix requires a long check before publication, explain
   the concrete need first and run only that necessary check. Record any deferred
   coverage honestly; short checks must not be reported as a full pass.

Pure documentation changes need link/content and diff review. Cosmetic text,
spacing and color changes need the applicable style/build and visual checks;
add a behavior regression only when behavior or an invariant changes. Financial
calculations, authorization, tenant separation, duplicate sends and stale-result
ownership require substantive regression coverage, including controlled
interleavings where relevant. Never weaken or skip these to meet a time target.

### Avoid duplicate work

- Do not rerun a passing check without changed inputs, a new failure or a
  concrete unresolved concern. Do not run full suites to keep an agent busy.
- Main and worktrees may run full gates in parallel with separate build outputs
  and isolated database fixtures. Never allow simultaneous conflicting writers
  to the same cache or fixture. Serialize only a shared resource conflict or
  measured resource contention; another running gate alone is not a blocker.
- Record the revision and relevant uncommitted diff with results. A later code
  change invalidates affected evidence; rerun affected checks and the full gate
  if its completion-boundary conditions apply. Do not label older results as
  verification of the final candidate.
- For a flaky test, state a hypothesis and use a bounded narrow reproduction.
  Prefer explicit request/completion signals and fake time to wall-clock waits.
  A longer timeout or a successful retry alone is not a demonstrated repair.
- Cancel obsolete jobs owned by the task after preserving useful diagnostics.
  Do not interrupt another task's gate without coordination.
- Record elapsed build/test time from existing output when available. Investigate
  slow phases before changing the runner; do not claim measured savings without
  comparable evidence. Report missing database/browser checks explicitly.

## Invariant contract review

For behavior changes, record a short contract before choosing tests: expected
result, authoritative owner, permitted dependencies, affected consumers and the
failure that must be rejected. Derive expected values from the product rule or
an independently worked example, not by invoking the implementation under test.
This applies across the application, not only to routing or ETA.

Use the relevant rows below. Mark non-applicable rows with a reason; identify
uncovered applicable cases explicitly. Do not generate a Cartesian test suite
or run expensive full gates during each iteration. Extend existing fixtures and
owner tests with small deterministic examples at the affected boundary.

| Change | Required evidence |
| --- | --- |
| Shared item reads or signatures | Alone versus batch, reordered peers and partitions give identical per-item values and signatures. Adding unrelated work does not change the item. |
| Dependencies and invalidation | Unrelated edits do not refresh work; a relevant assignment, policy or source revision does invalidate it. |
| Consumer projections | Affected screens/jobs agree on the same owner's facts for matching scope/version; intentional scope differences are explicit. |
| Repeated or cached work | Warm, cold and overlapping consumers preserve results; count provider/DB calls and materialization where the work occurs. |
| Stateful publication | Controlled interleavings reject stale results and preserve company/assignment/version boundaries, including after failure. |
| Queue/demand production | Identical observations do not continually create new versions or clear backoff; duplicate demands and old completions preserve newer work. |
| Queue scheduling | Under a stated bounded service-time/arrival model, eligible low-priority work makes progress within the declared bound; failed work respects backoff. |
| Existing invalid data | Seed the old state, exercise the owner recovery path and read the actual result; distinguish unresolved inputs from repaired state. |

Use explicit completion signals and fake time for queue/interleaving tests.
Exercise the real claim/complete persistence contract in an isolated supported
fixture when changing it. If unavailable, report the missing provider coverage;
an in-memory queue imitation does not prove the database scheduling contract.
Do not add production test traffic, unlimited retries or priority resets.

For a bug regression, retain the failing assertion on the old implementation
and the passing assertion on the fixed one. A test that merely mirrors a loop,
formula or snapshot cannot independently establish correctness. When an old
expectation contradicts the product contract, document the conflict and keep
its valid safeguards (for example, rejection after a predecessor changes).

Review evidence names the exact candidate, contract, representative cases,
consumer paths, checks run and coverage gaps. Differentiate result correctness,
work-count reduction and measured latency. Documentation of a required check
is not evidence that every existing module implements that check.

After publication, use the consistency-auditor guide's authorized bounded
verification through the normal owner. For example, a finished mileage request
must yield valid Total/RPM or an explicit missing-input reason before recovery
is reported complete. Retain a compact incident record, not repeated full dumps.

## Database test environment

Local disposable SQL databases are permitted, including Docker and
Testcontainers. PostgreSQL execution checks require isolated test credentials,
storage and connection settings; the working application and production
databases are not disposable fixtures. Bound resource use and clean up only
task-owned test resources. If no suitable fixture is available, report the
checks as not run. SQLite does not prove PostgreSQL behavior.

## Affected checks

| Changed area | Command | Included dependencies |
| --- | --- | --- |
| Map JavaScript, hover, labels, rendering | `bash test.sh map` | Fleet C# payload tests, architecture |
| SCSS, colors, spacing | `bash test.sh styles` | Architecture; also compile styles |
| Geocoding, address verification | `bash test.sh addresses` | Routing, finance, server architecture |
| Routes, deadheads, financial formulas, ETA | `bash test.sh routing` | Addresses, finance, ETA, server architecture |
| Fleet telemetry or map payload | `bash test.sh fleet` | ETA, map JS, both architecture suites |
| Dispatch | `bash test.sh dispatch` | Finance, routing, server architecture |
| Fuel | `bash test.sh fuel` | Routing, server architecture |
| Driver messaging, inbox, notices | `bash test.sh messaging` | Fuel (the shared webhook), messaging JS, server architecture |
| Expenses, load attribution | `bash test.sh costs` | Finance, architecture |
| Identity helpers | `bash test.sh identity` | Auth storage JS, both architecture suites |
| Synchronization | `bash test.sh synchronization` | Dispatch, addresses, server architecture |
| Shared contracts, persistence, DI, authentication, test infrastructure | Focused affected/dependent classes during development; full release gate before publication | Relevant owners and architecture; all assemblies/Node suites at release |

Every category runs the server and Client C# `Architecture` category and the
Client JavaScript architecture suite. The .NET command targets `pulsartms.slnx`, so
feature categories include matching tests from both assemblies. Category filters
select tests, not source projects to compile.

The runner isolates .NET build intermediates and outputs under `artifacts/tests`.
It must not overwrite a running Blazor development server's `bin/Debug` framework
manifest while that server is serving open tabs. Otherwise an ordinary test build
can cause boot or dynamically imported framework modules to return 404. Restart
the development server after deliberately rebuilding its own output. The release
gate uses a separate Release build and a unique publish directory.

`finance` and `eta` are aliases for the routing dependency group. Changes crossing
areas require the union of their checks. Client C#/Razor changes additionally need
`dotnet build Client -warnaserror -p:UseSharedCompilation=false`. Style changes
additionally need `npm run styles:build --prefix Client`; JS changes need
`npm run js:build --prefix Client`. Check relevant browser scenarios after visual
or interaction changes; automated tests are not a substitute for visual QA.

Before deployment run `bash verify-release.sh`, which includes all automated tests,
strict builds and staged artifact integrity checks; see `docs/operations/release.md`
for the opt-in authenticated browser gate. Report the categories actually run and
checks not performed. Do not infer
that unchanged files are unaffected when their dependencies changed.

## Test ownership and conventions

`Server.Tests/Server.Tests.csproj` references the server API and contains server unit,
integration and architecture tests. `Client.Tests/Client.Tests.csproj` references
the actual `Client` project and contains C# helper tests and bUnit component tests.
Both test projects are peers at the repository and solution root. Do not link production Client
source into the server test assembly or declare substitute partial components in
tests. `Client/tests` owns Node tests; `Client/tests/browser` owns browser scenarios.

Use feature folders and matching test namespaces. Put reusable fixtures and fake
transports in each test project's `Support` folder. Keep pure algorithm tests
separate from tests that create a database/service provider or render a component;
split a class when it mixes those responsibilities. Architecture checks belong in
`Architecture`, including layer boundaries and dependency registration checks.

Every xUnit test class declares a feature `Category`. Keep existing category names
stable so selection remains reliable. New or substantially changed classes also
declare an orthogonal `Kind`: `Unit`, `Integration`, `Component`, `Architecture`, or
`Allocation`. `Kind` describes the check, not the feature. Allocation checks remain
in their feature category and full suite; timing output is diagnostic and is not a
production performance claim.

Thread-allocation measurements use the non-parallel `Allocation measurements`
collection in `Support/AllocationMeasurementCollection.cs`. Concurrent
allocation pressure was reproduced contaminating the fuel matcher measurement;
only this sensitive fixture is isolated. Keep its measured loop and byte
limits intact. Isolation does not skip the feature/full-suite checks or
establish production memory usage under load.

For a narrow debugging run use `dotnet test Client.Tests -warnaserror --artifacts-path artifacts/tests --filter
'Category=Identity'` or `dotnet test Server.Tests -warnaserror --artifacts-path artifacts/tests --filter
'Category=Routing&Kind=Integration'`. These do not replace the runner's dependency
groups. `npm test --prefix Client` runs only JavaScript tests, not Client C# tests.
Do not rename, skip or remove tests to make a filter pass.

The bUnit suite renders production Login, Settings, DispatchList, FleetMap and
ArrivalEstimate components. It exercises pending submissions, settings conflicts,
view-specific polling, cancelled searches, truck focus, next-route error
recovery, delayed Next Loads on/off/on and A→B→A selection, current-load exclusion,
revision retention and ETA freshness. Next Loads regressions also hold planning,
details and next-route HTTP responses pending: enabled-before-selection can load
future routes independently when the current identity is cached, and A→B→A
restores complete geometry with the latest labels before any new HTTP response.
Cold selection must resolve the current identity without issuing a duplicate
next-route request. Cold-preview regressions hold live planning pending, exercise
the two-second timeout with a controlled clock, and reject late/cancelled replies
after selection, disposal, session reset or a newer same-key response. Unrelated
truck writes must not suppress a valid preview. Server tests verify provider-free
saved reads, authoritative unsaved-current identity, completion/input validation,
cache invalidation and nested shared-cache stripe collision recovery.
Unit tests bound snapshot memory/entry count, expiry and
truck/dispatch isolation. `FakeTimeProvider` advances polling/debounce/grace
deadlines without minute-long sleeps; production uses `TimeProvider.System`.
Wait on explicit request signals when work intentionally cannot render yet,
and use bUnit's retrying assertions for rendered results. Do not assume a short
serialization delay guarantees two publications overlap: hold an explicit fake
interop acknowledgement to test late-completion ownership deterministically.
Helper tests cover map
payload publication and authentication session races separately. Auth storage
Node tests exercise the real shared module with a simulated cross-tab Web Lock;
C# fakes verify its interop contract. These
tests do not prove browser layout, map provider behavior, real authentication
middleware, PostgreSQL migrations or live integrations. Run the relevant browser
checks before a release and report anything not exercised.
