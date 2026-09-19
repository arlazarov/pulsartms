# Test selection

Run from the repository root with `bash test.sh CATEGORY`. With no argument the
command runs all .NET and JavaScript automated tests. Pass multiple categories,
such as `bash test.sh fuel identity`, to run their union. Browser tests remain separate
because they require a running application and an authenticated session; follow
`Client/tests/browser/README.md`. No deployment or database changes are performed
by this runner.

## Database test environment

Docker is permitted for tooling and builds, but local SQL database servers must
not be deployed or started in Docker, through Testcontainers or through substitute
container runtimes, even for disposable tests. Real PostgreSQL execution checks
require a separate, isolated fixture that respects this restriction; application
and production databases are not test fixtures. If no suitable fixture is available,
report those checks as not run rather than starting a database container,
automatically installing a host database server or claiming SQLite proves
PostgreSQL behavior. Earlier reports of local disposable PostgreSQL containers
are historical results, not an approved workflow for future runs.

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
| Identity helpers | `bash test.sh identity` | Auth storage JS, both architecture suites |
| Synchronization | `bash test.sh synchronization` | Dispatch, addresses, server architecture |
| Shared contracts, persistence, DI, authentication, test infrastructure | `bash test.sh all` | Both .NET test assemblies and all Node suites |

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
