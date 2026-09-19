# Durable source-road preparation — September 17, 2026

## Implemented

SourceRoadRequests now owns missing-geometry demand, source input versions,
retry deadlines, successful cooldowns and worker leases. Map and Next Loads
reads persist explicit demand before returning. Input observation and browser
geometry identity remain separate so repeated polling cannot alternate hashes
and reset provider backoff. Priority promotion and initial observation preserve
an already pending attempt.

BaseRouteOperation captures source work, accepted sections, effective routing
profiles and predecessor history under the execution read scope. Page reads
batch source/history queries and reuse effective profiles per truck. Durable
identity is independent of cache generations and local connection counters.
RoutePreparationQueue now carries bounded local mutation hints only. Periodic
scans recover missed hints, and explicit demand survives outside the speculative
scan horizon. Hints are handed off individually to avoid stranding an untaken
batch when one transfer fails.

PostgreSQL workers claim due rows atomically with skip-locked selection. Each
completion requires its original unexpired lease and acknowledges only the
captured version. A changed input remains pending after older work finishes.
Shutdown leaves leased work for expiry recovery. Missing work completes without
provider calls. Completed demand is pruned after retention; pending demand is
not evicted by local memory limits.

The unapplied RebuildExecutionStorage migration includes this table and refuses
downgrade while it contains pending work. This is delivery state, not a new
calculation input or a reason to narrow publication locks.

## Verification

- Full suite: 2,694 Server, 1,014 Client C# and 561 JavaScript tests: 4,269 passed.
  Evidence: `artifacts/managed/diagnostic-JYNrt0/tests.log`.
- Strict migration-probe build: zero warnings/errors.
  Evidence: `artifacts/managed/diagnostic-O2Vt5h/build.log`.
- Isolated PostgreSQL transition and source-road concurrency probe passed.
  Evidence: `artifacts/managed/diagnostic-29Ux2p/postgresql.log`.
  The probe removed its isolated fixture after verification.

Regression checks cover replacement contexts/workers, future explicit demand,
source changes while leased, stale-owner rejection, durable retry/cooldown,
priority promotion, cleanup, deleted work and stable single/batched signatures.
PostgreSQL checks additionally exercise simultaneous demand and nonblocking
claims through independent connections, plus the pending-work downgrade guard.

## Boundaries

The conservative fifteen-table publication guard remains. Full transfer writer
unification, resource proposal review, remaining compatibility projections and
the per-truck writer revision contract still belong to the complete rebuild.
No deployment or working-database migration was performed. Browser workflows and
production throughput/latency were not measured in this slice.
