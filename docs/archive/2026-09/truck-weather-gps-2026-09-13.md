# Truck weather and GPS snapshot verification

Local changes on September 13, 2026. No application deployment or migration.

## Findings and changes

A bounded read-only checkpoint query showed three active trucks with GPS around
15:06 UTC while the checkpoint was updated at 17:48 UTC. A separate bounded
provider read returned fresh snapshot and stream observations around 17:53 UTC.
This distinguishes old saved application positions from missing provider GPS;
it does not establish every cause of a stale route in production.

The synchronization owner now copies high-frequency positions into checkpoint
vehicles while preserving feed cursors and sensor values. Followers can consume
these positions after the checkpoint is saved. Address absence no longer discards
newer coordinates. Retained positions cannot age a newer equal-time observation.
GPS validity thresholds and route mileage calculations were not relaxed.
The route renderer still requires confirmed progress to trim a saved route;
this change fixes a source of missing progress, not the route geometry algorithm.

Google Weather API was enabled in project `amftms` with user approval. A dedicated
Weather-only key was created and stored in local API user secrets, not source or
browser assets. Production secret configuration and API deployment remain pending.
A real bounded provider read returned valid metric current conditions.

Selected-truck weather is loaded independently, with a per-truck ten-minute
same-instance cache and a cancellable browser refresh. Google attribution is
visible. Personal units remain unchanged. Mobile telemetry icons are 32px;
desktop icons remain 28px. No landscape-hiding workaround was added: ordinary
iPhone browser tabs cannot reliably enforce an orientation lock.

## Verification

- Full `bash test.sh all`: 824 Client, 1,705 Server and 520 JavaScript tests passed.
- Strict Client and API builds passed with no warnings, including the final
  equal-time-observation guard. Both localhost processes were restarted.
- Fleet browser matrix: 12 cases passed, no browser errors or unexpected requests.
  Includes light/dark themes, responsive readings, scrolling and large text.
- Visual inspection of the 390px phone card confirmed the two-column grouping,
  weather icon and visible attribution. Browser data and map provider are fixtures.
- Live Google Weather transport succeeded. Authenticated production UI, real-device
  orientation and isolated PostgreSQL execution tests were not run.
- No production latency, memory or billed-usage improvement was measured.

Browser evidence: `artifacts/managed/browser-hours-forecast-Pdb84k`.
Staged UI artifact: `artifacts/managed/release-vsfVjv/publish/wwwroot`.
Moving the identical attribution style into its owning non-mobile module happened
after staging; the final strict local build contains that source organization.
