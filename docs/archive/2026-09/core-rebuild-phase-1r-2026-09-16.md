# Core rebuild phase 1R: persisted fuel-road validity

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

Publication already rejected roads changed during calculation. After a valid
commit, however, saved fuel reuse and automatic refresh did not compare all
consumed road versions. A changed future base, fallback or connection could
leave fuel looking current until another input or quote triggered calculation.

New truck snapshots retain FuelRoadDependencies in their existing summary JSON.
The versioned list comes from the observations accepted by the publication
transaction, including the root, future base/fallback selection, connections and
any onward connection. Absent base selection and repeated observations remain
represented. No geometry is copied into that list, and no schema migration is
required. Infrastructure bounds and validates the persisted evidence alongside
the existing summary checks.

SavedRoadVersion separates road identity from progress. Publication still
compares both; durable reuse excludes visited/passed stops and deviation timers.
The shared ISavedRoadValidation implementation batches existing compact
Infrastructure readers inside an execution read snapshot. It does not query
route geometry, call providers or introduce provider-specific Application SQL.
Read-time validation does not take the global publication table lock.

TruckFuelPlans checks remaining dispatch blocks and the onward connection even
when its summary is cached. Completed earlier blocks no longer invalidate the
remaining fuel plan. Changed roads, missing legacy evidence or an unsupported
dependency version mark the projected fuel plan NeedsRefresh and clear schedule
impact and purchase-dependent stop arrivals. Stored choices are retained.
Existing display consumers withhold stale recommendations.

The automatic refresh cycle uses the same validation before comparing quotes.
A road change can trigger recalculation while prices are unchanged. Manual
purchases and manual starting fuel remain excluded from automatic replacement.
Failures retain the saved plan for retry under the existing result-revision
guard. Successful calculation replaces dependencies with the newly accepted
observations. No new worker or polling cadence was introduced.

Application owns reuse policy, Infrastructure owns compact persistence reads
and validation, and the Client receives the existing freshness contract. Test
composition reuses its road validator and shared fallback fixture. The future
trip-cost, toll, owner/operator and actual expense-import requirements remain
in the core plan. Unrelated working-copy changes were preserved.

## Verification

Added 33 server cases:

- Seventeen integration cases cover changes after commit to base, connection,
  root and native road ownership/version, including removal and cleared
  geometry, fallback replacement/new base selection, warm-cache detection,
  legacy retention, progress/fuel-only changes, successful recovery and
  cancellation before reads.
- Nine persistence cases cover compact summary round trips and rejection of
  malformed, foreign-scope, unbounded or incomplete root evidence without
  replacing the existing snapshot.
- Six automatic-refresh cases cover changed/missing/unsupported evidence,
  preservation of manual choices and starting fuel, and failed retry.
- One unit case verifies completed-root exclusion, onward inclusion and native
  scope rejection when advancing to the next load.

Existing strict publication tests still reject tracking changes during
calculation. Existing architecture checks passed without relaxed boundaries or
exceptions. The first affected run caught a new assertion against an internal
command property; assertions now use public behavior without changing
visibility. The second found collection identity comparison in a round-trip
assertion and a synthetic previous-execution snapshot whose road scope had not
been updated. Both fixtures/assertions were corrected. No failing check was
removed or skipped.

Final affected run, `bash test.sh fuel`:

- Server.Tests: 1,503 passed.
- Client.Tests: 228 passed.
- JavaScript architecture: 52 passed.

Final full run, `bash test.sh all`:

- Server.Tests: 2,523 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 4,096; no failures or skipped tests.

The stage changes 25 files, including 19 C# files. The pinned CSharpier
formatter,
whitespace checks, file inventory and relative documentation links were checked
separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-58NU8V/baseline.json`: pre-edit hashes for
  2,000 files.
- `artifacts/managed/diagnostic-DnXId3/affected-1.log`: compile feedback.
- `artifacts/managed/diagnostic-Z3NSwC/affected-2.log`: fixture/assertion
  feedback.
- `artifacts/managed/diagnostic-8oMG4g/affected-3.log`: affected suite passed.
- `artifacts/managed/diagnostic-gU9FO6/full-1.log`: full suite passed.
- `artifacts/managed/diagnostic-S1W3Xx/audit.json`: final inventory and checks.

## Limits and next slice

Historical corrections can invalidate connection selection before any road
metadata changes. That gap remains: the next slice must retain historical
selection evidence and replay each original native lookup batch. Road tokens
are not a substitute for that evidence, immutable accounting records or actual
fuel/toll transactions.

Supported road writers must advance their existing version/hash/timestamp when
changing geometry. Arbitrary geometry-only database edits that preserve every
metadata field are not detected. Complete writer ownership, standalone
historical/unassigned work capture and normalized visits remain later work.

Read-time metadata checks add database work. They retain existing summary and
geometry cache bounds but do not establish a continuously valid result across
separate requests. Production latency, memory, throughput and global publication
lock contention were not measured. Real PostgreSQL checks were not run because
no suitable isolated fixture was available. No SQL server was started or
installed; database tests used isolated in-memory SQLite.

Live provider and authenticated browser checks were not run. Client/Razor/public
HTTP contracts were unchanged, so no separate Client build was required;
solution tests still build their dependencies. No migration was introduced or
applied, and no deployment was made.
