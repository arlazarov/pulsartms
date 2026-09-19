# Transfer road recovery — September 15, 2026

Follow-up to the [previous release's live limitation](full-review-release-2026-09-15.md).

## Cause and correction

Read-only production inspection found no saved native base routes for either side
of AMF1377 or AMF1383. The completed outgoing legs were excluded from preparation.
The incoming legs attempted to geocode a switch's descriptive site label despite
the native transfer already owning explicitly selected coordinates.

- Preparation and the Dispatch map now share ordered non-cancelled execution
  sections, including completed legs. The live assignment resolver still excludes
  completed legs; it was not relaxed.
- Native release/receipt locations resolve only through the exact non-cancelled
  participant/load/leg/visit binding and matching snapshot coordinates. Ordinary
  imported stops retain street-address verification. Base preparation, current
  planning and route-choice previews share this resolution.
- Missing map roads enqueue existing bounded, deduplicated preparation, including
  completed loads outside the normal scan horizon. HTTP map reads do not call
  routing providers. The native scan no longer hydrates live resource names,
  actuals and other display-only data to prepare static roads.
- Saved-road writes retain revision locking. Historical snapshots, assignments,
  actual transfer times and current-position plans are not overwritten. Separate
  sections do not create a fictional connecting road between release and receipt.

## Verification

- Affected Routing/Dispatch run: 624 Client, 1,081 Server, 12 Dispatch JavaScript
  and 47 JavaScript architecture tests passed before the final revision-race test.
- Final unfiltered automated suites: 550 JavaScript, 1,007 Client and 1,944 Server
  tests passed, with zero skips; strict build reported zero warnings/errors.
- Final local gate: `PULSARTMS_RELEASE_UI=1 bash verify-release.sh`, artifact
  `artifacts/managed/release-bUkeaZ/publish/wwwroot`. Integrity checks covered
  273 assets and seven entry graphs. Offline browser smoke passed 52 pages across
  12 cases: `artifacts/managed/browser-ui-w8pUhG/report.json`. The authenticated
  local browser gate was not run; no local API connected to production was started.
- Regressions cover both sides of a handoff occurring on different dates,
  completed-load demand, unchanged-scan reuse, unchanged live plans/history,
  changed transfer coordinates, cancelled/foreign transfer ownership and a
  concurrent assignment revision that must reject the old road and retry.
- Two earlier full-gate attempts timed out in the existing twelve-card Dispatch
  refresh test. It passed alone and in subsequent unfiltered runs; assertions and
  timeouts were not weakened. This intermittent test timing remains a limitation,
  not evidence of production performance.
- No schema migration is introduced. No isolated PostgreSQL execution fixture was
  available; PostgreSQL migration/concurrency tests were not run. Integration
  regressions use SQLite in memory; production diagnostics are read-only.

## Deployment and live verification

- Cloud Build `4de36be9-5bc2-4b2e-9ef0-5b36a7b57608` succeeded with all 3,501
  automated tests. Image digest:
  `sha256:a27d29c4ea53811b6c2aea94750fa604e82e0b1f9776a99576248d0acda46946`.
- Cloud Run `amftms-api-00119-dcs` is Ready/Active with 100% traffic. Previous
  revision `amftms-api-00118-x4j` is retired with TrafficShutDown. Direct liveness
  returned HTTP 200. No error-level entries were returned for the new revision
  in the initial post-release log check. Hosting files were unchanged.
- The new synchronization owner renewed its lease and wrote a checkpoint at
  05:08:53 UTC. Catalog, dispatch, planning, location, telemetry and assignment
  jobs advanced under the new owner with zero recorded failures.
- The background worker created all four missing road sections at 05:05:55–59 UTC:
  AMF1377 outgoing/incoming have 1,816/10,786 points; AMF1383 outgoing/incoming
  have 11,427/9,659 points. Every section has its complete one-leg geometry.
  A subsequent read retained the same calculation timestamps and point counts.
- All four execution snapshot checksums, assignment revisions and statuses match
  the read-only pre-release baseline. No manual production correction, SQL write,
  appointment change, document upload or migration was performed during diagnosis.
- Fresh authenticated production Dispatch pages for both loads no longer report
  unavailable road sections. AMF1377 also exposed all four Google map markers.
  This is saved-geometry and accessibility-state verification, not a pixel-level
  audit of the Google-rendered road or a proof of truck-access legality.
