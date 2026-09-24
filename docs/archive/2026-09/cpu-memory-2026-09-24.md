# CPU and memory, 10-truck fixture (September 24, night)

Local only. The setup was the real API on the isolated fixture schema, with
synthetic routing and HOS providers, in a Linux ARM64 container limited to
one CPU and 1 GiB (`tools/FleetLoadProbe`). No production database,
provider or deployment was involved. The fixture's synthetic routes are
about 4,000 km with ~75,000 points each, larger than most real loads.
Samples are about every 3 seconds and can miss short peaks.

## Load run (build at eae82c2f)

Five dispatchers polled for 180 s: locations, truck planning, the Dispatch
board, the Messages unread notice and inbox. Telemetry and planning
enqueues ran every 30 s. All 697 requests passed and the queue drained
with no retries.

| Phase | Container peak MiB | CPU avg / p95 (% of one CPU) | Allocation MB/s | Gen0 per min | Gen2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Cold preparation | 638 | 41 / 67 | 67.7 | 515 | 53 |
| Steady (5 dispatchers) | 720 | 34 / 62 | 58.2 | 390 | 80 |
| Drain | 684 | 25 / 41 | 42.9 | 292 | 6 |
| Idle, no clients | 651 | 11.5 / 28 | 13.6 | 91 | 32 |

At idle the process ended with a working set of 634 MiB, a managed estimate
of 207, a heap after the last GC of 349 and GC committed 368. These overlap
and must not be added.

| Request (steady) | Median ms | p95 ms |
| --- | ---: | ---: |
| Dispatch board | 1,328 | 2,074 |
| Messages inbox (no conversations) | 293 | 492 |
| Messages unread notice | 252 | 375 |
| Fleet locations | 65 | 247 |
| Truck planning read | 39 | 555 |

The board and the empty inbox are foreground costs dominated by database
round trips to the remote fixture. They were measured but not investigated
tonight.

## Idle: attributed by an allocation trace

With no clients and the queue drained, the idle process still used about
12% of a CPU and 13–15 MB/s of allocation. An EventPipe GC allocation trace
of 188 s (`run.py restart --trace`) attributed it:

| Owner (nearest application frame) | MB sampled |
| --- | ---: |
| `SavedRouteReader.Read`: base road re-parsed to attach a display reference on each plan read | 880 |
| No application frame (framework, GC, runtime) | 815 |
| `PlanningSummaryCache.Complete`: summary serialization every 30 s per running truck | 269 |
| `RouteChunkPacker.Decode` | 203 |
| `RouteDisplayCache.Snapshot.ReadPlan` | 178 |

Most of the first line was System.Text.Json building the positional
`RoutePoint` record through its constructor: `ArgumentState` (379 MB) and
`Arguments<…>` (321 MB).

My first estimate, 40 MB per truck per 30-second cycle, all from summary
serialization, was arithmetic from the total rate and code reading. The
trace showed serialization is 10% of idle allocation, not most of it.

## Fix and before/after

`a972cb54` gives the routing JSON options a `RoutePoint` converter. It writes
the same bytes and reads the same shapes the defaults did. On the same
fixture and schema, idle, 190 s windows:

| | Before | After |
| --- | ---: | ---: |
| Allocation, MB/s | 14.5 | 9.9 (−32%) |
| `SavedRouteReader.Read`, MB | 880 | 364 |
| Constructor-argument types, MB | 700 | 0 |
| Idle CPU, % of one CPU | 11.8 | 12.4 (no improvement; within noise) |

The after build also contains this evening's other commits; none of them
is on this path. Unit measurement: a 20,000-point road reads with 1.17 MB
instead of 3.25 MB.

## Not measured, or left

- **Why a plan read re-parses the base road.** Proposal in the
  [cohesion review](cohesion-review-2026-09-24.md). Removing that read would
  be the next idle reduction.
- **Idle CPU** did not move with allocation. Its source (the 30-second
  summary and planning loops, GC, the runtime) was not attributed; a CPU
  sample trace was not taken.
- **No long-running or recovery run.** The runs lasted minutes. Leaks over
  hours, restart recovery and a 512 MiB limit were not tested.
- **Foreground costs.** Dispatch board and inbox round trips were not
  investigated.
- **Cloud Run.** Memory and CPU there, and real routes and real provider
  latency, cannot be inferred from this fixture.

## Side findings

- **The probe API does not exit on SIGTERM within 90 seconds.** Docker
  killed it both times (exit 137), and a summary refresh logged an
  `ObjectDisposedException` during shutdown. The probe runs the planning
  workers outside the host and cancels them only after the host has
  stopped. Production runs them as hosted services, so this was not shown
  to affect production shutdown. That was not verified either.
- **A stray command.** While re-running the trace I ran a stray
  `docker run hello-world` by mistake. A `hello-world` image is present
  locally; whether this pulled it or it was there before is unknown. It was
  not removed.
