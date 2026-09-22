# Shared planning cache release, 2026-09-21

## Source and behavior

- `5b7d21c`: shared planning display snapshots, bounded route memory and
  post-commit publication.
- `ad249e9`: documentation formatting.
- `e998d2f`: company-scoped planning-input/profile rows cached per truck;
  overlapping reads batch only missing entries. Assignment changes notify both
  previous and receiving trucks. Shared settings, fleet catalog and UTC date
  remain common dependencies.

Read and publication ownership is documented in
[the maintained guide](../../architecture/fleet-efficiency.md).

## Verification

The final local `verify-release.sh` passed: 3,195 Server, 1,054 Client C# and
625 JavaScript tests. Strict builds reported zero warnings/errors. Published
Client verification checked 297 assets and 11 JavaScript dependency graphs.

Evidence: `artifacts/managed/diagnostic-IO6pvX/release.log` and the pinned
`artifacts/managed/release-L4vlyq/publish/wwwroot` artifact.

The initial new reassignment test failed because its synthetic truck external
IDs were duplicated. The fixture was corrected; its six-test class and the final
full gate passed. Existing routing checks passed before the full gate.

Local API was rebuilt from the final source against the existing isolated remote
PostgreSQL fixture. Map and board planning HTTP requests succeeded. The localhost
map reloaded and displayed TEST-001, its assignment, remaining distance and HOS.
No new CPU/RSS benchmark or authenticated production browser test was performed.
The opt-in automated browser/offline smoke checks were not run.

No new schema migration is introduced by the per-truck cache change. Production
route chunk/movement migrations had already been applied before this release.

## Publication

The superseded Cloud Build `99f9ab76-3048-4405-889f-c1b70b7810a4` was cancelled
before changing production traffic so the per-truck fix could join this release.

Final Cloud Build `8f044594-af64-46f5-98ea-293760598cc9` passed. Cloud Server
checks: 3,171 passed, 20 skipped; Client: 1,054 passed. PostgreSQL checks are
skipped there because no isolated fixture is configured. Two three-case
PostgreSQL theories count as two skipped tests, accounting for the four-case
count difference from the local run. The uploaded archive was checked for the
new batch cache and regression tests.

Cloud Run revision `amftms-api-b-8f044594-af64-46f5-98ea-293760598cc9` was verified
before routing 100% of traffic to it. Image digest:
`sha256:2ab0d647cb1652a9d77f39b488b80dcc711f6dc79d906f129e1b931873d3fa3b`.
Resources remain 512 MiB and one CPU. Evidence:
`artifacts/managed/diagnostic-CABvg8/deploy.log`.

Firebase Hosting successfully released the exact verified local Client artifact
at <https://amftms.web.app>. Evidence:
`artifacts/managed/diagnostic-wf65eW/hosting.log`.

Post-release verification found:

- `/api/health/live` returned HTTP 200 Healthy both directly and through Hosting.
- Cloud Run reported the new revision at 100% traffic.
- Hosted `index.html`, `css/main.css` and one generated JS chunk matched the
  published artifact byte-for-byte.
- HTML/CSS returned `no-cache`; the fingerprinted chunk returned immutable
  one-year caching.
- Anonymous `/api/health/ready` returned 401; authenticated readiness and
  production planning workflows were not checked through HTTP.
