# Truck starting-stop confirmation — September 11, 2026

## Implemented

- Shared truck-itinerary projection retains the full imported Dispatch record but
  excludes driver-only prefix stops from road, fuel and truck ETA inputs.
- Explicit per-stop truck assignment determines the automatic start. A missing
  trailer does not exclude truck travel. Missing truck data is not inferred from
  an old saved route.
- Dispatch Details provides a shared manual truck/start editor. Confirmation is
  separate from imports, permission-checked and revision/stop-identity guarded.
- Current and future roads, deadhead selection, preparation fingerprints, fuel
  signatures and map stop-detail identity consume the truck itinerary.
- An unresolved driver change blocks ETA rather than borrowing the former driver's
  HOS or assuming the receiving driver's personal travel is rest.

## Verification and rollout

`bash test.sh all`: 1,475 server, 666 Client C# and 425 JavaScript tests passed.
Strict isolated Client build and SCSS compilation passed. Offline staged UI smoke
in installed Chrome passed at 390, 1440 and 2344px,
both themes and 100%/200% text scaling. The default Playwright browser was absent;
Chrome was used without installing browsers. The read-only smoke does not verify
live confirmation persistence. Its report is under the managed browser artifacts.
SQLite fixtures and
PostgreSQL SQL-translation tests ran; no isolated PostgreSQL execution fixture was
available. These results do not prove production performance or live integrations.

Migration `20260911183435_AddTruckAssignmentStart` was generated, not applied.
No server/client deployment or AMF1376 manual assignment was performed. Updated
API code must not be started against the previous schema. AMF1376 still requires
confirmation of truck 54777 from its second stop after rollout. Receiving-driver
availability/HOS must be resolved before its downstream ETA can be promised.

Mixed-truck loads are guarded as ambiguous; this change does not introduce a
multi-truck route-segment storage model.

## Rollout follow-up

The first local release gate failed an allocation threshold (6,384 bytes against
4,096); its isolated cases and the complete unchanged retry passed. The retry
verified 252 published assets and seven JavaScript dependency graphs.

Cloud Build `8b27cb81-d4f6-4117-8e50-83f2ffd41e7a` failed before deployment on a
five-second next-load component-test timeout. Its 1,475 server tests passed.
The failing Client test and all 194 Fleet Client tests passed locally without
source or threshold changes. A fresh cloud gate is required; these local results
do not waive the cloud failure.

The unchanged retry, Cloud Build `3d52b7b0-1f3e-4511-82cf-a293b7f50203`, passed
and deployed API revision `amftms-api-00100-cmh` with 100% traffic. Image digest:
`sha256:85857b7bc1f34c2e183206d766ee73842a98d5446a53490e1c32f230ed56a1f1`.
Startup logs confirmed applying `20260911183435_AddTruckAssignmentStart` and its
migration-history insert. Firebase Hosting published the verified
`artifacts/managed/release-7yzc69/publish/wwwroot` artifact. Its offline Chrome UI
smoke passed before publication; production `/api/health/live` returned Healthy
and the served CSS version was `dd14c8d041488cde`.

AMF1376 confirmation remains pending: the available browser session requires
operator login. No database assignment write or impersonated operator was used.
The authenticated readiness endpoint and live confirmation workflow were not
verified without that session. Earlier rollout-pending statements above describe
the pre-deployment checkpoint, not the current migration/publication status.

## Following in-transit loads on the map

The operator subsequently confirmed AMF1376 for truck 54777 from stop 2. Dispatch
showed its driver-only prefix correctly, but `NextLoadSelection` still discarded
following in-transit loads. Removing that final assigned-only filter retains the
current-load exclusion and existing completion/order rules. Regression tests cover
explicit/inferred current selection, completed/earlier exclusions and ready saved
geometry with a manually confirmed truck.

The complete local release gate passed: 1,479 server, 666 Client C# and 425 Node
tests. Initial Cloud Build `6fb40d40-d633-49ab-b15d-d7c2546b376c` failed a one-second
Client render wait; the same five speed-badge cases passed locally. The unchanged
full gate passed on a one-off `e2-highcpu-8` worker in build
`251edddd-58e5-4ccb-9426-b155095703fa`; no test thresholds were altered.

API revision `amftms-api-00101-w5h` received 100% traffic with image digest
`sha256:d47c29f042398380faf9ffd95d091f380133ee0b44fc4e081cd93d274ae16b23`.
No client publication or migration was needed for this filter change. Production
liveness returned Healthy and the new revision served the 54777 next-routes
requests with HTTP 200. Read-only diagnostics confirmed matching saved geometry
for AMF1376's Webster/Port St. Lucie truck stops and its saved connection.

The authenticated Chrome page had Next loads enabled, but visual route acceptance
was blocked by its missing WebGL/3D context and vector-map fallback. HTTP success
and saved geometry do not constitute a verified rendered route in that browser.
