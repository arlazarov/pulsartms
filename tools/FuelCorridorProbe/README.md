# Fuel corridor probe

Splits `FuelRegionPlanner`'s `corridor` filter into its two halves and measures
each on real data, using the **real** `FuelSearchGeometry` and the **real**
`RouteRegionLookup` rather than a model of either. It exists because production
measured that stage at 4,370.6 ms over 696 priced stations and the stage covers
two things done per station:

```csharp
var match = geometry.Match(x.Station.Point, progress, ct);
return match.Away <= 2 && countries.Matches(match.Point, x.Station);
```

No database, no host, no provider. It reads a plan copy and a station list from
files given on the command line.

## Running it

```sh
dotnet run -c Release --project tools/FuelCorridorProbe -- <plan.json> <stations.json>
```

Both files stay **outside the repository** — they are carrier route geometry and
the station catalogue. Copy them out once with read-only queries:

```sh
psql "$PULSARTMS_READONLY_URL" -A -t -o /outside/repo/plan.json \
  -c 'SELECT "PlanJson" FROM "DispatchRoutePlans" WHERE "Id" = '"'<id>'"';'

psql "$PULSARTMS_READONLY_URL" -A -t -o /outside/repo/stations.json \
  -c 'SELECT json_agg(json_build_object('"'"'lat'"'"',"Latitude",'"'"'lon'"'"',"Longitude",'"'"'country'"'"',"Country")) FROM "FuelStations" WHERE "Latitude" IS NOT NULL;'
```

## What it measures

1. `geometry.Match` over every station, reporting the `SegmentsExamined` the
   index already returns — min, median, max and total.
2. `countries.Matches` over every station, on a **fresh** `FuelAccessCountries`
   each time, because it memoises per point and a warm cache would answer a
   question nobody asked.
3. The corridor filter exactly as written, which short-circuits: a station more
   than two miles off the road never reaches the country check. The filter is
   therefore not the sum of the two halves.
4. On a single-leg route only: the same match with `maximumAwayMiles = 2`
   supplied through `MatchLeg`, which is the question the caller actually has.

Each measurement is preceded by two warm-up passes.

## Limitations

**Absolute times do not transfer.** Taken on Arm64 with a warm process and no
other load. Production is x64, one vCPU, with background work running. The
per-station figure here came out about seven times faster than the deployed
one; read the **ratio between the halves**, not the milliseconds.

**One station catalogue, three routes.** Whether a station is near a road
depends on both. The catalogue is national and the routes are regional, so
almost every station is far from almost every route — which is the normal case
for this filter and the reason it behaves as it does, but it is not every case.

**Step 4 changes the question slightly.** `MatchLeg` restricts to one leg and
returns miles relative to that leg's start. On a single-leg route that is the
same query; on a multi-leg route it is not, which is why the probe skips it
there. It shows what a bounded search would cost, not a drop-in replacement.

**This is a measurement, not a patch.** Nothing in the shipped code was changed
to take these numbers, deliberately, so the first reading is of the code as it
stands.

## Results

`docs/archive/2026-09/fuel-corridor-measurements-2026-09-22.md`.
