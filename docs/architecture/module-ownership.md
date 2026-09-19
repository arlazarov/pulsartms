# Module ownership and save boundaries

Status: an observed record of the code on 2026-09-19, not a target design and
not approval for any change. It exists so that an extraction is planned against
measured coupling instead of an impression of it. Every count below was read
from compiled signatures or from the source of the named files; where a method
cannot establish a fact, the entry says so.

Re-derive these numbers before relying on them. `ModuleDependencyTests` keeps
the dependency list honest; the other counts are not yet enforced by a check.

## Dependencies between feature modules

Twenty-one dependencies exist between the modules under
`Server/Application/Features`, measured from the compiled signatures of the
types the code declares: base types, interfaces, fields, properties,
parameters and returns. Compiler-generated types are excluded, because how
many locals an async state machine hoists into fields depends on the build
configuration. Seven pairs depend on each other in both directions.

| Pair | Direction that is the harder one to remove |
| --- | --- |
| Eta and Routing | Eta reads saved roads, profiles and deadhead history |
| Dispatch and Execution | Dispatch commands drive accepted execution |
| Dispatch and Routing | Dispatch commands queue route preparation |
| Routing and Execution | Routing reads the captured itinerary |
| Fleet and Synchronization | Fleet reads synchronization status |
| Routing and Synchronization | Shared synchronization options |
| Dispatch and Synchronization | Synchronization sends dispatch commands |
| Dispatch and Eta | Board enrichment |

`ModuleDependencyTests` records the twenty-one and fails on a twenty-second. It
also fails when a recorded dependency disappears without being removed from the
record, so the list can only shrink.

A cycle is what makes an extraction expensive: moving one of the two modules
requires a contract for the other direction in the same change.

## Who writes each table

`IAppDbContext` exposes fifty-five sets. Counting only explicit `Add`,
`Remove` and `Update` calls:

- Forty-eight have a single writing module.
- `Shipments` is written by Border and Shipments; `MovementAllocationEvents` by
  Execution and Mileage.
- `SynchronizationCheckpoints` and `RoutingApiCalls` are written only from
  Infrastructure.
- `SwitchParticipants`, `ExecutionLegStops` and `FuelTransactions` have no
  explicit writer. The first two are written through their aggregate.

This method cannot see a module that loads another module's entity and changes
a property, because that write has no call of its own. Treat the single-writer
result as a lower bound on ownership, not proof of it.

Reading is far wider than writing. `Dispatches` is read by nine modules,
`Trucks` and `Users` by eight, `Drivers` and `ExecutionLegs` by seven, while
thirty-four sets are read by one module only. Wide reading is not itself a
fault: what matters is whether the reader goes through a contract or
re-interprets the owner's rules, and status values are strings, so a reader can
re-interpret them silently.

## Where an operation begins and ends

Thirty-six files open a transaction: twenty-two at Serializable and fourteen at
the provider default. Nineteen further files require a transaction that is
already open and refuse to run without one, among them `ExecutionAcceptance`,
`InitialExecutionAssignment`, `RoutePlanStore` and `DispatchNumbers`.

The convention already in the code is therefore: one owner opens the
transaction and commits it, and participants join it. The convention has no
name and nothing enforces it.

Twenty of the thirty-six touch a table owned by another module inside their
transaction. An operation is consequently wider than a module: `CancelSwitch`
spans Execution, Dispatch and Mileage in one Serializable transaction,
`SetTruckAssignment` spans Dispatch and Execution, and `SyncDispatche` spans
Dispatch, Fleet and Execution. Loads, execution and movements form one
transactional core with three owners, so they cannot be separated by giving
each module its own context.

`LockExecutionLegAsync` is the shared concurrency token of that core. Dispatch,
Execution, Mileage, Routing and the ETA forecast store all take it, yet it is
declared on the shared `IAppDbContext` and has no owning module.

Concurrency is guarded in three layers: the process gates in
`Application/Concurrency`, the Serializable isolation level, and the leg lock.
Whether the first layer is required for correctness or only reduces
serialization retries cannot be decided from the source; it needs a real
PostgreSQL fixture.

## State that ties the application to one process

`Application/DependencyInjection.cs` registers twenty-seven singletons. These
hold shared state in process memory: `DriverHosSnapshot`, `FleetTelemetryCache`,
`FleetLocationStream`, `ReadCache`, `RouteDisplayCache`, `FuelPlanMemory`,
`EtaMemory`, `SamsaraHosHistoryCache` and `SamsaraDriverCatalogCache`, together
with the gates in `Application/Concurrency`.

Two consequences follow, and both are visible today. HOS is served from
`DriverHosSnapshot`, which a background operation in the same process fills, so
an instance started with `BackgroundOperations:Enabled=false` reports no HOS at
all. A second instance would keep its own copy of each of these and would poll
the providers again on its own.

`deploy-server.sh` pins `--max-instances 1`. That is a consequence of this
state, not a capacity decision, and moving the state out of process memory is a
prerequisite for separating request serving from background work. The cache
half of that is now done; see below for what the pin still waits on.

Two of these are no longer only in memory: the driver hours and the truck
positions are recorded as they are collected, so an instance that runs no
background work still answers with what another recorded, each bounded by the
age past which the reading means nothing.

The rest are caches, and a cache does not want persisting. `ReadCache`,
`FuelPlanMemory` and the provider caches all sit over data that is already
stored; what they lacked was a way to learn that another instance changed it.
`CacheGenerations` holds its versions in process memory, so an instance that
invalidated a group told nobody. What that needed was shared invalidation, not
shared caches - a different change from the one the hours and positions needed.

`CacheInvalidationRelay` is that change. Each instance publishes the groups it
dropped to `CacheInvalidations` and applies what the others published, so an
invalidation reaches every instance within one interval instead of never.
Three properties are worth knowing before relying on it:

- It is not instant. Between another instance's write and the next poll this
  one can still answer from cache. Bounded staleness replaces staleness that
  never corrected itself; it does not replace reading through.
- The log is read by time window, not by a cursor. A row inserted before
  another can become visible after it, and a cursor past that point would
  never see the late one.
- It ignores `BackgroundOperations:Enabled` and `:Roles` on purpose. Those
  decide which instance does which work, and an instance configured to serve
  reads and nothing else is exactly the one that must not hold caches nobody
  can clear.

`RouteDisplayCache` needed nothing: it already keys its entries on
`ReadCache.Generation`, so it follows the same invalidations.

`FuelPlanMemory` and `EtaMemory` key their entries on assignment revisions and
content hashes rather than on time, so a superseded entry is missed rather
than served, and they need no relay for that reason. What is still genuinely
per-process is `ProcessGates`, which keeps one instance from starting the same
work twice; with two instances that work can be started twice again. Whether
that only costs requests or can also collide on a write has not been checked,
and nothing here has been run on two instances. That, not the caches, is what
the pin now waits on.

## What actually makes a request slow here

Measured from the generated SQL during one board page load against the
managed database this runs on:

  208 queries, 13754ms in total, 66ms on average
  179 under 100ms, 23 between 100 and 200ms, 6 over 200ms

Almost none of that is the database working. A round trip to this database
costs about 66ms whatever the query is; the same statements against a local
PostgreSQL would be about a millisecond. What is slow is the *number of
sequential queries*, and that is invisible on a developer machine.

This changes what optimising means here. Rewriting a query saves a couple of
milliseconds of work and none of the latency. Removing a query saves the
whole sixty-six. The one change so far that moved a measurement -
ApplyExecutionAsync from 1436ms to 366ms - removed a round trip; it did not
make anything compute faster.

The counts say where the round trips are:

| Table | Queries in one page load |
| --- | --- |
| LoadExecutionLegs | 34 |
| Dispatches | 25 |
| CacheInvalidations | 22 |
| SwitchParticipants | 16 |
| Drivers | 16 |
| Trucks | 15 |
| Trailers | 15 |

`Trucks`, `Drivers`, `Trailers` and `SwitchParticipants` appearing about
fifteen times each is one shape: `ExecutionLoads.ReadAsync` runs roughly
fifteen times per page and each run asks separately for the names of the
trucks, drivers and trailers it just loaded. Those are small reference
tables read once per call rather than once per request.

`CacheInvalidations` is the relay polling, and it was set to two seconds
before this was known. At sixty-six milliseconds a poll that was a fifth of
one instance's database time spent on an exchange with nothing to carry.

## Where the board stands after the round trips came out

One page load, measured the same way as the 208-query baseline:

| | at the start | after |
| --- | --- | --- |
| deadhead read, whole | 1882ms | 567ms |
| forecast describe | 1971ms | 901ms |
| enrichment request, financials | 2347ms | 1005ms |
| enrichment request, forecast | 2433ms | 1446ms |

These are single runs against a network-bound database, so treat the margins
rather than the digits; the margins are large and consistent across both
requests.

An apparent 700ms was unaccounted for at one point - the board reporting
1482ms of financials while the deadhead read reported 789ms. That was a
misreading: the two lines came from different requests. Within one request
they agree, 1003ms of board of which 567ms is the deadhead and the rest
details and execution. There is no hidden cost there.

## The round trips that must stay

Counting queries per table says where the repeats are; it does not say which
repeats are waste. Each of the four largest was checked, and two of them are
load-bearing:

**Trucks, Drivers, Trailers, SwitchParticipants** were waste. They are tiny
tables read once per call instead of once per request, and `FleetNames` and
`ActiveTransfers` in `Application/Reference` fixed that.

**LoadExecutionLegs** must keep its per-call filter. It looks like the same
shape - thirty-six queries over twelve rows - but unlike the others it grows
with every executed load and never shrinks. Loading it whole would work
today and become a fault later, silently.

**Dispatches** is not a repeat within a request. Twenty-seven queries is
about three per request across the eight a page makes, each over ids that
request asked for. It also grows without bound.

**The two board enrichment calls** look like the clearest waste of all: each
re-runs the whole board query, so the page pays for the base computation
twice, about 470ms. They must stay two.

They run concurrently - the measured starts are 2119ms and 2124ms - in two
requests with two database contexts, so the forecast and the deadhead read
overlap. Combined into one call they would serialise, because one context
cannot run them at once:

  two calls  470 + max(1600, 1880) contended  ~2.4s
  one call   470 + 1600 + 1880 in sequence    ~3.9s

The duplicated 470ms buys 1.6 seconds of waiting. Removing the waste would
make the page slower, which is the opposite of what counting queries alone
suggests.

## Why opening hours are not yet a filter

A station that exists and trades can still be shut at the hour a driver
reaches it, and `FuelStationHours` now answers that question from the
provider's local periods. The planner does not ask it, and the reason is
structural rather than unfinished wiring.

`FuelOptimizer` chooses stops in miles and gallons. It has no clock:
`FuelArrivalPolicy` is about gallons on arrival, not time. `FuelStopArrival`,
the per-stop result, carries `Gallons` and `Percent` and no timestamp. The
only place a schedule exists is `FuelScheduleContext.Evaluate`, which
replays a whole finished route through hours of service to produce a
schedule impact - a property of a route, not an arrival time for one
station.

So there is nowhere to ask "is it open when he gets there", because nothing
computes when he gets there. Giving the planner that means either an arrival
estimate threaded through candidate selection, or a check after selection
that can reject a plan and re-run - and a rejection has to keep the rule the
owner was most explicit about: the driver always has fuel stops. A plan
refused for a closed station must be replaced, never emptied.

Closed businesses are different and are already excluded, because
`CLOSED_PERMANENTLY` and `CLOSED_TEMPORARILY` need no clock to decide.

A smaller step was considered and is not free either: telling the dispatcher
that a chosen station keeps limited hours, deciding nothing, so a person can
act on it. `FuelPlanStop` has a `Warning`, which looks like the place for it
and is not - that field carries the arrival reserve warning from
`FuelReservePolicy.ArrivalWarning`, and it is assigned rather than appended
in five places, so either message would silently replace the other. It needs
a field of its own and a path to the client, which is a change rather than
plumbing.

## What the forecast description cannot be

Describing a truck's chain is the largest single cost in a board request and
in opening one load: about 460ms inside `ExecutionWorkReader.ReadBatchAsync`,
reached through `EtaChainInputsService.DescribeTrucksAsync`. The board caches
its own index and pays nothing on later calls; this path is asked again with
different flags and pays in full, which is why the first click on a truck
takes roughly a second and the second does not.

Caching the description looks like the answer and is not. The description is
the change detector: `InputHash` is compared against what a stored forecast
was computed from, and a board read notices a moved appointment precisely
because it recomputes the description and finds a different hash. A stop time
changed directly in the database - which is how synchronisation writes
arrive - invalidates no cache group, so a cached description would answer
with the old hash and the board would keep showing a forecast for a schedule
that no longer exists.

`EtaChainInputTests.BoardReadInvalidatesTheRootWhenAFutureAppointmentChanges`
fails on exactly that, which is how this was found rather than shipped.
Splitting the reader into a display path that may cache and a publication
path that may not does not help: the display path needs the freshness for the
same reason.

What is left is to make the read itself cheaper, which is a question about
that query rather than about caching around it.

## Open questions

These need a decision before the affected work starts; nothing here is settled
by reading the code.

- Whether a commercial order, a freight shipment and an execution leg become
  three separate concepts, given that one trip may carry loads for several
  brokers.
- Which module owns the leg lock and the process gates once execution and
  commercial loads are separated.
- How an expense and its allocation to a load are recorded, before tolls,
  actual costs and owner-operator pay exist.
- Whether the two multi-writer tables keep two writers or gain one owner.
- Whether the two multi-writer tables keep two writers or gain one owner.

## Settled

- The reset script is rebuilt for the current schema, and both its migration
  count and the schema it acknowledges are now read from the model by
  `ResetInventoryTests` rather than restated by hand. Counting migration files
  had counted `RebuildExecutionStorage` twice, because it is written across
  two files, so the guard demanded one migration more than any current
  database has.
- `FuelRoadsAreValidatedBeforeProfileAndResultWrites` is removed. It asserted
  the order of six calls by their position in `FuelPlanningService.cs`, and
  three of those orderings were not properties of the program: the profile,
  route and truck-plan writes share one transaction, so which ran first is not
  observable. The two that mattered are now asked of behaviour, including a
  refusal between the writes, which nothing covered before.
- Fuel publication does not become a contract of its own. With the positional
  test gone it could, and examining what that contract would carry says it
  should not: the publication needs the planning state, the captured inputs,
  the plan, the profile, the saved roads, the deadhead history, the fuel
  result, the itinerary, the baseline route and the expected revision, plus a
  verification step that has to run inside the transaction. Two of those are
  aggregates of everything else. A parameter object that wide is evidence the
  seam is not there.

  It would also cross no module boundary. `ModuleDependencyTests` records one
  edge between these modules, `Routing -> Fuel`, in one direction; fuel
  planning already lives inside Routing and writes Routing's own tables. The
  contract would be an abstraction inside one module, which is the move that
  removes folder references without removing coupling.

  What blocked the move is gone, so the day fuel planning has a reason to
  leave Routing, it can. Until then the sequence keeps the owner it has.
