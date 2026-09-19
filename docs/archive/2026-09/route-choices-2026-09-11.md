# Load route choices — September 11, 2026

Implemented the approved alternative-route and manual via-point editor on Fleet
Map. The maintained behavior is in [route planning](../../features/route-planning.md).
Client changes are owned by the shared Routing/RouteEditor component, the Fleet
Map coordination partial, routing SCSS and the map's route editor module.
Business validation and persistence remain in Application, with TomTom and EF
configuration in Infrastructure.

## Verification

- `bash verify-release.sh`: 1,530 Server, 691 Client and 428 Node tests passed;
  strict builds and 252 published assets / seven JavaScript dependency graphs
  verified. Staged Client: `artifacts/managed/release-unPiQ5/publish/wwwroot`.
- After separating provider calculation time from the choice's save timestamp,
  `bash test.sh all` passed the same counts and the strict API build passed with
  zero warnings/errors. The staged Client was unchanged by that server-only fix.
- Route editor browser probe: eight cases at 1440/768/390/320px, light and dark,
  zero browser errors or unexpected requests. Report:
  `artifacts/managed/browser-route-editor-U6CAvh/report.json`.
- General offline UI probe: twelve scenarios, both themes and 100%/200% font
  scales, zero failures, browser errors or unexpected requests. Report:
  `artifacts/managed/browser-ui-rTIeAB/report.json`.
- `git diff --check` passed. Generated reports follow artifact retention.

Regressions cover provider truck restrictions and alternative cache validation,
via anchoring/collapse without extra service stops, preview ownership/expiry,
cross-service draft reads and bounded per-actor retention, stale saves,
completion retention, preserving the selected road after GPS reconnection,
future-road fuel invalidation, failure cooldown revisions, component selection,
explicit save/cancel, same-leg via ordering and map marker/listener cleanup.

Browser checks use the actual staged Blazor Client with intercepted fixture
APIs and an explicit map substitute. Native Google drag behavior, GPU picking,
live TomTom routes, real authentication and production performance were not
verified. SQLite checks do not prove PostgreSQL runtime/migration behavior; no
isolated PostgreSQL fixture was available or started.

## Deployment state

Deployed to production on September 11, 2026. Cloud Build
`71ec3237-7bd6-43a9-a9ae-bc82cb28bcfd` passed the complete gate and produced image
digest `sha256:febec158e56564f65bd819a6f164e6643e689edc4f8656e4b6cfbbae6b9df1af`.
Cloud Run revision `amftms-api-00103-5kz` became ready with 100% of traffic.
Startup logs confirmed application of migrations
`20260911222023_AddDispatchRouteChoices` and
`20260911224154_AddRouteChoicePreviews`. This is deployment evidence, not an
isolated PostgreSQL regression test.

Firebase Hosting published the verified `release-unPiQ5` Client artifact.
Public index, stylesheet and generated Fleet Map module SHA-256 hashes matched
that artifact. Both direct API and Hosting-rewritten liveness returned Healthy.
Existing localhost processes were not restarted during this production release.

The first Cloud Build (`f29c8d1a-7161-42e8-9c74-26cf230f21dc`) stopped before
deployment on a five-second timeout in
`FutureSelectionSurvivesAnUnchangedPollButClearsWhenTheLoadDisappearsOrNextLoadsAreHidden`.
The isolated scenario and all 691 Client tests passed locally afterward; the
second complete cloud gate passed 1,530 Server, 691 Client and 428 Node tests
without source changes, skipped tests or relaxed conditions. The intermittent
test timeout remains unexplained; the successful retry does not establish its cause.

The browser probe also found and fixed delayed enabling of Add while typing,
invalid Boolean ARIA rendering, a wrapping mobile close button, and horizontal
document movement at 320px caused by a body minimum wider than the viewport's
scrollbar-adjusted content area.

## Oversized preview hotfix

Truck 11006 / load 1382 received three valid provider alternatives, but draft
serialization duplicated their leg coordinates into aggregate point arrays and
exceeded the eight-MiB draft limit. Draft persistence now reuses RoutePlanStorage
to retain exact leg geometry once. The size limit is unchanged. Regressions cover
large alternatives, exact selected-road persistence and rejection of genuinely
oversized drafts without replacing the prior preview. Before publication,
`bash test.sh all` passed 1,532 Server, 691 Client and 428 Node tests.

At the user's explicit request, publication did not rerun tests. A temporary
build-only Cloud Build configuration left the maintained release gate unchanged.
Build `3d1049a8-4fef-4ace-8964-eeee147b4636` produced image
`sha256:f0abe9b0aa6cc27ca3162d9053aa67a8852c8d035be13aaae52e18d5e732180a`.
Cloud Run revision `amftms-api-00104-wp6` became ready with 100% of traffic;
Hosting-rewritten API liveness returned Healthy. Client and schema were unchanged.
The hotfix was not exercised through a live route-options request after deployment.
