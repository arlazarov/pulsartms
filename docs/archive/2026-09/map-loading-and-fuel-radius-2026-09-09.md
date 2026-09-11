# Stable map loading and nearby fuel follow-up — September 9, 2026

## Scope

- Fuel selection version 22 expands the station search from two to ten geographic
  miles. Estimated road access, access fuel, driving time and the configured stop
  charge remain part of the objective. The separate two-mile GPS route-match
  tolerance does not change. The search still uses saved road geometry, not new
  provider routes, and includes all assigned loads.
- Truck/HOS and route/ETA panels reserve responsive minimum dimensions. Cold reads
  retain the ready panel's three mileage columns and address/ETA slots without
  `Loading saved route…`. Empty placeholders cannot retain invalid route ownership
  or ETA values.
- Recommended fuel stations have compact dark `Fuel 1` order badges above their
  unchanged price-colored points and blue rings. Repeat visits share one badge;
  load stops remain individually numbered, route-colored circles. Both the badge
  and station remain clickable.
- A saved preview without progress cannot draw or fit a driven route prefix.
  Plan/progress arrive atomically at the route layer, which first draws the known
  remaining section. A warm missing-progress read preserves only the previously
  trimmed road, without publishing retained mileage as a fresh measurement.
  Manual dragging cancels a deferred fit; following and disposal also prevent it.

## Station #333 diagnosis

A bounded read-only snapshot check found LOVES #333 approximately 6.67 geographic
miles from each relevant saved leg. The previous two-mile cutoff excluded it
before economic comparison. It was not excluded because of starting fuel.

A same-quantity comparison against the saved LOVES #399 purchase gave about $34.19
in gross price savings and $37.56 in extra estimated access fuel/time, a difference
of approximately $3.37 against #333. This is not a complete replacement itinerary
comparison or a verified driving distance. No station was manually selected or fuel
plan recalculated as part of the diagnosis or deployment.

## Verification notes

The preliminary full local gate found four outdated Client assertions requiring
the entire empty panel to disappear. Updated assertions instead require neutral
mileage placeholders, no retained stop/ETA content, and a null map-route payload.
All 46 FleetMap component tests then passed. This preserves the ownership invariant
while accommodating the requested stable panel.

The first sizing probe passed ten light/dark viewport cases with matching cold and
ready panel heights/map position, including 1028px and 390px widths. It used the
previous verified WASM with new CSS in an isolated offline browser; it is not a
final-artifact or live-provider result.

The fuel-badge GPU/popup probe passed 40 offline cases with no browser errors or
unexpected requests. Screenshots were inspected. These synthetic scenes verify
appearance, selection and popup layout, not crowded real maps or fuel economics.

After UTC midnight, five cases in three existing fuel integration tests failed
because they reused Toronto fuel-price dates as UTC dispatch schedule dates. The
fixtures now separate these dates and explicitly assert the prepared root dispatch.
All 43 class cases passed during the formerly failing time window. No production
calendar behavior changed and no assertions were removed.

The final complete local release gate passed 1,176 Server tests, 486 Client C# tests
and 251 Node tests (1,913 total, zero failed/skipped), including architecture checks.
Strict solution and Client builds had zero warnings/errors. The staged publish
verified 231 assets, compressed variants and six JavaScript dependency graphs.
The Cloud Build strict compilation and the same Server/Client suites also passed.

The exact `release.AAbSna` artifact passed all eight full hours-forecast browser
cases without CSS overrides, browser errors or unexpected requests. Sixteen strict
refresh probes observed no empty ETA cards. Loading/ready panel heights and map Y
matched, with no horizontal overflow. The earlier ten-case CSS sizing probe adds
the 1028px breakpoint check. Wide and mobile screenshots were inspected.

Firebase Hosting published `artifacts/release.AAbSna/publish/wwwroot`. Twelve GET
checks across both public hosts matched the exact artifact SHA-256 for index,
Fleet Map fallback, CSS, fleetMap.js, gpuScene.js and Client WASM. The live page was
reloaded successfully. Local Client and API were rebuilt/restarted with migrations,
synchronization and Gmail background maintenance disabled for the local API.

## Deployment

Final Cloud Build: `a1a32dc6-2411-4dd1-8a12-addeaf6deb80`, status `SUCCESS`.
Cloud Run revision: `amftms-api-00089-2w2`, serving 100% of traffic with
Ready/ConfigurationsReady/RoutesReady all true. Exact deployed image:
`sha256:58a1bfbb676289a3d86fc1c7375b91482d346aa1bd64a60ec18c4addfdc1443e`.
Previous revision: `amftms-api-00088-vhz`.
Both public hosts and the direct API URL returned `200 Healthy` after rollout.
The complete Client artifact, not an older publish folder, was used for Hosting.

Two earlier follow-up Cloud Builds were cancelled before deployment while the
Client placeholder regression and then the date-dependent fixture failures were
being corrected. Neither replaced the running API revision. The final release
gate was rerun in full after these corrections and the manual-drag regression.

## Limitations

No isolated PostgreSQL fixture, authenticated lifecycle soak or production memory
benchmark was run. Ten miles is a geographic candidate radius; access road length
and time remain estimates. Exact terminal-endpoint visits and projection while a
truck is more than two miles from the saved road require separate handling; this
radius change does not silently broaden GPS matching or claim to fix those cases.
There are no new migrations or production profile/data repairs in this follow-up.
