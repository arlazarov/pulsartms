# Bounding the corridor search, measured in production — 2026-09-22

The change: `FuelSearchGeometry.Match` gained `maximumAwayMiles`, defaulting to
infinity, and the corridor filter passes the two miles it already judged by.
Commit `041f4f0`. Deployed as revision
`amftms-api-b-44df5471-14f6-46bf-a3f5-6df7d382c926`, at 1 GiB with the GC
settings unchanged, so this measures the change and not the environment.

Both readings are single previews of load 11006, taken the same way: two
`stages` snapshots bracketed by four `memory` reads, one `build-total`, the
process confirmed unchanged across the window.

| | before | after |
|---|---|---|
| `fuel-regions/corridor` | 4,370.6 ms | **61.5 ms** |
| `fuel-edit/arrival-regions` | 4,372.8 ms | **94.8 ms** |
| `EditFuelPlanCommand` | 5,180.5 ms | **1,267.7 ms** |
| `fuel-edit/core-total` | 5,256.7 ms | 1,190.1 ms |

696 priced stations both times, 10 kept in the corridor, 3 shortlisted. No
TomTom calls in either interval. The saved fuel plan was not modified.

## What the numbers do and do not say

**The counts matched; the answers were not compared here.** Production shows the
same number of stations examined, kept and shortlisted. That the bounded and
unbounded searches select the *same* stations was established by
`tools/FuelCorridorProbe`, which fails its run when the two forms disagree, and
by `Server.Tests/Routing/BoundedRoadMatchTests.cs`. Equal counts on their own
would not have shown it.

**The local factor did not transfer, and predicting from it would have been
wrong.** Locally the bounded search was about 600× faster than the unbounded
one. Production improved the stage by 71×. The gap between bench and server is
itself uneven: 7× on the unbounded path, 61× on the bounded one. The bench
establishes direction and rules out a regression in the answer; it does not
size the win.

**One measurement each, with background traffic.** Neither reading is an average
under sustained load, and the counters are process-wide.

## Where the time is now

From the same interval, stages that moved:

| stage | ms |
|---|---|
| `fuel-edit/core-total` | 1,190.1 |
| `fuel-edit/horizon` | **610.5** |
| `fuel-edit/inputs` | 203.2 |
| `fuel-edit/saved-fuel` | 164.2 |
| `fuel-edit/arrival-regions` | 94.8 |
| `fuel-regions/build-total` | 89.8 |

About 78 ms of the handler sits outside `EditCoreAsync` — the pipeline, the
gates and the response.

`itinerary-read/work-batch` also moved: four calls, 498.7 ms, 124.7 ms each.
`FuelHorizon.BuildAsync` does call into that reader through
`ReadConnectionAsync`, `ReadBaseAsync` and `CaptureRouteAsync`, so some of it
may be inside the 610.5 ms horizon — but the counters are per process and those
four calls could equally have come from another operation in the window.
Attributing them needs stages inside the horizon, which is the next step, not
an inference from this table.

## Memory in the same window

| | before | after |
|---|---|---|
| working set | 479.4 MiB | 489.1 MiB |
| heap after last GC | 114.9 MiB | 145.4 MiB |
| fragmented after last GC | 31.7 MiB | 52.1 MiB |
| committed after last GC | 182.4 MiB | 188.1 MiB |
| cgroup usage | 440.3 MiB | 456.5 MiB (of 1,024) |
| allocated in the window | — | +78.3 MiB |

Collections: gen0 +2, gen1 +2, gen2 +2.

**Accounted cache occupancy totals about 6.4 MiB** — `reads` 5.0 of 16 MiB,
`route-indexes` 0.6 of 32, `route-display` 0.3 of 16, `fuel` 0.4 of 8,
`planning-summaries` 0.1 of 8, and `hos-history` and `truck-history` at zero.

This removes the grounds for the earlier suggestion to cut the cache budgets:
they are nowhere near full. It does **not** account for the working set. The
6.4 MiB is what the caches report holding, not the memory of everything those
entries reference, and the composition of the remaining hundreds of megabytes
is still unestablished — see `waste-audit-2026-09-22.md`, whose section on
cache budgets should be read against this.

## Next

Stages inside `FuelHorizon.BuildAsync`, separately around
`ReadConnectionAsync`, `ReadBaseAsync` and `CaptureRouteAsync`, plus a total
for the method. Measure first; optimise the expensive part after it is named.
