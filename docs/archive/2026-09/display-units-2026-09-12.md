# Fleet-wide display units — September 12, 2026

Settings → Display preferences now owns temperature (`fahrenheit`, `celsius`,
`both`) and distance (`miles`, `kilometers`, `both`), alongside the existing load
prefix. Both remains the default. The user explicitly requested a shared setting,
not a browser-local preference. Admin-only writes and authenticated reads keep
the existing revision/conflict boundary. Other sessions adopt changes after refresh.

Client formatters cover Outside, Fleet and Dispatch travel distances, load details,
route-choice distances/deltas, and current-stop/fuel-station distance labels.
Speed, MPG, rate-per-mile formulas and quote units retain their labeled bases.
No route/fuel recalculation or provider request is introduced by changing units.
Component and JavaScript regressions retain selection and geometry on unit changes.

## Verification

- `bash test.sh all`: 1,628 Server, 761 Client and 471 JavaScript tests passed.
- `npm run js:check --prefix Client`: passed.
- Strict Debug solution build and isolated strict Release Client publish passed.
- Staged Chrome UI smoke: 44 page checks in 12 cases passed, including native settings selects,
  both themes and 100%/200% text sizes.
- Staged Fleet inspector smoke: 10 cases passed across desktop/phone widths,
  exercising both-units, Celsius/kilometers and Fahrenheit/miles preferences.
- Staged route editor smoke: eight cases passed, including kilometer-only and
  dual-unit alternatives, small-screen bounds and intercepted save/cancel flows.
- Reports had no failures, unexpected requests or browser errors. Settings and
  the Celsius truck-inspector screenshots were visually inspected.
- Browser checks used deterministic API/map substitutes. Real provider rendering,
  real authenticated multi-user saves and isolated PostgreSQL execution tests were
  not run; application/production data was not used as a test fixture.

## Database and localhost

After explicit user approval, EF applied only
`20260912232808_AddDisplayUnits` to the configured cloud Neon database. The prior
migration history was read first: this was its sole pending migration. It adds
two required `varchar(16)` columns to `DispatchSettings`, each defaulting to `both`.
SQL generation and model-snapshot checks passed without connecting to PostgreSQL;
the actual approved migration application completed successfully. No migration
remains pending from this change.

Client and API were rebuilt and restarted on ports 5067 and 5086. Settings returns
HTTP 200; the unauthenticated settings API correctly returns 401. The website and
Cloud Run service were not published as part of this request.

Disposable evidence was recorded under managed runs `scratch-9zfq9e`,
`browser-ui-56pLb6`, `browser-hours-forecast-bJ1I7m` and
`browser-route-editor-QDx7o9`; retention may remove them.
