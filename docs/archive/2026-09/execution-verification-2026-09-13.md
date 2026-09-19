# Switch and automatic mileage verification

Date: 2026-09-13 (America/Toronto).

This follows the earlier [source checkpoint](execution-checkpoint.md). The user
authorized test execution. This record is not a deployment or operational
assignment repair; the [execution guide][execution] defines the current scope.

[execution]: ../../architecture/dispatch-execution-and-settlements.md

## Implementation follow-up

- Switch workspace, preview and planning are exposed in Load Details. Release
  and Receive retain independent actual times, resource checks and retry keys.
- Cancellation before actual work restores the outgoing itinerary and retains
  history. It supersedes automatic planned mileage without deleting actuals.
- Exact-source ordinary actuals can fill missing facts. Future ordinary address
  and appointment changes require a separate preview and explicit acceptance.
  Changed identities, cargo operations and existing actuals are not guessed.
- Transactional planning notifications survive restart. Route saves enqueue
  planned mileage; an independent OBD feed records confirmed physical intervals.
  Unknown boundaries remain pending or become explicit gaps, not invented miles.
- Pending odometer maintenance continues during provider backoff. The odometer
  cursor and lease are independent of GPS synchronization.
- A manual physical interval excludes only intersecting raw OBD pairs. Disjoint
  measured pairs before and after it remain eligible, without double counting.
- GPS feed checkpoint changes now occur only after cancellable publication
  preparation, avoiding a checkpoint advancing without its corresponding data.
- Route cache copies preserve execution-leg identity and assignment revision.
  Preview ownership checks reuse existing reads; the regression bounds remain
  seven database commands for the cold fixture and one for a repeated read.

Pay is not implemented. Observed mileage is partial coverage, not a complete
trip reconstruction. General native stop editing and historical corrections,
full future cross-leg ETA/fuel continuity and native trip creation remain
outside this release boundary.

## Executed checks

Final `bash test.sh all` result:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Server xUnit | 1819 | 0 | 0 |
| Client xUnit / bUnit | 887 | 0 | 0 |
| JavaScript / styles / architecture | 529 | 0 | 0 |

The runner includes both .NET architecture categories and all Node suites.
Final local output: `artifacts/tests/execution-full-final.log` (retained build
artifact, not a committed operating instruction).

Additional successful checks:

- Separate Client build with warnings as errors, shared compilation disabled
  and one MSBuild worker: zero warnings and zero errors; 14 JS assets built.
- `npm run js:check --prefix Client`.
- `npm run styles:build --prefix Client`.
- `npm run format:check --prefix Client`.
- `dotnet csharpier --check Server Client Server.Tests Client.Tests` using the
  pinned formatter and the project's 80-column target.
- Offline Npgsql model/snapshot and migration SQL generation checks inside the
  server suite, including manual-origin backfill and guarded downgrade.

Initial failures exposed an EF projection translation issue, a legacy ETA
upsert missing its assignment revision, lost cache identity, extra preview
queries and the GPS checkpoint race. These were corrected without loosening
the query-count bounds or adding architecture exceptions. Test fixtures were
also updated for unique fleet IDs, nonbreaking display units, compact date
markup and cancellation ownership. The header-link test found a double-encoded
ampersand in its accessible label; the production label and regression now
agree.

## Not executed or changed

- No real PostgreSQL fixture was available. SQLite and offline Npgsql checks
  do not prove PostgreSQL execution, transaction races or migration application.
- No SQL server or database container was started. Application and production
  databases were not used as test fixtures.
- No authenticated browser, physical-phone, live map, Samsara, Google Weather
  or TorqueAI verification was performed in this test run.
- No production latency, memory or request-volume improvement was measured.
  Fixture query bounds are not production performance measurements.
- Neither `20260914022232_AddExecutionAndMileage` nor
  `20260914031300_FinishSwitchAndAutomaticMileage` was applied.
- No deployment, production configuration change, assignment rewrite or actual
  Drop/Hook confirmation was performed, including trucks 11005 and 54777.

Deployment remains a separate operation requiring the release gate, a safe
PostgreSQL validation plan and draining old readers/writers before the
non-rolling-compatible schema transition.
