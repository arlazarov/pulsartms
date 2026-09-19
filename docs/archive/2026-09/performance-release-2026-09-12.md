# Same-server performance release — September 12, 2026

Published the working copy described in [the implementation record](performance-and-fuel-edit-2026-09-12.md)
to the existing `amftms` production project. The prepared `pulsartms` cloud project
was not activated; no DNS, database move or integration-owner cutover occurred.

## Deployment identities

- Cloud Build: `d70124f3-6824-4d70-bb62-ded11cebe930`, `SUCCESS`, finished
  `2026-09-12T13:47:23.401342Z`.
- API image: `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:63fb1baa8d6c0138cc000e9f1b9d6c3590fafaf71bd9b5fe298bb6241084f65d`.
- Cloud Run revision: `amftms-api-00105-7dc`, deployed with 100% of traffic.
  Previous ready revision: `amftms-api-00104-wp6`.
- Firebase Hosting: `amftms`, https://amftms.web.app, published all 264 files from
  the verified `release-xLhPC0/publish/wwwroot` artifact.

## Verification and first-attempt failure

The first Cloud Build, `c56bf017-9c67-44fb-98c5-48b5617614f0`, failed before
deployment. A visibility test released its held request before cancelling the
polling loop, allowing a legitimately due periodic tick to race its final
assertion. Teardown now cancels before releasing the request. Production polling
code and all assertions were unchanged; no test was skipped or weakened.

After that correction, the Fleet dependency group passed locally, followed by the
complete local release gate: 1,589 server, 718 Client and 439 Node tests. The second
Cloud Build passed the full gate independently, with no build warnings/errors.
Published asset verification checked 264 assets and seven JavaScript graphs.

The 79 HTML/JS/CSS/WASM files in the final local artifact matched the preceding
release artifact, which had also matched the previously browser-tested artifact.
The browser matrix was not rerun for this test-teardown-only change.

After publication, the Hosting index and stylesheet returned HTTP 200 and matched
the staged bytes; the index retained `Cache-Control: no-cache`. API liveness
returned `Healthy` both directly and through the Hosting rewrite. A revision-scoped
Cloud Logging query found no severity ERROR-or-higher entries at verification time.
This is a startup/release smoke check, not authenticated end-to-end or production
performance evidence.

No new migration was introduced for this performance release. No separate
PostgreSQL fixture, authenticated readiness test or migration-history inspection
was run; no production database was used as a disposable fixture. No Git commit
or push was performed.
