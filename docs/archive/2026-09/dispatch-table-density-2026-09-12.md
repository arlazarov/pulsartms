# Dispatch table density — 2026-09-12

The table no longer stacks every pickup or delivery into one row. Each group shows
its first unfinished visit, or its last visit when complete. Groups with multiple
visits expose total/completed counts through a button opening the existing shared
load dialog. Original visit identities, order, appointments and completion remain
available there. Single-stop groups retain their normal summary. Financial values
and server calculations are unchanged.

The change belongs to the Dispatch page's Razor/code-behind and table styles. It
reuses `DispatchStopPresentation`, `DispatchBoardRow`, `DispatchLoadDialog`, shared
button controls and named spacing/type tokens. No new endpoint or request is needed.

## Verification

- `bash test.sh dispatch styles`: 580 server tests and 434 Client tests passed,
  with style and architecture JavaScript checks.
- `bash verify-release.sh`: 1,589 server, 723 Client and 451 Node tests passed;
  strict build and staged asset integrity succeeded.
- Offline browser UI smoke using installed Chrome: all 44 page cases passed at
  390/1440/2344px, light/dark themes and 100%/200% text. No reported geometry
  failures, browser errors or unexpected requests. Desktop and mobile five-stop
  screenshots were inspected. Mobile retains the table's horizontal scroll.
- Default Playwright Chromium was unavailable; the successful run used Chrome.
- `git diff --check` passed. No migration, deployment, live integration or
  PostgreSQL execution was performed.

Staged artifact: `artifacts/managed/release-82oGJs/publish/wwwroot`.
Browser evidence: `artifacts/managed/browser-ui-GEMGcE/report.json`.
