# Dispatch stop views and stable truck clusters

## Changes

- More details selects one full-width stop inside the existing load dialog.
  All stops restores overview scroll and keyboard focus. Stop identity and
  hidden forecast content survive polling; explicit scroll restoration disables
  browser scroll anchoring in this dialog only.
- Completed visits use numbered city/check/scheduled-appointment rows. Full
  addresses and other facts remain in Details. Single-load lanes use available
  width, with completed history beside remaining work on wide cards and above
  it on narrow cards. Enlarged text wraps without a fixed row height.
- Truck cluster labels retain their offset while group membership is unchanged.
  One mouse or touch activation selects the first zoom that separates the group,
  bounded by the existing cluster maximum. The geographic center remains marked.
- Includes the previously local compact Fleet inspector load/order, scheduled
  final delivery and shared HOS dials. No new API calls or financial formulas.

## Verification

- `bash verify-release.sh`: 456 Node, 1,589 Server and 727 Client tests passed;
  strict solution build had no warnings or errors. Verified 264 staged assets
  and seven JavaScript dependency graphs.
- Final artifact: `artifacts/managed/release-nW5DUa/publish/wwwroot`.
- Offline UI: `browser-ui-GXrjDB`, all 44 page cases passed, including both
  themes, 100%/200% root fonts, stop-view return focus/scroll and the four-completed
  pickup fixture. Desktop and phone screenshots were inspected.
- GPU markers: `browser-map-markers-eaCj9n`, two mouse/touch cases passed.
- HOS/forecast matrix: `browser-hours-forecast-qr2x7h`, ten cases passed against
  the preceding `release-JG7Lqs` artifact. The subsequent production change was
  dialog scroll anchoring, exercised by the final offline UI matrix.
- The added scroll-anchor style contract passed separately after the full gate.
- Earlier browser runs identified outdated full-detail expectations for compact
  history and a real scroll-anchor conflict; those runs were not release approval.
- No isolated PostgreSQL fixture was available. PostgreSQL and authenticated
  live-provider checks were not run. No server, schema or migration changes are
  required for this client-only release. Performance was not measured.

## Publication

Firebase Hosting publication of the exact verified artifact completed successfully
for project `amftms`. SHA-256 comparisons on `https://tms.amfcarrier.com` matched
the staged index, main CSS, generated load-dialog and fleet-map entry modules,
and fingerprinted Client WASM. The Cloud Run API and database were not redeployed.
