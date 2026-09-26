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

## Findings

- Request A reproduces the saved road exactly (1,229.8 mi).
- Traffic is not the cause: the delay is 0 and B returns the same road.
- A truck restriction on I-90 is not the cause: D uses the same truck
  profile and is 92.6 miles shorter and 48 minutes faster than A.
- The cause is TomTom's answer to `avoid=borderCrossings`: without it
  the fastest road crosses Ontario (C); with it, TomTom returns a US road
  that is not the best US road it can produce (D), and its alternatives
  are worse still.

Not fixed. The border hardening of September 25 asks for this option for
every one-country trip, so other trips across the Great Lakes region may
take the same kind of detour. A fix (a guiding waypoint, another way to
exclude Canada, or comparing against a waypointed road) is a routing
decision for the owner and Root.
