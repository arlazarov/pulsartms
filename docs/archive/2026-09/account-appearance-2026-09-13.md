# Account appearance — September 13, 2026

## Implementation

Settings offers Light/Dark to every authenticated account. Existing semantic
roles style the UI; this does not implement the separate generated design
mockups. Company integration, numbering, units and fuel settings remain Admin
only, including their existing server policies.

`GET/PUT api/settings/appearance` uses MediatR and the active profile resolved
through `ICurrentUser`. Requests accept no target user ID. `Users.Theme` stores
the validated preference with a Light default. The root Client provider restores
it before mounting account pages, saves explicit choices only, and rejects late
reads/saves after account changes. Failed saves preserve the confirmed theme;
failed reads keep the app usable and expose a retry. Logout restores Light.
There is no browser-wide preference, theme polling, push subscription or
routing/fuel recalculation. Other devices restore on sign-in or page reload.

The existing map host replaces its one retained Google map only when the
requested scheme changes, preserving the previous center/zoom. Ordinary
same-theme navigation still reuses that map. The native basemap scheme must be
chosen at construction, according to Google's
[map color scheme documentation](https://developers.google.com/maps/documentation/javascript/mapcolorscheme).
Provider-rendered visual behavior still requires a live check.

## Verification

- `bash test.sh all`: 819 Client C#, 1,673 Server C# and 513 JavaScript tests
  passed, including architecture checks.
- Strict Client build with isolated `artifacts/tests` output:
  no warnings/errors.
- JavaScript type check and JavaScript/SCSS compilation passed.
- EF reports no model changes beyond the prepared migration. This design-time
  check used an inert connection string; it did not execute SQL on a database.
- Local Release staging succeeded; no deployment command was executed. The
  toolchain reported the existing missing optional `wasm-tools` workload.
- SQLite integration tests verify persistence across fresh contexts, account
  isolation, Light defaults and inactive/missing/anonymous rejection. Validators
  and handlers reject unsupported themes.
- Component tests cover single pending saves, failed writes, successful theme
  application without remounting children, late account reads/saves, logout,
  retry after a failed initial read and non-Admin Settings ownership.
- Offline staged appearance smoke: desktop 1,440px and mobile 390px passed,
  including a fresh browser context restoring the first account's choice while
  a second account retains Light. No overflow; mobile buttons are 44px high.
- Offline staged page transitions: eight combinations of desktop/mobile,
  Light/Dark and normal/reduced motion passed. A duplicate Settings wrapper
  selector found during iteration was corrected by naming the Admin group.
- Appearance screenshots were inspected. These are synthetic authenticated API
  fixtures, not a live authentication or PostgreSQL end-to-end test.

Final staged artifact: `artifacts/managed/scratch-w6d6br/publish/wwwroot`.
Appearance evidence: `artifacts/managed/browser-ui-QXNgrW/report.json`.
Page transitions: `artifacts/managed/browser-ui-VpzM64/report.json`.

## Pending activation

`20260913135741_AddUserTheme` is prepared but **not applied**. It adds only a
non-null five-character `Users.Theme` column, defaulting existing rows to Light.
Do not apply it to the cloud application database without the user's approval.
The existing localhost API and Client were not restarted with this feature.

No isolated PostgreSQL fixture was available, so PostgreSQL migration execution
was not tested. Live Google dark maps, real multi-device authentication, a full
authenticated dark-theme page/dialog audit and production performance remain
unverified. No deployment or business-data writes were performed.
