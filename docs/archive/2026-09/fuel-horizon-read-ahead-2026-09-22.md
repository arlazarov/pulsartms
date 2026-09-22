# Reading the horizon's rows together — verification, 2026-09-22

The change: `FuelHorizon.BuildAsync` looped over the following loads and, on
each iteration, fetched that load's saved connection and its base route — one
round trip each. Production measured `connection` at 238.7 ms over two calls
and `base` at 272.4 ms over two, which is 511.1 ms of a 526.9 ms horizon; the
reads nested inside them - `read-captured-route` 236.9 ms and `base-route-read`
257.5 ms - came to 494.4 ms of that. The keys are all known before the loop
starts, so the rows are now read together, two queries instead of four.

**Not deployed.** This is the local verification only.

## What it does and does not change

The loop's conditions, its order, the freshness checks and the fallback
branches are untouched. The prefetch decides which loads to read for using the
loop's own conditions on data already in memory: a load the loop skips is not
read for, and collection stops where the loop would throw. The hash comparison
in `ReadBaseAsync` and `DeadheadConnection.Find` in the connection read happen
where they did, over the same rows.

**A rejected itinerary is not free.** The loads before the break are collected
and read, and the loop's later refusals - a missing connection, geometry that
does not join, an unanchored route - are decided after those reads, exactly as
before. Only what lies past the break costs nothing.

Moving the reads out of the loop took the round trip out of the `connection`
and `base` stages. Those stages still cover everything else the calls do -
reading the prefetched row, the freshness comparison, deserialising the
geometry, and the fallback query when a key was not covered. The two batched
queries are timed as `fuel-horizon/connection-rows-query` and
`fuel-horizon/base-rows-query`, so the round trips stay visible where they now
happen.

A dictionary entry exists for every key looked for, holding null when that row
does not exist, so a caller can tell "covered, absent" from "not covered"; only
the second falls back to its own query. The two reads are sequential — they
share one `DbContext`.

## The filter asks for pairs, and why the first attempt did not

The query is a disjunction of the pairs themselves, one
`DispatchId = a AND ExecutionLegId = la` per key.

The first attempt was a list of legs and a list of legless dispatches, argued
exact from two partial unique indexes that both tables do carry:

```
unique (ExecutionLegId) where ExecutionLegId is not null
unique (DispatchId)     where ExecutionLegId is null
```

Confirmed present in the deployed database:

```sh
psql "$PULSARTMS_READONLY_URL" -c "SELECT tablename, indexdef FROM pg_indexes
  WHERE tablename IN ('DispatchBaseRoutes','DispatchDeadheads');"
```

**That argument was wrong, and review caught it.** Uniqueness says a leg names
exactly one row. It does not say that row belongs to the dispatch the caller
asked about. Given the pair (A, legOfB) the leg list finds B's row, and
dropping it in memory afterwards is too late: its payload has already crossed
the wire. Asking for the pair means a row belonging to another dispatch never
leaves the database.

This is not a theoretical cost: `DispatchBaseRoutes` holds 27 rows across 19
dispatches — **8 dispatches carry two rows** — and `RouteJson` averages
**543 KB**.

### The identifiers are parameters, which they were not at first

A hand-built predicate decides how its values reach the database, and bare
`Expression.Constant` values are **inlined into the SQL text**. Reading the
generated statement showed exactly that:

```sql
WHERE ... (d."DispatchId" = '11111111-…' AND d."ExecutionLegId" = '22222222-…')
```

Every distinct set of loads would then have its own statement, missing both
EF's query cache and the server's plan cache — a cost paid on every horizon,
in exchange for nothing. The values are now held and read as fields, the shape
a captured local has, and the same query reads:

```sql
WHERE ... (d."DispatchId" = @Dispatch AND d."ExecutionLegId" = @Leg)
       OR (d."DispatchId" = @Dispatch2 AND d."ExecutionLegId" IS NULL)
```

`FuelHorizonPairSqlTests` holds this in place: it asserts no identifier appears
in the statement body, that a null leg still compares with `IS NULL`, and that
a different set of loads of the same size produces a byte-identical statement.

## Tests

`Server.Tests/Fuel/FuelHorizonReadAheadTests.cs` — six tests over
`KeysToReadAhead`, no database: every following load read once for its
connection; a load with nothing left to do not read at all; a single-stop load
needing a connection but no base route; nothing read past a load on another
truck; nothing read past a load whose stop belongs to another truck; nothing
read past the forty-stop limit.

`Server.Tests/Persistence/FuelHorizonPrefetchQueryTests.cs` — one test against
the real engine, on the recorded development fixture. It seeds a dispatch with
base routes on two different legs, a legless row on a second dispatch, and asks
for one pair; it asserts the pairing and, separately, **how many rows the query
returned**. It also asks for one dispatch paired with another dispatch's leg
and requires that nothing comes back at all.

`Server.Tests/Persistence/FuelHorizonPairSqlTests.cs` — reads the generated SQL
and asserts the identifiers arrive as parameters and the statement is reusable
across key sets. The dictionary alone could not show over-fetching, because it is
built from the keys either way, so `Pair` now counts the rows into
`fuel-horizon/base-rows-read` and `fuel-horizon/connection-rows-read` — useful
in production for the same reason.

### The row-count test had to be taken out of the parallel run

`PerformanceStages` totals are per process. The test passed alone and failed in
the full suite, because another test touching the horizon moved the counter
inside the window being differenced - the same trap that makes a single
production stage reading unattributable. It now sits in a
`DisableParallelization` collection, following `AllocationMeasurementCollection`,
which exists for the same reason. The full suite was run twice after the fix.

### The tests were checked against a broken filter

Loosening the base-route filter to the dispatch alone — the shape that drags in
the neighbour — fails the test with `Expected: 1, Actual: 2`. Putting back the
leg-list filter, the one the index argument was supposed to justify, fails the
foreign-pair case with `Expected: 0, Actual: 1` — one row of somebody else's
geometry. Both deliberate breaks were reverted; `git diff` on that file shows
no trace of either.

## Commands and results

```sh
dotnet build Server/Application/Application.csproj -warnaserror   # Build succeeded
dotnet dotnet-csharpier Server.Tests/ Server/Application/          # clean
dotnet test Server.Tests/Server.Tests.csproj -warnaserror   # run twice
#   Passed!  Failed: 0, Passed: 3212, Skipped: 0, Total: 3212
bash verify-release.sh
#   625 JavaScript, 1,054 Client, 3,212 Server; 297 assets, 11 graphs
```

3,204 before, eight added. No container database was started; the PostgreSQL
test uses the recorded development fixture and skips when none is recorded.

`FuelHorizon.cs` reached 515 lines and the 400-line rule failed the build, so
the reads moved to `FuelHorizon.Reads.cs`; the two files are 271 and 262 lines.

## What is not established

That production gets faster, and by how much. Two round trips are removed, but
the transfer and parsing of roughly a megabyte of route geometry remain, and
the size of a table on disk is not the size of the JSON a query carries. An
earlier estimate of −240 ms was withdrawn as unfounded. Measuring it needs
several identical previews before and after, compared on median and spread —
`fuel-edit/inputs` alone moved between 203.2 ms and 392.4 ms across two runs of
the same load, so a single reading will not settle it.
