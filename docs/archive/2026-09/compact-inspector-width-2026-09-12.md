# Compact truck inspector width

The compact Fleet Map truck inspector now uses a dedicated 64rem width cap
(1024px at the default root font), down from the expanded 76rem cap. Actions
remain beside HOS instead of consuming auto-margin space. The load column uses
content-driven width; destination retains flexible space. Type, clocks, data,
mobile width, map bounds and coordinated top/side gaps are unchanged.

## Verification

- `bash test.sh styles`: 53 Server and two Client architecture tests passed,
  plus Node styles and architecture checks.
- `bash verify-release.sh`: 456 Node, 1,589 Server and 727 Client tests passed;
  strict build and 264-asset integrity verification passed.
- Staged artifact: `artifacts/managed/release-FgipGU/publish/wwwroot`.
- Fleet-only browser matrix: ten cases passed, both themes at five widths,
  including expanded views and enlarged-text checks. The compact view asserts
  its smaller centered cap and adjacent action placement.
  Evidence: `artifacts/managed/browser-hours-forecast-1OJyYm`.
- Desktop and phone screenshots inspected. No authenticated provider checks or
  PostgreSQL fixture checks were run; no migrations or server changes.

Published to Firebase Hosting on September 12, 2026 with the
[route-preview progress release](route-preview-progress-2026-09-12.md#publication).
The live CSS and Client artifact hashes were verified against the staged release.
