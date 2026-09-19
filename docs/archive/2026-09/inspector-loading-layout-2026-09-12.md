# Truck inspector loading layout

## Scope

Client-only Fleet Map presentation. No API, persistence, route calculations or
deployment changes.

- Loading and ready route details share one retained panel, group tree and
  stop-scoped ArrivalEstimate instance.
- Load/order/total use assigned grid slots instead of wrapping according to the
  temporary reference text. Narrow inspectors retain three metadata rows.
- The neutral ETA/cycle placeholder follows the ready forecast's row hierarchy.
- Desktop scrollbar space remains stable; phones retain their full content width.

## Verification

- `bash test.sh fleet styles`: 294 Server, 222 Client and 461 Node checks passed,
  including architecture checks. This is not a full-suite run.
- Strict Client build and isolated Release publish completed without errors.
- Browser evidence: `artifacts/managed/browser-hours-forecast-PvzLy8/report.json`.
  All 12 light/dark cases passed, including phone and intermediate widths,
  retained DOM identity, delayed preview/reference/live planning, overflow and
  enlarged text. The preview fixture excludes HOS/ETA, matching the saved-preview
  response rather than prematurely supplying a full forecast.
- The 390px light case measured the same 291.75px route-detail height during
  loading, preview and ready states.

Browser checks use isolated API/map fixtures. They do not prove live provider,
authentication or arbitrary real-address layout behavior. Full suite and database
checks were not run for this presentation-only change. Nothing was deployed.
