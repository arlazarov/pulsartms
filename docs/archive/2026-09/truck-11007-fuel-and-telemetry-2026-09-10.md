# September 10 local fuel and telemetry follow-up

## Scope

- Effective default tank capacity is 250 US gallons; no bulk truck-data rewrite.
- Station search allows forty geographic miles from saved route geometry. Estimated access fuel in both directions, access time, stop cost, reserve and schedule protection remain part of economic selection. This is not verified road access or a guarantee of savings.
- Current-GPS route matching remains two miles, independent of station search.
- Shared speed thresholds apply to Dispatch driving pills and map telemetry. Fuel and engine indicators retain semantic warning colors. HOS labels distinguish duty clocks from vehicle motion.
- Fuel price comparison retains compact blue Your price and green IFTA/Savings styling, with next-date values and server-calculated differences to the right.
- Valid planned fuel visits remain visible with the ordinary Fuel Stations layer disabled.
- Picked-up loads enter Papers In Transit even on their pickup date. CSS URLs receive a content version.

## Runtime evidence

Read-only diagnostics for truck 11007's current AMF1383 load found its saved route version 4 was 2.2766 miles from the current GPS position. The fuel route guard correctly rejected that stale match. Normal background preparation produced route version 5; a later match was 0.0006138 miles away. The guard was not widened or bypassed.

The authorized Calculate Fuel action then succeeded: LOVES #412, 110 US gallons, approximately USD 614.02, with 25 gallons on arrival and 135 gallons after purchase. These were live values at verification time, before the subsequent forty-mile search expansion; prices and telemetry can change. Fuel 1 remained visible with ordinary stations disabled.

After restarting the local API and Client with the forty-mile implementation, authenticated Fleet Map loaded successfully. Visual inspection of LOVES #625 confirmed compact September 10/11 comparison columns, blue Your price, green IFTA/Savings and warm-colored unfavorable changes.

## Checks and limits

### Later local quote and calculation verification

Ordinary station inspectors now use content-sized width with a 22.5-rem minimum
and 32-rem maximum, both bounded by map width. The old two-column ordinary-station
layout no longer reserves an empty column. Live DOM measurement confirmed unchanged
map bounds when opening the inspector. The final comparison adds server-calculated
percentage changes and relative Yesterday/Today/Tomorrow headings. Browser inspection
of LOVES #625 showed Today 5.540, Tomorrow 5.724, change +0.184 and +3.32% for Your price.
Unfavorable changes are orange, favorable changes green, and unchanged values neutral.

A subsequent report that 11007 still could not calculate was not reproducible in
the local session: the explicit Calculate Fuel request completed in approximately
9.3 seconds and the editor showed LOVES #435, 117 US gallons, approximately USD 655.67.
This is not evidence that an intermittent stale-route failure has been permanently
fixed. The inspector's Calculate Fuel action updates the plan without automatically
opening the editor. A contemporaneous read-only diagnostic showed route version 6
matching current GPS within 0.001 mile and a saved fuel result.

Final quote follow-up `bash test.sh all`: 1,405 server, 634 Client C#, 386 JavaScript
tests passed (2,425 total). Strict Client build and API publish succeeded. Local
runtime was restarted from `api-quotes` and `client-quotes-final`; no deployment
or migration. Logs are `quote-full.log`, `client-quotes-final.log` and `api-quotes.log`
under the same artifact directory. PostgreSQL execution and production performance
were not tested.

- Final `bash test.sh all`: 1,404 server, 634 Client C#, 385 JavaScript tests passed; no failures or skips. Includes architecture checks.
- Strict Client build succeeded with zero warnings/errors; API publish succeeded.
- Initial forty-mile tests exposed fixtures whose formerly distant stations were now valid candidates. Those fixtures were moved outside the new boundary, and positive/negative economic selection coverage includes forty miles. One allocation check failed during concurrent builds and passed in the final full run without weakening its bound.
- Local logs/builds: `artifacts/truck11007.HTCy9n`, including `full-40mi-verified.log`, `client-40mi.log`, `api-40mi.log`.
- No isolated PostgreSQL fixture was available; PostgreSQL execution checks were not run. No migration was applied. No deployment was performed. Production performance was not measured, and automated checks do not establish complete visual correctness.
