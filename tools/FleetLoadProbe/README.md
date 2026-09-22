# Synthetic fleet load fixture

This opt-in local tool hosts the actual API controllers, authentication,
Application registrations, PostgreSQL persistence, planning queue, route,
fuel and ETA services. It never reads application User Secrets or production
configuration. Only the recorded `pulsr_core_fixture_` database and
`pulsr_test_runner` role are accepted. Every run has a separate schema and
connection pool. No database server is started in a container.

Synthetic inputs include 10, 50 or 100 trucks, drivers and trailers, two accepted
execution legs per truck, three stops per load, priced fuel stations, current
GPS/fuel readings and eight-day HOS histories. The first road spans roughly
4,000 km with geometry sampled every 50 m (roughly 80,000 input points).
Smooth bends at two scales prevent a straight-line-only simplification test.
Coordinates are fabricated;
the generated polylines are not navigable roads. Routing and HOS adapters are
synthetic. All server HttpClient factory transports reject external HTTP.
Browser Google Maps still loads its normal basemap.

The fixture applies existing migrations inside its isolated schema, including
planning revision and history triggers. No working database is migrated.
Provider network parsing, provider rate limits, synchronization imports, email,
camera and weather are outside this workload. Planning uses its real two-consumer
worker; the load driver explicitly triggers fuel and ETA through their existing
services. Those differences must accompany performance conclusions.

## Build and start

Run from the repository root with Docker Desktop available. The launcher uses
the existing remote test database, never a local database container. Runtime
containers publish only `127.0.0.1:5086`; do not publish the fixture remotely.

```sh
node scripts/artifacts.mjs run diagnostic -- dotnet publish \
  tools/FleetLoadProbe -c Release -r linux-x64 --self-contained false \
  --artifacts-path '{artifacts}/build' -o '{artifacts}/publish' \
  -p:UseAppHost=false

node scripts/artifacts.mjs run diagnostic -- python3 \
  tools/FleetLoadProbe/run.py start --publish /absolute/publish \
  --trucks 100 --memory 1g
```

Keep the build run pinned with `.keep` while its publish tree is mounted.
The launcher pins its own fixture manifest. The default is Linux x64 with one
CPU and no swap; on an ARM Mac this uses emulation. For a native ARM64 run,
publish with `-r linux-arm64` and start with `--platform linux/arm64`.
The publish architecture must match the selected container platform. Neither
local architecture establishes Cloud Run latency. Memory limits are test
settings, not production changes.
Seeding occurs in a separate process before the measured API starts.

The ordinary Debug Client at `http://localhost:5067` talks to this API. The
synthetic Admin is `load-test@example.invalid`; the five dispatcher accounts
are `load-test-1@example.invalid` through `load-test-5@example.invalid`.
Their disposable fixture password is `Synthetic-local-100!`. These accounts
exist only in the generated test schema. The normal authorization middleware
protects `/probe` controls and diagnostics; production contains no probe routes.

## Exercise and inspect

```sh
node scripts/artifacts.mjs run diagnostic -- python3 \
  tools/FleetLoadProbe/exercise.py --prepare --seconds 240 --idle 180

node scripts/artifacts.mjs run diagnostic -- python3 \
  tools/FleetLoadProbe/exercise.py --detour --seconds 240 --idle 180
```

Cold preparation requires actual saved fuel stops and populated ETA forecasts
for both loads. Five separately authenticated dispatchers poll GPS, switch
truck planning reads and page Dispatch. The driver updates GPS and enqueues
production route work, records queue state and waits for drain before idle.
Detours use the ordinary persistence threshold rather than bypassing the
rerouting policy. Authentication tokens remain in the driver process only.
Memory samples use the existing Admin diagnostic endpoint without forced GC.
Record Docker OOM/exit state separately if the process fails.

For request-scoped database measurements, an Admin request may send
`X-Probe-Measure` with `planning`, `board`, `refresh` or `fuel`. Read the last
32 completed measurements from `/probe/database`. The listener records EF
command counts/durations, connection-open and transaction-end durations,
query fingerprints and table names. It never retains SQL text, parameters,
credentials or payloads. Connection timings include pool acquisition; command
timings do not include all reader consumption/materialization. Duration sums
are not exclusive wall-clock attribution when operations overlap.

`POST /probe/refresh/{index}` invokes actual automatic planning for one truck,
without the background queue claim/completion wrapper. Use it to isolate
planning service reads; do not present it as a complete queue-job trace.
The Client loads Dispatch rows and ETA/financial enrichment separately. Test
those query flags as well as the API defaults; default `board` includes ETA.

Open the actual Client and interact with several trucks as a separate browser
check. HTTP load tests do not prove map responsiveness, correct selection,
rendered fuel stops or complete visual correctness. Report warmup, concurrency,
read errors, queue completion and idle behavior separately; do not treat an
empty plan or failed calculation as a cheap successful operation.

## Cleanup

`compare.py` compares a baseline and candidate publish on an already prepared
fixture. It restarts only the manifest's container, retains the schema, measures
the same planning reads and fuel/ETA calculation cold and warm, and leaves the
candidate running. Pass `--manifest`, `--baseline`, and `--candidate` paths
through the managed diagnostic runner. Run without concurrent builds or tests.
This focused comparison does not replace a full fleet capacity measurement.

For architecture comparisons, specify `--baseline-platform` and
`--candidate-platform` with matching publish directories. `--compact-gc` records
30-second idle memory, invokes the fixture-only Admin compaction endpoint and
records the resulting memory map. The command intentionally forces GC for
diagnosis; it is not an application cleanup policy. No such endpoint is added
to the production API. Compare idle and compacted samples separately and do
not equate allocation traffic, process RSS, cgroup usage and live heap size.

Stop the browser workload before stopping the fixture. Its saved manifest
identifies the one container and schema that cleanup may remove:

```sh
node scripts/artifacts.mjs run diagnostic -- python3 \
  tools/FleetLoadProbe/run.py stop --publish /absolute/publish \
  --manifest /absolute/fixture.json
```

This preserves other schemas, development data, production and Docker volumes.
Retain aggregate evidence under `docs/archive` and remove unused build pins
after the local server has stopped.
