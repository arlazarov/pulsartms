# Map marker visibility and zoom follow-up — September 9, 2026

## Changes

Current and future stops use fixed 30px circles with a stronger white outline and
centered 13px digits. Per-load colors are unchanged. Each future stop occurrence
now has its own marker and exact selection callback, including repeat visits at
the same address. Coincident markers use 36px screen-space spacing without moving
geographic anchors. The circle uses a small prepacked square icon, not a background
whose dimensions depend on text. Fuel price colors and recommendation rings are
unchanged; fuel visit numbers remain inside popup cards.

Current, future and empty-route strokes use a 5px minimum and the shared 1.75 width
multiplier (previously 1.25), with a 2.5px total white outline. Existing selection ordering
still draws stations and stop markers above route lines.

At the user's follow-up request, the automatic roadmap/hybrid switch at zoom 15
was restored: camera idle selects hybrid at 15 or greater and roadmap below 15,
without reapplying an unchanged map type. Equivalent route
detail levels no longer republish identical geometry, and zoom 14 or greater
reuses the full-detail level. Genuine detail changes preserve progress and stop
anchors; line-width updates remain independent. This removes redundant path
updates, not proof that all provider zoom stutter
has been eliminated.

## Checks

The release checks below preceded the final map-type restoration. Its focused
interop regression verifies the exact threshold, idle-only changes, no redundant
type replacements and listener disposal; final combined release checks are separate.

- Final `bash test.sh map`: 88 Server, 178 Client C#, 187 map JavaScript and
  18 JavaScript architecture tests passed (471 total). This was a category run,
  not the full suite.
- JavaScript typecheck and strict Client build passed with no warnings or errors.
- Offline browser smoke: 40 cases passed with no failures, browser errors or
  unexpected requests. Evidence is under ignored
  `Client/test-results/stop-circles-30-routes-5-final-20260909`.
  Pixel checks cover equal circle dimensions for single- and double-digit pickup
  and delivery numbers at device densities 1 and 2. Full map screenshots confirm
  thicker routes do not cover the circles or fuel markers.
- The real GPU fixture caught a packed-icon accessor mistake before completion;
  the corrected callback and a regression check ensure the circle atlas is drawn.
- The Client was rebuilt and restarted on localhost. HTTP integrity matched
  22 generated assets for both identity and gzip requests (44 checks).

No server code, database migration or production deployment was part of this
follow-up. No PostgreSQL fixture, production performance benchmark or long-running
Google Maps zoom measurement was performed. The offline projection does not prove
live geographic rendering or smoothness on every device.
