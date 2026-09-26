# Truck 11007's detour at Erie (September 26)

The owner reported that 11007's road (AMF1414, Ticonderoga, NY to
De Pere, WI) was "crooked again". The saved road is continuous and stays
in the US, but after Erie, PA it leaves I-90, runs about 130 miles south
on I-79 to near Cranberry, PA (40.68 N) and returns north-west on the
turnpikes: about 200 miles where I-90 takes about 110.

## Comparison (owner-authorized, 2026-09-26 09:13 UTC)

Five TomTom Routing requests with the production query
(`TomTomRoutingProvider.Query`: truck, commercial, `routeType=fastest`,
13.5 ft, 8.5 ft, 72 ft, 80,000 lb, 20,000 lb axles, 5 axles, ferries
avoided), from the saved road's start (43.89354, -73.40377) to the
delivery (44.42875, -88.09795). Nothing was saved and no plan changed.

| Request | Miles | Hours | Southmost point between Erie and Pittsburgh |
|---|---|---|---|
| A: as production (`traffic=true`, `avoid=borderCrossings`) | 1,229.8 | 18.09 | 40.678, -80.098 (I-79) |
| A with 2 alternatives | 1,229.8 / 1,352.2 / 1,642.7 | 18.09 / 20.01 / 23.52 | all further south |
| B: `traffic=false`, border avoided | 1,229.8 | 18.09 | 40.678, -80.098 |
| C: traffic, border allowed | 1,144.1 | 17.77 | north of Lake Erie (Ontario) |
| D: A plus a waypoint on I-90 near Ashtabula, OH | **1,137.2** | **17.29** | 41.909, -80.599 (I-90) |

## Findings (corrected at 10:00 UTC)

The first reading, that `avoid=borderCrossings` makes TomTom return the
detour, was wrong. Seven more requests (09:54-09:58 UTC, 12 in total),
recorded in `artifacts/managed/diagnostic-LPkdbv` (no key in them):

| Request | Miles | Hours | Path at Erie |
|---|---|---|---|
| U: no border option (the fastest road) | 1,144.1 | 17.75 | through Ontario |
| Ontario blocked by 4 `avoidAreas` rectangles built from U, border option kept | 1,229.8 | 18.06 | I-79 |
| the same rectangles, border option dropped | 1,229.8 | 18.06 | I-79 |
| `alternativeType=betterRoute` with the saved road as `supportingPoints` | 1,229.8 | 18.06 | I-79 only; nothing better offered |
| A with a 54 ft vehicle | 1,229.8 | 18.06 | I-79 |
| A with 60,000 lb | 1,229.8 | 18.06 | I-79 |
| A with `vehicleCommercial=false` | 1,229.8 | 18.05 | I-79 |

- Request A reproduces the saved road exactly; traffic, length, weight,
  the commercial flag and the border option are ruled out as causes.
- With Canada excluded in any way, TomTom's own answer - including its
  "better route" search against the saved road - is the I-79 road.
- Only a waypoint on I-90 near Ashtabula (D) gives a shorter, faster road
  (1,137.2 mi, 17.29 h). A waypoint can lift a restriction that applies to
  through traffic, so D is not known to be legal for this truck without
  the stop; nothing in the responses says which segment TomTom avoids.

## Decision needed

No general code change is defensible from this evidence: steering trucks
through a waypoint TomTom would not choose itself could route them where
they may not go through. Options for the owner and Root:

1. Accept TomTom's truck road as it is (current behaviour).
2. Ask TomTom support why the truck profile avoids I-90 between Erie and
   Cleveland (the requests above are recorded for that).
3. Let a dispatcher add a waypoint on a load that is known to be legal,
   through the existing route options, per load, with the border check
   still applied.

Nothing was changed in code, in saved plans or in production.
