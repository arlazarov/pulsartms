# What the fuel corridor filter spends, and on which half — 2026-09-22

Follows the production reading that put 4,370.6 ms of a 5,180.5 ms fuel preview
into `FuelRegionPlanner`'s `corridor` stage, over 696 priced stations, at
6.28 ms each. That stage does two things per station, and this establishes
which of them owns the time. Taken with `tools/FuelCorridorProbe` on the real
`FuelSearchGeometry` and the real `RouteRegionLookup`; its README holds the
method and the limits.

## The split

Three saved routes, the full 698-station catalogue, Arm64, Release:

| route | `geometry.Match` | `countries.Matches` | corridor as written |
|---|---|---|---|
| 12,612 pts / 767 mi | **611.2 ms** | 1.0 ms | 609.2 ms |
| 9,917 pts / 640 mi | **475.3 ms** | 1.0 ms | 477.2 ms |
| 5,973 pts / 386 mi | **350.1 ms** | 0.9 ms | 350.7 ms |

**The country check is not the cost.** One millisecond for 698 stations,
0.001 ms each — consistent with the separate finding that a GeoTimeZone lookup
costs about a microsecond. All of the corridor's time is `geometry.Match`.

## Why the match costs what it does

The index reports `SegmentsExamined`, so it can be asked directly:

| route | points | median segments examined | share of route | total |
|---|---|---|---|---|
| 12,612 | 12,612 | 12,611 | **100 %** | 6,848,454 |
| 9,917 | 9,917 | 9,916 | **100 %** | 5,279,016 |
| 5,973 | 5,973 | 5,971 | **100 %** | 3,893,412 |

The median station walks the **entire road**. The index builds 789 blocks for
the longest route and prunes with essentially none of them.

The reason is in `RouteGeometryIndex.Match`: a block is skipped when

```csharp
LowerBoundSquared(block, point, cosine) > searchDistance * searchDistance
```

where `searchDistance = Math.Min(bestAway, maximumAwayMiles)`. `bestAway` is
the best distance found so far, and `maximumAwayMiles` defaults to infinity.
For a station far from this road — which is almost every station in a national
catalogue against a regional route — `bestAway` settles at hundreds of miles,
the bound prunes nothing, and the search degenerates to the linear scan the
blocks exist to avoid.

## What the caller actually wanted

`corridor` keeps only stations within two miles:

```csharp
return match.Away <= 2 && countries.Matches(match.Point, x.Station);
```

It never says so. `FuelSearchGeometry.Match` does not expose
`maximumAwayMiles` at all, although the index underneath supports it and
`MatchLeg` passes it through. Asking the same question with the limit supplied,
on the single-leg routes:

| route | unbounded | bounded at 2 mi | segments | result |
|---|---|---|---|---|
| 12,612 pts | 611.2 ms | **1.0 ms** | 6,848,454 → **320** | 10 stations both ways |
| 9,917 pts | 475.3 ms | **0.8 ms** | 5,279,016 → **256** | 10 stations both ways |

Roughly **600× less work for the same answer**, and the ten stations kept match
what production reported for `corridor-kept`.

## What this does not establish

**That production would improve by the same factor.** The absolute per-station
figure here is about seven times faster than the deployed one — Arm64 against
one x64 vCPU with background work. The ratio between the halves is what
transfers.

**That bounding is correct in general.** It was checked on two routes where
both forms named the same ten stations. A change would need a test asserting
that the bounded and unbounded matches agree for every point inside the limit,
not a spot check.

**That `MatchLeg` is the fix.** It restricts to one leg and returns miles
relative to that leg, so it is not a drop-in replacement; the third route has
two legs and the probe skips the comparison there for exactly that reason. The
change this points at is threading `maximumAwayMiles` through
`FuelSearchGeometry.Match` itself, and giving the corridor filter a way to say
the two miles it already means.

**Nothing was changed to take these numbers.** Two other things noticed while
instrumenting — the same `Match` cost in the `eligible` filter, and a
loop-invariant `onward.At(onward.Miles)` inside its lambda — were deliberately
left alone so this first reading is of the code as it stands.
