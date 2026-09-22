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
#   625 JavaScript, 1,054 Client, 3,218 Server; 297 assets, 11 graphs
```

## Limits, and what this does not explain

**The production spike is still unexplained.** `fuel-edit/inputs` measured
219.1, 966.8 and 218.0 ms across three previews of 11006. One statement was
removed from a read that issues six; a local count cannot establish what the
966.8 ms was. Nothing here should be read as predicting it.

Counted, not timed: statement counts and transaction boundaries are stable and
provider-independent in the sense that matters here. Wall-clock on a local
fixture is not a server, and none is quoted.

Not separated by measurement: connection acquisition, pool or lock waits,
network time against SQL duration, and materialisation against assembly. The
existing stages divide the read into `work-batch`, `legacy`, `evidence` and
`assemble` only.

**What to measure on the server, after separate approval:** two `stages`
snapshots around a single preview of 11006, reading `itinerary-read/*` beside
`fuel-edit/inputs`, taken several times so a 966 ms reading can be seen as
recurring or as a one-off; and `pg_stat_activity` sampling during it to
separate waiting from executing. Neither is authorised by this note.
