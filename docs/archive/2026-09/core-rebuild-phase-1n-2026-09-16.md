# Core rebuild phase 1N: automatic exchange-rate publication

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

Fuel's final effective-profile check already bypassed caches, but the automatic
rate writer did not participate in result publication. An observation could
change after validation and before the fuel result committed. Locking the whole
SynchronizationCheckpoints table would also affect unrelated background jobs.

FuelExchangeRateStore now requires the existing IPlanningPublicationScope.
SaveAsync enters its own fresh transaction, performs the leased state update
and commits before returning. The rate writer and planning result writers
therefore use the same exclusion protocol. The PostgreSQL table-lock inventory
remains twelve tables; no checkpoint table is added. Rate-state changes must
continue to use this store and scope. This does not protect arbitrary database
edits that bypass the owning writer.

A rate committed before result publication is visible to the uncached effective
profile check. It cannot change again until publication releases its scope.
This also covers the first usable observation after a missing rate. Changed
effective rates reject automatic and manual fuel publication before profile
or either fuel copy is replaced. An explicit fleet rate keeps priority; an
updated automatic observation does not change that effective profile.

Provider requests, lease acquisition and lease release remain outside the
protected state write. The existing refresh service invalidates its read cache
only after the store returns from commit. Busy entry, cancellation or failed
commit preserves the previous stored observation and cache generation. The
refresh still releases its lease and can retry. Reads do not acquire the write
scope, create placeholder rows or call the provider.

FleetSynchronizationOperation now handles a planning exception with an explicit
retry time as an expected deferral. It schedules the next attempt without
claiming success, incrementing failures or logging a job failure. Existing
success/error history remains until a successful run resets it. An already-past
retry hint uses RetrySeconds; other failures retain exponential backoff and
boundary logging. The successful hourly rate cadence remains unchanged.

No schema, migration, public HTTP field, financial formula, Client file or
additional worker changed. The existing rate lease still rejects stale or
expired owners. Unrelated working-copy changes were preserved.

## Verification

Added 14 server cases covering:

- Updated and initially missing rates at automatic/manual fuel publication,
  despite warm caches, preserving the profile and both previous fuel copies.
- Explicit fleet-rate precedence during automatic/manual fuel saves.
- Busy publication, cancellation and commit failure preserving the stored and
  cached rate, releasing the lease and allowing a subsequent refresh.
- Provider work before transaction entry and cache invalidation after commit.
- Exclusion between result publication and rate saves on independent SQLite
  connections, with rate publication succeeding after release.
- Rejection of an older caller-owned transaction without taking its ownership.
- Protected ordering in the production rate writer while excluding the shared
  checkpoint table from the source-lock inventory.
- Scheduled-job deferral with an explicit retry hint, while other work proceeds.

The existing malformed-state, lease-ownership, expiry and restart tests continue
through the protected production store. No architecture exception, disabled
test or weakened invariant was introduced. The first affected run identified
an xUnit assertion-style analyzer error; Assert.Single replaced a count check.
The affected suite was rerun successfully after that correction.

Affected `bash test.sh routing fuel synchronization` passed:

- Server.Tests: 2,180 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed.

Final `bash test.sh all` passed:

- Server.Tests: 2,403 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,976; no failures or skipped tests.

The stage changes 21 source, test and documentation files. Pinned CSharpier
validation covers all 12 changed C# files. Whitespace and local documentation
links are checked separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-LHWmz6/baseline.json`: pre-edit hashes for
  1,983 files.
- `artifacts/managed/diagnostic-fjZ2F7/changed.json`: initial C# inventory and
  formatter run.
- `artifacts/managed/diagnostic-YRN60F/affected-1.log`: analyzer feedback.
- `artifacts/managed/diagnostic-XsS0Qy/affected-2.log`: affected checks passed.
- `artifacts/managed/diagnostic-HFK2iq/full-1.log`: final full-suite run.
- `artifacts/managed/diagnostic-0DI43f/audit.json`: preliminary inventory and
  formatting/whitespace checks.
- `artifacts/managed/diagnostic-Xzsfc1/audit.json`: final inventory, formatter
  and whitespace checks, and local documentation links.

## Limits and next slice

The source-table publication lock remains broad and transitional. Rate saves
now briefly acquire that existing protection and can defer while source or
result writers are active. No production latency or throughput improvement is
claimed. A narrower replacement must preserve rate-writer participation along
with complete work/settings revision ownership.

The checkpoint table receives no new table lock, but unrelated-row concurrency
has not been executed on PostgreSQL. SQLite serializes more broadly and cannot
establish that production property. Real PostgreSQL execution and lock
contention were not tested because no suitable isolated fixture was available.
No local SQL server was started or installed.

This stage protects the stored observation during publication. It does not
freeze clock-based rate age checks or persist the complete chosen observation
as immutable accounting evidence. Existing amount signatures and dated-rate
validation remain unchanged.

Next: saved-road version ownership across calculation and result commit,
including independently prepared base roads and route-choice settings
validity. Fuel refresh after historical corrections also remains open. Durable
accounting records, settlements, normalized visits and removal of compatibility
paths remain later work.

Live provider and authenticated browser checks were not run. Client/Razor/HTTP
contracts were unchanged, so no separate Client build was required; the full
solution test runner still builds its dependencies. No migration was introduced
or applied, and no deployment was made.
