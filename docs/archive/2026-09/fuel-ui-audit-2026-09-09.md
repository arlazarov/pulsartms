# Fuel visits and search audit — 2026-09-09

## Scope

Requested fuel-card redesign, review of fuel selection and recent map/Dispatch
changes, followed by a verified API and frontend release. The workspace has no Git
metadata; review was source-, regression-, and archive-guided, not an exhaustive
commit diff. No production database was used as a test fixture.

## Corrections

- Fuel visits have ordered numbered cards, projected arrival and departure fuel
  rings, and the planned purchase between them. Repeated physical stations keep
  separate visits. Percentage rings use physical tank capacity. Units follow the
  server quote; unknown gauge inputs never become a zero-percent tank.
- Price-colored map circles stay unchanged, with order badges outside them.
  Display quotes now come from server-selected road diesel, not whichever active
  discount happened to be first. IFTA toggles reselect the ready quote.
- A full-route reachability backbone gets shortlist priority. An 8,000-mile pure
  regression demonstrates why filling a bounded shortlist from early route bins
  could falsely lose coverage near the destination.
- Economically evaluated terminal plans may choose a worthwhile top-up above 80%
  fuel. The legacy non-economic policy remains intact; stop cost and minimum
  purchase still prevent gratuitous tiny fills.
- Fuel planning explicitly includes overdue unfinished assignments without changing
  the normal Dispatch board. Build, reuse, projection and commit share that scope.
- Remaining-stop identity checks reject completed future stops without invalidating
  a legitimately completed prefix during rollover. Automatic calculation and read
  projection agree on valid nonfuture reading timestamps. Old readings remain
  usable without assumed burn; a recent explicit manual quantity remains supported.
- Selection version 15 forces the next explicit search to use corrected rules.
  Otherwise-valid versions 11–14 remain displayable until a replacement succeeds.
- Quiet unavailable HOS presentation removes internal maintenance explanations.

## Verification and release

- Full strict release gate: 861 Server tests, 453 Client C# tests, and 211 Node
  checks passed, with zero warnings or errors. Published asset integrity verified
  222 assets and six JavaScript dependency graphs.
- Offline staged UI: 44 page checks across both themes, 1440px/390px/2344px and
  100%/200% root font scale; no failures, browser errors or unexpected requests.
- Offline GPU/card smoke: 36 scenarios, including paired fuel gauges at 390px,
  repeated station visits, known diesel prices and unchanged marker price colors;
  no failures, browser errors or unexpected requests. Screenshots were inspected.
- Additional ETA/cycle browser scenario: eight cases, 56 screenshots, no failures,
  errors or unexpected requests. Its stale reset/fuel-summary expectations were
  replaced with explicit absence checks matching the user's earlier removals;
  retained recap and fuel-bridge values are still asserted across refresh.
- One operational recalculation for truck 54777 saved version 15 at
  `2026-09-09T14:32:59.091079Z`, covering AMF1375 and AMF1373. It selected LOVES
  #682 (112 US gal) and LOVES #305 (165 US gal). This uses the then-current tank
  reading; subsequent sensor changes are subject to ordinary validation. The
  real popup displayed its numbered arrival/departure gauges and server quote.
- API deployed successfully: Cloud Build `cf916d8d-7a33-45ac-ac6e-7f78dac3e999`,
  digest `sha256:1297121ac7bb765149853dd80be500745d81bb12d2425bfc86fc617dbacd999e`,
  Cloud Run revision `amftms-api-00085-6rm`, 100% traffic, live health `Healthy`.
- Verified frontend artifact: `artifacts/release.fgJFGx/publish/wwwroot`.
  Hosting publication is pending Firebase reauthentication. The expired Firebase
  login and reuse of the already-authorized Google Cloud access token both failed;
  a Firebase sign-in tab has been handed to the user. No frontend release success
  is claimed yet.

The first optional staged browser run lacked its bundled Chromium executable;
the complete gate was rerun successfully using the installed Chrome. The
authenticated long map lifecycle soak and isolated PostgreSQL checks were not run.

## Fuel badge polish follow-up

- Fuel order numbers now use centered blue badges with white text above the
  unchanged price-colored circles. Badge clicks select the same station.
- Visit headings reuse that number treatment, a compact distance and a subtle
  divider. Single visits fill the available row with a centered 272px gauge
  group; repeated visits retain separate cards. Purchase values and units are
  unchanged.
- Affected checks passed: 87 Server, 178 Client C#, 170 map JavaScript, 17 style
  and 18 JavaScript architecture checks. Final GPU/card smoke passed 40 cases
  with 52 screenshots and no failures, browser errors or unexpected requests.
  Desktop and mobile single/repeated cards and map badges were visually inspected.
  Report: `Client/test-results/fuel-badge-polish-full-row-2026-09-09/report.json`.
- The final complete gate passed 861 Server, 453 Client C# and 211 Node checks,
  strict builds, 222 asset hashes, six JavaScript graphs and 44 offline UI page
  checks. An intermediate attempt stopped on an MSBuild child-worker failure;
  the entire gate was rerun successfully with node reuse disabled.
- Latest verified frontend artifact: `artifacts/release.3huzn7/publish/wwwroot`.
  It supersedes the earlier artifacts in this report. Firebase reauthentication
  remains incomplete; this frontend has not been published. API revision 85 is
  unchanged. The fuel-search and persistence findings below remain unresolved.

## Limits retained

This remains a bounded optimization among checked alternatives, not a proof of
the globally cheapest possible route. The default 24-candidate shortlist and 12
candidate route checks stay bounded; cold preparation of assigned base/deadhead
routes and terminal escape checks are separate work. The complete itinerary must
fit mandatory-stop and provider-waypoint limits; unsupported plans fail closed
instead of publishing partial coverage.

No isolated PostgreSQL fixture was available, so PostgreSQL execution/migration
tests were not run. No new migration is required for these additive JSON/DTO fields.
Production latency and provider request volume were not benchmarked. Offline
browser fixtures do not establish geographic/provider or real-world route safety.
Two previously recorded Canadian station coordinate anomalies remain a separate
data-quality issue; this task does not overwrite addresses from guesses.

## Unresolved follow-up findings

- Confirmed persistence mismatch, not fixed in this audit: `TruckFuelPlanStore.ValidSummary`
  still limits `Plan.DispatchIds` to three (`Server/Infrastructure/Persistence/TruckFuelPlanStore.cs:66`).
  `FuelHorizon.BuildAsync` now includes all assigned loads within 40 mandatory stops,
  and `FuelPlanningService.CommitAsync` passes those complete IDs to the store.
  Therefore an otherwise valid four-or-more-load result is rejected before the
  store's SQL write; reads also reject such summaries. The three-load limit was
  the only obsolete dispatch-count bound found in the scoped fuel lifecycle search.
- Existing all-assignment coverage verifies a five-load horizon but stops before
  persistence (`Server.Tests/Routing/AutomaticPlanningTests.cs:337`); the actual
  fuel-recalculation test covers only three loads. Real-store tests use a two-load
  fixture and do not cover this boundary. Add an isolated real-store round-trip
  for four loads, ideally also the 40-stop boundary, plus a four-load recalculation
  commit regression. Keep the 40-stop, geometry, payload, ownership and newer-only
  write checks intact. No server source changes or live operations were performed
  for this finding.

## Truck 54777 purchase follow-up — read-only replay

The owner's later screenshot questioned the 112-gallon purchase at LOVES #682.
The saved version-15 calculation at `2026-09-09T14:32:59.091079Z` starts with
50.721 gallons, not the roughly 100-gallon reading used by the earlier #706
calculation. The first checked visit arrives with 30 gallons, buys 112 and leaves
with 142; the second visit is LOVES #305 on the return journey in AMF1373.
The 779.77 miles between them consume 117 conservatively rounded gallons at
6.720416657142858 MPG, leaving the configured 25-gallon reserve. This explains
the quantity for that selected pair, not the optimality of selecting the pair.

An isolated pure replay of the saved baseline and current normalized USD road
diesel quotes reproduced all twelve actual scheduled chains in order. It found
52 route occurrences, 22 shortlisted candidates out of the allowed 24, and 38
local scenarios. Only two saved checked chains were feasible: the selected
#682 + return #305 at $1,996.441, and #366 + #366 at $2,039.54. Nine alternatives
failed detour limits; eight included the same distant #397 station.

LOVES #706 at $5.613/gallon is only $0.09 cheaper than #682 at $5.703. Adding its
southbound occurrence to the selected pair does not create a useful purchase in
the local optimizer: the small fuel saving does not cover the third stop's
configured $20 cost. A 20–30-gallon first purchase therefore is not inherently
cheaper under the current settings.

The replay also confirmed a shortlist omission: LOVES #333 at $5.445/gallon and
6.67 estimated access miles is absent, while cheaper #397 at 28.78 access miles
dominates many checked variants. Manually including #333 gives a projected
#682 / #333 / return #305 purchase chain of 58 / 165 / 54 gallons, improving the
baseline comparison from $1,990.74 to $1,981.20. The $9.54 difference excludes
its actual road detour and is neither a checked saving nor proof that #333 wins;
additional fuel and time could erase it. A price/access-diverse shortlist and
check schedule deserve a separate correction within the same budgets. No
application change or provider recalculation was performed for this follow-up.

Ignored evidence is in `artifacts/fuel-54777-review-2026-09-09.json.gz.b64`, the
pure harness `artifacts/fuel-audit.oslzHC/54777/Review.csproj` / `Program.cs`, and
`artifacts/fuel-audit.oslzHC/54777/results.md`. The price input covers the route
bounding area, not the entire station database; exact first-twelve reproduction
is observed, not assumed. No production latency claim is made. The replay
process exited normally; no global MSBuild/dotnet shutdown or process kill was
performed during the concurrent release gate.
