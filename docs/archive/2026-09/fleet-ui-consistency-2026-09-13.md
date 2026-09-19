# Fleet UI consistency — local verification, 2026-09-13

## Changes

- Speed units inherit the number's font size, keeping regular unit weight. Order,
  total distance, facility and street values use the existing body token; labels
  remain secondary. No new typography scale was introduced.
- The phone title strip centers primary remaining distance without a visible
  Remaining label. Disclosure and Close share the opposite header group and retain
  their positions through loading, Details and Hide.
- One retained appointment row above ETA belongs to the tracked next stop. Exact
  dispatch/stop matches enrich missing preview appointments. Final delivery is not
  shown while the next visit is a pickup. The label slot has stable width.
- IFTA immediately precedes Fuel Stations, with shared chip styling and native,
  visually hidden checkbox semantics. Sidebar links reuse the existing action,
  typography, spacing and navigation tokens without the previous gradient/stripe.
- Future stop details show and copy the matching stop's imported PU # or DEL #.
  Missing references, unrelated IDs and driver-only stops cannot supply the value.
- Fuel, route and camera editors suppress the retained map inspector. Delayed
  marker callbacks cannot reopen it until the editor closes. Camera is mounted
  outside the hidden inspector; another editor cannot silently discard its draft.
- Editing invalidates cached Deck renderer instances, not retained road geometry.
  Cancel restores a fresh renderer for the unchanged saved road.

## Checks

- `bash test.sh all`: 780 Client, 1,662 Server and 482 JavaScript tests passed.
- After the final appointment-label adjustment, `bash test.sh fleet styles`
  passed the affected/dependent categories and architecture checks (237 Client,
  312 Server, plus the runner's map, styles and architecture JavaScript suites).
- Strict Client Debug build and staged Release publish passed. Artifact verification
  checked 264 assets and seven JavaScript entry-point dependency graphs.
- Offline staged toolbar matrix: 16 cases, both themes, four widths and 100%/200%
  root text. Brand/sidebar matrix: 35 cases, no reported errors or clipping.
- Offline staged Fleet inspector: 12 cases, six widths in both themes, including
  stable loading geometry, disclosure, measured typography and enlarged-text probes.
- Offline route editor: eight cases, including Cancel without a save, restoration
  of the inspector and unchanged map bounds. Offline fuel/camera: one
  390×844 light-theme case passed with inspector suppression and restoration.
- Real offline GPU road checks passed at DPR 1 and 2, including three consecutive
  edit/cancel cycles with no geometry republication and a positive road-pixel check.
- Local Client restarted; `GET http://localhost:5067/fleet/map` returned 200.

Final inspector/editor artifact: `artifacts/managed/scratch-QIyihW/publish`.
Reports use managed runs `browser-hours-forecast-Tf9crN`,
`browser-route-editor-faKR27`, `browser-fuel-editor-cKzyPk`,
`browser-map-markers-nCpugc`, `browser-ui-pHK0cO` and `browser-ui-erHUHO`.
Toolbar/sidebar reports precede the final appointment-label-only adjustment.
These ignored generated artifacts remain subject to retention.

## Limitations

No deployment, migration, database test fixture, live provider request or business
write was performed by these checks. Browser fixtures intercept APIs and do not
validate production calculation accuracy, authentication or performance.
The complete fuel-editor responsive matrix was not certified: an earlier 390×667
case exposed insufficient timeline height in the short-map fixture; the final
targeted fuel/camera case covers only 390×844. The user's existing browser tab
could not be reloaded automatically because its control runtime failed to start.
