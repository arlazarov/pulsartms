# Compact map panels and Dispatch details — September 10, 2026

## Implemented behavior

- Selected truck and current-route information now overlays the same map stage.
  Selecting, loading, switching, refreshing and clearing a truck does not reserve
  document-flow space or resize the map. The page heading and filters remain
  outside the overlay. Mobile uses a summary toggle and bounded scrolling.
- The fuel editor is a non-modal, 26rem card inside that existing map, without
  another map region, dimmed background or inert page controls. Timeline and
  station details scroll independently; header and save/cancel controls remain
  accessible. Mobile uses a shorter timeline and a 60dvh height cap bounded by
  the map. Partial quantities, full tank, reordering and server-provided costs
  retain their existing contracts.
- Camera focus and route fitting account for the visible panels. Overlay geometry
  is cached from layout observation; Follow does not measure DOM rectangles on
  every frame. Observer disposal and first-selection visibility are covered.
- The shared load-details dialog is content-sized, with adjacent stop cards when
  space permits. Address, facility, appointment and ETA remain visible. Native
  disclosures contain references, extended hours and cargo details. Enlarged
  mobile text gets one financial column; financial values remain server-owned.
- Compact Dispatch cards retain remaining distance and fuel-stop count; the six
  mileage/rate values are available in Details. Cards, Table and Papers share a
  stable heading and toolbar. Truck headers retain duty/rest and Next recap.
- Moving and stationary engine-on/Idle truck markers are green, distinguished by
  arrow/circle shape; engine-off stationary markers are gray. Markers are 28px,
  with no central status dot. Fuel points are 20px without inside price text.
  The selected next route stays bright while other routes are subdued, without
  widening them. Provider full-screen/camera/zoom buttons remain disabled.
- The retired Fuel stops settings section and Restore defaults action are gone.
  Application normalizes only hourly cost, reserve and fill to $35/hour, 25 US gal
  and 100%, including legacy settings. Reads do not rewrite stored JSON; saves
  validate first and preserve optimistic concurrency and other preferences.
  Two verbose address-confirmation messages receive concise UI presentation;
  validation and unrelated actionable errors remain intact.

## Verification

Final local artifact: `artifacts/compact-panels-final.yt6Zbr/publish/wwwroot`.

- `bash test.sh all`: 1,283 Server, 591 Client and 358 Node tests passed.
  Log: `artifacts/compact-panels-final-tests.log`.
- Strict API build and Client Release publish passed. Integrity verification
  checked 249 assets and seven JavaScript dependency graphs. The optional
  `wasm-tools` optimization workload was not installed.
- Offline staged UI: 44 pages across 12 width/theme/text-size profiles passed.
  Report: `Client/test-results/compact-panels-final-ui/report.json`.
- Fuel editor: four desktop/mobile/theme scenarios passed, including mouse/touch
  reorder, quantity/full-tank controls, focus callbacks and intercepted save/cancel.
  Report: `Client/test-results/compact-panels-final-fuel-editor/report.json`.
- Hours/map panels: ten scenarios passed. Native map element identity and exact
  rectangle stayed unchanged through cold selection, ready data, repeated
  selection, pending/replaced forecasts and clearing selection.
  Report: `Client/test-results/compact-panels-final-hours/report.json`.
- Stop details: five scenarios passed.
  Report: `Client/test-results/compact-panels-final-stop-details/report.json`.
- All final browser reports contained zero checked failures, browser errors and
  unexpected requests. Desktop/mobile screenshots were inspected, including
  enlarged text. A preceding renderer-only GPU check passed both pixel densities
  and eight route appearance probes; its report is
  `Client/test-results/map-next-selection-all-roads-verified/report.json`.

Manual verification in the authenticated local Google map measured the map at
1193×838, x=212/y=142 both before and after selecting truck 11005 and after
opening/closing its fuel editor. Heading, filters, green stationary marker and
the non-modal editor were visually inspected. No fuel plan or settings were
manually saved. The temporary browser viewport override was reset.

Local Client and API were rebuilt/restarted. API startup retained explicit
migration, synchronization and Gmail-maintenance disabling flags; liveness passed.
No production deployment or new migration was performed.

## Limits

Offline browser APIs and provider callbacks are fixtures; those runs do not prove
live routing accuracy, external integrations or production authentication. The
manual check is a small local-provider observation, not a full end-to-end audit.
PostgreSQL execution was not run because no safe isolated fixture was available;
no database server/container was started. SQLite regressions do not establish
PostgreSQL behavior. Production performance, long-running memory and GPU memory
were not measured. Passing tests do not prove every visual state is correct.
