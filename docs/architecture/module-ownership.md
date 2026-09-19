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
prerequisite for separating request serving from background work.

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
- When the reviewed reset script is rebuilt for the current schema. It refuses
  to run at all today, because it requires the 39-migration pre-rebuild schema
  and the database has 46. Its operational list also omits eleven model tables,
  among them `ExecutionLegStops` and `ExecutionLegRevisions`, so past that
  guard it would leave accepted execution behind the loads it cleared. What it
  protects is correct and checked by digest; only the list is stale.
- Whether `FuelRoadsAreValidatedBeforeProfileAndResultWrites` is still earning
  its place. It asserts the order of literal strings by their position in one
  source file, so it fails when that sequence moves even though behaviour is
  unchanged, and it blocks giving fuel publication a contract of its own. The
  same guarantee is already covered by what the code does rather than how it
  reads: `LateRoadChangesKeepProfileAndBothFuelCopies` and
  `FailedFuelCalculationPreservesProfileAndBothSavedCopies` prove a rejected
  publication writes nothing, `ProfileSaveOnlyInvalidatesTheCacheAfterCommit`
  proves caches drop only after the commit, and
  `FuelSuccessCommitsTheRequestedProfileWithBothCopies` proves a successful one
  commits the profile and both copies together. Replacing it is a decision
  about the checks, not a licence to relax one.
