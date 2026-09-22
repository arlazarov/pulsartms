# Native ARM64 50-truck load test

Follow-up [CPU measurement](fleet-cpu-2026-09-21.md) found low average CPU
utilization during queue processing. Queue delay alone does not demonstrate
CPU exhaustion.

## Scope

A fresh isolated PostgreSQL schema hosted 50 synthetic trucks and 100 loads.
The real API and two-consumer planning worker ran natively on Linux ARM64 on
an ARM Mac, with one CPU, 1 GiB memory, no swap and workstation GC. The build
includes the fuel allocation optimization. No forced GC occurred in this run.
No production configuration, database or deployment was changed.

Each first route spans approximately 3,830 km with roughly 75,000 input
vertices. Routing and HOS providers are synthetic; actual migrations,
persistence, authentication, planning, fuel and ETA services are exercised.
No database server was started locally or in Docker. External provider
parsing and ordinary import/background workloads are excluded.

## Results

All 50 route and fuel plans and 100 ETA forecasts were prepared successfully.
Five authenticated dispatchers then polled for three minutes while telemetry
enqueued all-truck updates every 30 seconds. All 317 measured requests passed.
The queue drained to zero with zero reported retries. Docker reported no OOM
termination. The process remained available locally.

| Phase | Observed peak MiB | Sampled duration |
| --- | ---: | ---: |
| Baseline | 108.0 | Initial sample |
| Cold preparation, concurrency 2 | 640.6 | 518 seconds |
| Five dispatchers and telemetry | 663.3 | 179 seconds |
| Queue drain | 582.8 | 126 seconds |
| Idle without forced GC | 539.8 | 116 seconds |

Final container usage was 512.8 MiB. Samples every approximately three seconds
can miss shorter peaks. The original x64 emulation run peaked at 925.7 MiB
and ended near 777 MiB: this run reduced observed peak usage by approximately
28% and final usage by approximately 34%. Both architecture and the fuel
optimizer changed, so this is not an isolated measurement of either factor.

| Operation | Count | Median ms | Sample p95 ms |
| --- | ---: | ---: | ---: |
| Prepare routes, fuel and ETA | 50 | 18,507 | 27,319 |
| Truck planning read | 85 | 1,798 | 2,581 |
| Dispatch board read | 85 | 3,590 | 5,387 |
| Fleet locations read | 85 | 5 | 129 |

Cold preparation allocated approximately 28.15 GiB cumulatively, compared
with 172.65 GiB in the original run. Steady activity still allocated 14.00 GiB
and queue drain 5.56 GiB. Allocation traffic is not simultaneous RAM usage.
The final managed estimate was approximately 204 MiB, last-GC fragmentation
138 MiB and GC committed memory 354 MiB. These quantities overlap and must
not be added. This short run does not establish long-term leak freedom.

Memory is improved, but one CPU still cannot finish an all-truck refresh
before the next 30-second update. Work coalesces and drains after demand
stops. Planning reads and Dispatch latency also remain material. A 1 GiB
limit accommodated this specific workload; this does not establish complete
production capacity or justify a 512 MiB limit.

## Browser verification and limitations

The actual Client at localhost:5067 switched between TEST-001 and TEST-050.
Routes and mileage rendered. TEST-050 displayed its populated ETA and two
saved fuel stops in the editor; the editor was closed without saving changes.
TEST-001 displayed no ETA after its early forecast expired during the long
preparation. The fixture explicitly calculates ETA during preparation and
does not run the regular ETA refresh worker. Saved forecast counts therefore
do not prove that all 50 forecasts remain fresh throughout the test.

This is a synthetic capacity experiment, not a real-road navigation test or
a production latency guarantee. Browser interaction covered selected trucks,
not all 50. No 100-truck run or detour scenario was performed.

## Evidence

Paths are relative to `artifacts/managed`:

- `diagnostic-fVLuFT`: memory samples, requests and queue state.
- `diagnostic-cUla3E`: isolated fixture manifest.
- `diagnostic-UoSJzl`: native publish mounted by the running fixture.
- `diagnostic-i5OuC1`: post-run automated checks.

The launcher now accepts `--platform linux/arm64`; its default remains x64.
Publish output must match the selected platform. Python syntax checks passed.
The post-run full suite passed all 4,854 tests: 3,177 server, 1,052 Client C#
and 625 JavaScript. JavaScript type checks and `git diff --check` passed.
Builds and tests started only after memory sampling finished.
