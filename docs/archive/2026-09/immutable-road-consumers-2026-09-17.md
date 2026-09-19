# Immutable road consumers — September 17, 2026

This record covers the locally verified base-road, historical-connection and
financial-input slice. No working-database migration or deployment occurred.
It precedes the subsequent ETA consumer migration.

## Changes

Base preparation, selected-road reads, coordinate policy and hashes now consume
RouteWorkSnapshot/RouteWorkStop directly. Source entities are captured at the
entry boundary. The calculation does not rebuild a Dispatch. The same immutable
contract carries historical current/predecessor work across the persistence
interface and saved-fuel history replay. DeadheadConnection retains immutable
endpoints and work; caller edits require a new capture to affect its signature.
RouteWorkProjection.ToLoad was removed after its final production consumers
moved. Test fixture copies live only in Server.Tests support.

TruckPath and StopOperation retain one Domain implementation for manual truck
start, deleted anchors, conflicting assignments and personal-travel gaps.
StopCompletion shares the handoff/override/actual-evidence decision between
source editing and immutable reads. DispatchRates accepts only DispatchRateInputs
and validated connection values. Its server formulas and six-decimal rounding
are unchanged.

## Verification

- Full suite: 4,293 checks passed (2,717 server, 1,015 Client C#, 561 JavaScript).
  Evidence: `artifacts/managed/diagnostic-jUxeqM/tests.log`.
- Architecture checks require immutable road/connection/history contracts and
  explicit financial inputs; the deleted reverse projection cannot reappear.
- Added cases cover frozen manual path selection, deleted anchors, personal
  gaps, conflicting trucks, JSON round-trip of completion/handoff facts, and
  source/accepted base inputs during caller mutation.
- PostgreSQL probe built without warnings/errors and passed all transition,
  base/history, acceptance, transfer, queue, ownership and concurrency scenarios.
  Evidence: `artifacts/managed/diagnostic-Ovg7uP/postgresql.log` and `build.log`.
  Fixture `pulsr_core_fixture_df12caa03d304a7386b06d3922d64b14` was removed.
- Empty upgrade/downgrade and the populated clean transition retained protected
  identity/configuration and passed password/role checks after the fixture reset.

These checks do not establish production throughput. Live routing/fuel and
screen-model bridges are separate remaining work in the
[core specification](../../architecture/core-rebuild.md).
