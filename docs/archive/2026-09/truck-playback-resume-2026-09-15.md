# Truck playback visibility recovery — September 15, 2026

## Scope and cause

The user reported intermittent marker movement and accelerated playback after
hiding the browser while following truck 54777. Read-only production checkpoint
samples showed advancing GPS timestamps and speeds around 72–75 mph. Checkpoints
are persisted periodically, so these samples do not establish uninterrupted feed
delivery or frame-by-frame behavior on the user's device.

The Client retained its old playback clock while hidden. That clock advanced by
at most 250ms per render with a 1.1 catch-up multiplier, while pruning retained
GPS history against wall time. A later snapshot could therefore replace the
playback segment and animate a large geographic correction over six seconds.
The 75-second buffer could also run dry across a minute batch plus delivery and
polling jitter. Four new deterministic regressions failed before the fix.

The truck layer now rebases before paint on visibility restoration, a frame
suspension over one second, or history pruning beyond the playback cursor. It
clears correction transitions across that discontinuity and retains Follow's
screen anchor. Ordinary playback advances at 1x and never past available GPS.
The buffer is 90 seconds; this adds 15 seconds of display latency, not provider
requests. Missing telemetry still stops at the last measured point.

The release also includes the previously requested removal of redundant Trucks,
Trailers and Drivers links from Settings; Fleet retains those resource tabs.
No server logic, assignments, GPS records or database schema change is required.

## Verification

- `bash test.sh map` passed, including dependent Client/server architecture.
- Six deterministic lifecycle cases cover hidden snapshots, snapshots after
  return, frame suspension, batch jitter, real GPS exhaustion and hiding while
  already stopped at the last buffered point.
- `truckPlaybackSmoke.mjs` used the production source modules in isolated Chrome
  without Playwright focus emulation. Three cases passed: new data while hidden,
  new data after return, and browser minimization with an exhausted buffer.
  Hidden paint count was zero; resumed playback measured approximately 1x with
  Follow retained. Fixture time advanced by ten minutes per case.
- Browser evidence: `artifacts/managed/browser-map-startup-xnaEJM/report.json`.
  GPS and map-provider behavior in this probe are synthetic. This is not a live
  Google Maps GPU acceptance run or a production performance measurement.
- Earlier full-suite attempts hit the existing five-second publication wait in
  `DispatchBatchRefreshTests`. A diagnostic Client-only run passed all 1,008
  tests, and the twelve-card case took about three seconds. Its functional render
  wait was raised to 15 seconds to allow parallel-suite scheduling; request-count,
  snapshot, refresh and visibility assertions remain unchanged. No test is
  skipped, and no production Dispatch behavior was changed for this test.
- Dedicated PostgreSQL fixture checks were not run; no permitted isolated
  PostgreSQL fixture was available. No migration is introduced.

## Release

The final `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` completed successfully:
556 JavaScript, 1,008 Client and 1,944 server tests passed (3,508 total, no skips).
Strict builds reported zero warnings/errors. All 52 offline UI page checks passed;
report: `artifacts/managed/browser-ui-jUnN7v/report.json`.

The gate verified 273 published assets and seven module dependency graphs in
`artifacts/managed/release-uop7io/publish/wwwroot`. The exact staged artifact was
published successfully to Firebase Hosting project/site `amftms`.

Production responses from `https://tms.amfcarrier.com` matched the staged index,
Fleet Map entry and all its recursively imported modules, plus the Client WASM
(seven files). Entry HTML and unversioned modules returned `no-cache`; the
fingerprinted WASM returned one-year immutable caching.

- `index.html` SHA-256:
  `feb0790a967385fcbaecf2782935939c49a6d084321aff9f8208bb9efb127719`.
- `js/generated/fleetMap/fleetMap.js` SHA-256:
  `af66d3481eacededf2447b6c740b44237b1e7223c3ac739c2f574bd592aeac2f`.
- `Client.ax4nxmknhk.wasm` SHA-256:
  `ae518e085d93f7a46ad4191bc5a40d07971a35331b8d68743281d2ebf88eae11`.

A fresh authenticated production map for 54777 loaded its map canvas, actions,
route facts and telemetry: about 70 mph near Harrisonburg, compared with the
earlier Jolivue location. This accessibility/read check does not establish live
Google GPU animation correctness; the visibility regression uses the isolated
browser fixture described above. API revision and database migrations remain
unchanged.
