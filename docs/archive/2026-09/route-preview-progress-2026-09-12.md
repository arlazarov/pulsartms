# Initial saved-route progress

The saved preview formerly returned geometry with null progress, so selection
painted and fitted the full road before live planning supplied its trimmed start.
RoutePreviewService now uses the existing ServerTelemetry or FleetTelemetryCache
snapshot and RouteDisplayCache exact geometry through RoutePlanningService.Progress.
The existing Client publication applies geometry and progress atomically.

There are no additional provider calls, telemetry fetches, writes, tracking
advances or schema changes. The normal matcher owns completed-stop minimums,
assignment/input checks and GPS freshness. Missing/stale telemetry retains the
existing full-road fallback; no progress is invented. The existing fleet-preview
30-second cache remains unchanged; per-truck reads match the current snapshot.

## Checks

- `bash test.sh routing fleet`: 762 Server and 380 Client tests passed, plus
  included JavaScript map and architecture checks.
- Four new server cases cover background/request telemetry caches, stale GPS,
  another truck, no provider calls and unchanged persisted tracking.
- The Client cold-preview component test verifies progress on the first geometry
  payload while live planning and Details remain held. Existing JavaScript tests
  assert no full-road paint or fit when progress accompanies the initial plan.
- Full `bash verify-release.sh` passed: 456 Node, 1,593 Server and 727 Client tests,
  strict build, 264 assets and seven dependency graphs.
  Artifact: `artifacts/managed/release-7Vy6Rp/publish/wwwroot`.
- The preceding full run timed out in an unchanged Next Loads polling test.
  That test passed individually and in the repeated full run; it was not weakened.
- Live browser/provider behavior and PostgreSQL were not tested. SQLite fixture
  tests do not establish PostgreSQL behavior. Performance was not measured.

## Publication

Deployed on September 12, 2026. Cloud Build
`e08e3ab5-e172-4399-afcb-464794c581c1` passed its full gate (727 Client and
1,593 Server tests) and produced image digest
`sha256:9fbaac78eca6a8c4ba4a3b332aed9edd0a538c9dd093eacb2050bfdb1d61bfcd`.
Cloud Run revision `amftms-api-00106-rhx` is Ready and serves 100% of traffic.
The preceding revision was `amftms-api-00105-7dc`.

Firebase Hosting published `release-7Vy6Rp/publish/wwwroot`, including the
compact-card width change. Live index, CSS, fleet-map entry module and Client
WASM hashes match the staged files on `https://tms.amfcarrier.com`.
Both the public site's and direct Cloud Run `/api/health/live` returned
200 Healthy. No new migration is required; authenticated database readiness,
production truck selection and upstream providers were not exercised.
