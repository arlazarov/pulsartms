# fuel-edit/inputs on the execution-backed path — 2026-09-22

`acb3f72` measured the fuel preview on the SQLite legacy fixture and fixed a
waste that only the legacy branch had. Production load 11006 is
execution-backed, so that result did not explain it. This measures the path
production actually takes, against PostgreSQL.

## Fixture

`Server.Tests/Support/PostgresFixture.cs` — the recorded development fixture,
one schema per run, skipped when none is recorded. No container or host
database was started and no production data was copied. The chain is synthetic:
one truck, a trip, three dispatches of two stops each, three execution legs
(one active, two planned) and their `LoadExecutionLegs` links. The test asserts
the chain really is execution-backed — every segment carries an
`ExecutionLegId` — before it counts anything.

`QueryColumnProbe` now records statement text and, separately, transaction
starts, commits and isolation level; boundaries do not reach a command
interceptor, so they are counted through `IDbTransactionInterceptor` instead.

## What one ReadFreshAsync costs

`IFuelWorkInputsReader.ReadFreshAsync` → `TruckItineraryReader` inside
`IExecutionReadScope`, which opens one **repeatable-read** transaction.
Measured over five warmed repetitions, the count is identical every time:

| | statements | transactions |
|---|---|---|
| execution-backed, before | **7** | 1 begin, 1 commit, repeatable-read |
| execution-backed, after | **6** | unchanged |
| legacy chain (SQLite, `acb3f72`) | 3 | 1 begin, 1 commit |

No statement is sent twice within a read, before or after.

The six are: the fleet with its driver and trailer joins (11 columns); the
source dispatch query (57); the native legs with their stops (160); a dispatch
read by id (28); the load-to-leg links (12); and switch operations by leg (14).

## The read that was removed

The seventh was the widest of them — a dispatch with `Include(Stops)` and its
source link, 162 columns — and on a fully execution-backed chain it ran with an
**empty** id set. Work carrying a leg is read natively; `legacyIds` is what is
left, and here there is nothing left. EF still sends the statement, so it was a
round trip that could only return nothing.

`TruckItineraryReader` now skips it when the set is empty. Its neighbours on
the same path already do this — `WorkSequenceReader` returns early on
`native.Length == 0`, `GetTruckExecutionLoads` on `links.Count == 0` — so this
restores the pattern rather than inventing one. A mixed chain still reads its
legacy half, and a test holds that.

Reverting the guard fails `AFreshReadIsOneSnapshotAndDoesNotAskForLegacyWork`.

## Considered and not changed

The fleet query reads every truck even when one is requested. It is not waste:
`knownIds` and the unit-number map are built from the whole fleet and decide
which dispatches match, including loads that name another truck by number.
Narrowing it would change the selection, not just its cost.

## Commands and results

```sh
dotnet test Server.Tests/Server.Tests.csproj \
  --filter "FullyQualifiedName~FuelInputsExecutionPathTests"
#   2 passed, on the recorded PostgreSQL fixture
bash verify-release.sh
#   625 JavaScript, 1,054 Client, 3,219 Server; 297 assets, 11 graphs
```

## Limits, and what this does not explain

**The cause of the production spike is not established.** `fuel-edit/inputs`
measured 219.1, 966.8 and 218.0 ms across three previews of 11006. A local
count of statements does not establish what that reading was, in either
direction: it neither explains it nor rules out the removed statement as part
of it. Nothing here should be read as predicting the server.

Counted, not timed: statement counts and transaction boundaries are stable and
provider-independent in the sense that matters here. Wall-clock on a local
fixture is not a server, and none is quoted.

## The stages, and what each one covers

Added where nothing named the work, reusing the four stages the reader already
had:

```
fuel-edit/inputs                     the caller's view of the whole read
  execution-scope/open               connection acquisition AND the BEGIN
  itinerary-read/read-total          the reader, inside the snapshot
    itinerary-read/work-batch        fleet, source dispatches, native legs
    itinerary-read/legacy            the leftover dispatches, skipped when none
    itinerary-read/evidence          links and switch operations
    itinerary-read/assemble          building the snapshot from the rows
    (remainder)                      read-total minus the four above
  execution-scope/commit             the COMMIT
```

`execution-scope` is shared by twelve callers, so read its rows with their
counts; `fuel-edit/inputs` bounds the fuel one.

**`open` is connection acquisition and the BEGIN together.** It does not
separate a pool or lock wait from the statement, and must not be quoted as
either. Nothing is recorded on the path that joins an outer transaction, so a
count there is a snapshot this scope actually opened.

Still not separated by measurement: pool and lock waiting inside `open`,
network time against SQL duration, and materialisation against assembly within
`assemble`.

## The one protocol to run on the server, after separate approval

Repeat a single preview of 11006 several times, and for each take two `stages`
snapshots around it. From the difference, per preview:

1. `fuel-edit/inputs` — the reading being explained.
2. `execution-scope/open` and `/commit` with their counts — whether the time is
   before the first statement or after the last.
3. `itinerary-read/read-total` minus the sum of `work-batch`, `legacy`,
   `evidence` and `assemble` — whether it is inside a named read or in the
   remainder.

Read `open` as acquisition-plus-BEGIN, never as a pool wait on its own. If the
time lands there, separating it needs `pg_stat_activity` sampling during the
preview, which is a further step and is not authorised by this note either.
