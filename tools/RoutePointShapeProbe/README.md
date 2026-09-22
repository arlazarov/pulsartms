# Route point shape probe

Compares the two possible shapes of `Domain.Models.Routing.RoutePoint` — the
`sealed record` class the codebase has today, and a `readonly record struct` —
on the payloads the routing code actually produces. It exists so that the
shape can be changed against a measurement rather than an argument, and so the
change can be re-checked afterwards.

It starts no host, no background services and no provider, and it never
connects to a database. It reads saved plan copies from a directory you pass
on the command line.

## Running it

```sh
dotnet run -c Release --project tools/RoutePointShapeProbe -- <plans-directory>
```

`<plans-directory>` holds `*.json` copies of `DispatchRoutePlans.PlanJson`.
**Keep them outside the repository** — they are carrier route geometry. Copy
them out once with a read-only query, for example:

```sh
psql "$PULSARTMS_READONLY_URL" -A -t -o /some/path/outside/repo/<id>.json \
  -c 'SELECT "PlanJson" FROM "DispatchRoutePlans" WHERE "Id" = '"'<id>'"';'
```

Pick several plans of different sizes. The measurements below were taken on
three, of 5,973 / 9,917 / 12,612 points.

## What it measures

Four scenarios, each decoded by both shapes from **identical JSON bytes**, with
the decoded coordinates compared point by point before anything is timed:

| | scenario | corresponds to |
|---|---|---|
| A | full geometry | a cold plan load, `RoutePlanStorage.Read` |
| B | simplified geometry | `displayJson`, after `TrimForDisplay` simplifies each leg at two metres |
| C | no geometry | `metadataJson`, where `GeometryOmitted` empties every leg |
| D | index boundary — **a model, see below** | the fuel search's `Match`/`At` calls |

Per scenario: allocated bytes per operation, milliseconds per operation, and
collection counts by generation.

## Conditions

- Release, workstation **non-concurrent** GC (set in the `.csproj`), so
  collection counts belong to the measured work.
- Both shapes are warmed 350 times each before measurement, then measured in
  **alternating rounds** — 7 rounds of 20 iterations, median reported.
- Every round is preceded by two forced blocking gen2 collections.
- `GC.GetTotalAllocatedBytes(precise: true)` counts the whole process, which is
  why this probe does no other work while measuring.

The alternation matters. An earlier version measured one shape to completion
and then the other, which made whichever ran first pay for tier-0 code and
produced a 20% reading in the **wrong direction**. If you change this probe,
keep the warm-up and the alternation.

## Limitations — read before quoting a number

**Scenario D is a model of the index boundary, not a measurement of
`RouteGeometryIndex`.** It faithfully reproduces the one thing under test: the
real index stores coordinates as value types inside and returns a `RoutePoint`
across its API. It does **not** reproduce the real `Match`, which narrows by
block with a cosine bound where the model scans linearly. **Read only D's
allocated bytes.** Its time is a property of this file. The byte figure is
verifiable by hand — one returned point per call, 32 bytes each on 64-bit.

**The collection counts say nothing.** Twenty iterations of a megabyte or two
rarely trip a collection under workstation GC with a large gen0 budget, so that
column sits near zero for both shapes. Whether the shape change reduces GC
pressure in the server is **not established here** and needs a sustained load,
not a microbenchmark.

**A decode is not the application.** Scenario A's time is the cost of decoding
route geometry, not of serving a request. Around half a millisecond per plan
load, against a fuel path that measures in seconds. Do not read a percentage
here as a percentage of application CPU.

**The models are not the production types.** They mirror `TruckRoute`,
`RouteLeg` and `RoutePoint`. A real `RoutePlan` also carries stops, a fuel
plan, tracking and profile, all of which decode too. The measured difference is
the geometry portion — which is the portion the shape change affects — but the
whole-read cost in production is higher than scenario A shows.

**One machine, one architecture.** Taken on Arm64 with workstation GC. The
server runs x64 with Server GC in a 512 MiB container. The direction should
carry; the magnitude is not established there.

## Results

`docs/archive/2026-09/routepoint-shape-measurements-2026-09-22.md`.
