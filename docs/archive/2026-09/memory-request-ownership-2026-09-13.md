# Memory and request ownership optimization

## Scope

This local pass targets Application's incremental GPS stream and the Client's
planning cache and selected-truck weather loop. It does not change route/fuel
calculations, GPS/HOS polling cadence, freshness bounds, database schemas, shared
cache sizes or provider query parameters. No production data was modified.

## Source findings and changes

- `FleetLocationStream` previously cloned each retained point when constructing
  its next rolling window. The retained points are privately owned, cloned at
  ingress and projected into separate response objects. Reusing those private
  references removes the repeated point clones without exposing mutable state.
  Corrections still replace the entry, and failed pagination does not publish a
  partial window. Case-insensitive tuple keys remove uppercase string allocations;
  one active-ID array is reused across pages within a read.
- `PlanningDisplayCache.Clear` previously discarded bookkeeping without stopping
  in-flight transport. A resettable cancellation lifetime now owns coalesced and
  superseded refreshes, explicit previews and bulk preloads. Reset/disposal cancels
  that work. Existing generation, request ownership and last-consumer guards stay
  in place. This does not promise that already-started server work is cancelled.
- Weather previously continued its retry/refresh loop while the page was hidden.
  It now uses the existing visibility subscription and waits for visibility or
  its existing deadline. A fresh reading does not refetch on every tab switch;
  an overdue one resumes promptly. Parent disposal cancels the child before the
  shared visibility resource is released. No new JS listener or polling service
  was added.

## Evidence and limits

These changes remove work identified by source inspection; no before/after heap,
working-set, latency, database-query or HTTP-count measurements were collected.
No percentage reduction or production performance improvement is claimed.
In particular, this pass does not reduce database queries or alter their indexes.

Regression sources cover reset/disposal transport cancellation, new-session
recovery, hidden weather/deadline behavior and case-insensitive GPS correction
with mutation isolation. Relevant categories are Fleet and Routing with their
dependent Architecture categories according to `docs/testing.md`. The user has
reserved test execution for an explicit request, so tests and browser checks were
not run. These changes have not been deployed and add no migrations.
Client and API builds completed with zero warnings/errors. The local Client was
restarted on port 5067; the API build used an isolated managed scratch directory
and did not replace the running API or publish a new server revision.

## Subsequent production publication

The operator requested publishing all ready changes. Firebase reported completion
for `artifacts/managed/release-VB5RJE/publish/wwwroot`. API Cloud Build
`c7e51043-cf38-49ba-a6e4-6fa569436ac0` succeeded using the existing Dockerfile and
a temporary build-only configuration; maintained release gates were unchanged.

Cloud Run reported revision `amftms-api-00112-m6p` serving 100 percent of traffic.
Image digest:
`sha256:721fa6b76032df24173d09ce7fe578b545d7dcb07323ba89e996406e757c7eb0`.

Tests, browser checks and post-deployment smoke/asset verification were not run.
The planned Switch operation and per-truck transfer-segment calculations remain
unimplemented. This release does not resolve the mixed-truck assignments on
AMF1377 and AMF1383 or change their stops, trailers or completion history.
