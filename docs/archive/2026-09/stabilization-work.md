# Stabilization implementation tracker

Approved scope: the full-project audit and the subsequent SCSS, JavaScript, server,
and test-structure recommendations. Preserve existing behavior except for documented
bug fixes. Source backup: `/Users/antonarlazarov/Developer/amftms-stabilization-backup.USo9ux/source-before.tar.gz`.
This is a source backup, not a database backup or a deployed-artifact snapshot.

## Workstreams

- Implemented: separate root-level `Server.Tests` and `Client.Tests` projects,
  referencing real production code, feature/Kind categories, matching namespaces,
  shared fixtures, bUnit component regressions and shared test selection.
- Implemented: Application feature ownership, profile/plan persistence boundaries,
  assignment/address/fuel fixes, bounded batch predecessor reads and durable Gmail
  watch renewal/recovery after explicit registration.
- Implemented: pure JS preparation, targeted type checks, stable stop-layer identity,
  same-version stop metadata updates and acknowledged geometry-free interop polling.
- Implemented: SCSS public API, palette-derived semantic themes, global/page ownership,
  removed legacy styles/hidden duplicate station-close control, common PageHeader.
- Implemented: view-safe Dispatch polling, cancelled-request cleanup, bounded ETA
  freshness, truck visibility synchronization and unchanged next-route error recovery.
- Implemented: conservative provider-attempt accounting, role uniqueness guard,
  secret-free artifact gate, quiet polling and immutable deploy identity.
- Applied and verified: the single additive role-index migration on the pinned,
  configured `neondb` database, after private backup and reviewed SQL.
- Implemented: session-bound retries/logout and origin-wide locked storage, with
  C# regressions and isolated real-Chrome cross-tab assertions.
- Implemented: exact staged-artifact release gate and offline WASM UI coverage.
- Implemented and independently reviewed: durable station-lookup retry ownership
  and latest-prepared revision validation (A13), verified by the 393-test Server run.

## Verification snapshots

- Integrated stabilization gate, before the temporary budget override below, passed
  393 Server, 70 Client C#, and 99 Node tests (562 total), strict solution
  build/publish, 222 staged asset hashes, six JavaScript dependency graphs and
  all 40 offline UI cases. Verified artifact:
  `artifacts/release.yd3hDz/publish/wwwroot`. The named test projects are peers
  at the repository and solution root: `Server.Tests` and `Client.Tests`.
- Follow-up full Server run passed 393 tests with warnings treated as errors and
  isolated output after A13 and source-enumeration changes. It includes pre-call
  reservation, restart/rollback persistence, exception/cancellation cooldown,
  deferred corrected queries, no provider work inside import transactions, and
  both application orders for stale prepared results. Independent source review
  found no remaining A13 blocker. No extra migration or live provider call was used.
- Follow-up Client run passed 70 tests after the delayed Next Loads component
  matrix and final session-pinning guard; both are included in the final gate.
- The offline smoke passed all 40 actual-WASM page cases across desktop/mobile,
  both themes and 100%/200% root font sizes, with deterministic API fixtures and a
  Google map module stub. It reported no checked geometry failures, browser errors
  or unexpected requests. It is not real-map/GPU acceptance or browser zoom testing.
- Isolated real Chrome Web Locks checks passed six cross-tab assertions. Component
  regressions cover session changes, delayed board views, ETA freshness, visibility,
  next-load transient errors and delayed selection/toggle ordering.
- `test.sh all` also passed the 384/64/99 baseline using isolated `artifacts/tests`
  output, leaving the Debug Client static-web-assets manifest hash unchanged.
- Only the Client was restarted after a strict Debug build. Anonymous Chrome booted
  Login without page/request errors or framework 404s. An existing authenticated
  browser rendered the real Google map, truck 54777's current route and Dispatch.
  Future-route geometry and the full map/GPU scenario matrix were not verified.
- After the final gate, both local processes were restarted from a fresh strict
  Debug solution build. API liveness on 5086 and Client on 5067 returned HTTP 200;
  the reloaded authenticated Dispatch page rendered without browser errors.
  Synchronization remains disabled by the existing Development configuration.
  Ordinary telemetry reads occurred during this live check; it is separate from
  the offline, provider-free gate. No remote deployment was performed.
- Clean API publish rejected no filenames and pending-model check found no drift.
- Browser/GPU performance, real provider lifecycle and production latency are not
  established by these counts. Historical browser figures remain historical.

## Temporary route-budget override, 2026-09-08

- Explicit operator request: disable the truck-specific reroute budget while
  retaining the shared TomTom ceiling of 1000 attempts per UTC calendar day and
  30 per minute. API configuration now sets `RouteRecalculationBudget:Enabled=false`;
  absent configuration still defaults to enabled.
- Disabled reservations honor cancellation and skip the truck-budget gate and
  database access. Prior attempt history, provider accounting, caching,
  deduplication and sustained-deviation checks are unchanged. No migration or
  deliberate business-data edit was required.
- Independent source review passed. `bash test.sh all` passed 396 Server, 70
  Client C# and 99 Node tests (565 total). The strict API Debug build passed with
  no warnings. New regressions cover configuration binding, no database access
  while disabled and retained history after re-enabling.
- Restarted the local API with the new build; API liveness on 5086 and the
  unchanged Client on 5067 returned HTTP 200. Restart also clears the automatic
  planning error's two-minute memory cache. No forced route calculation was
  used for verification; open clients resumed ordinary telemetry reads.
- This override has not been remotely deployed. The earlier staged UI/release
  artifact is the stabilization baseline and does not contain this follow-up.

## Next Loads selection-latency follow-up, 2026-09-08

- User-reported regression: enabled-before-selection waited for current planning
  and details, while A→B→A discarded the future-route revision and geometry.
  Earlier delayed-response tests proved stale-response rejection, not immediate
  restoration before another HTTP response.
- Implemented a component-owned, truck/current-dispatch keyed display cache
  (12 entries, eight MiB, five-minute expiry), complete geometry/labels replay,
  concurrent next-route reads once current identity is known, and removal of the
  trailing duplicate selection request. Version checks after awaited clearing
  prevent an obsolete null-identity continuation from clearing a newer selection.
- `bash test.sh fleet` passed 94 Server, 34 Client C#, 63 map JS and 18 JS
  architecture checks (209 total). Seven added cases exercise pending planning
  and details, cold identity without duplicate fetch, immediate cached replay
  including labels-only and empty results, plus cache TTL/byte/entry boundaries.
  This is the affected/dependent-category run, not a new full-suite release gate.
- Strict Client Debug build passed with zero warnings. Restarted the local Client
  and reloaded the authenticated browser. Checked Next Loads enabled before truck
  selection, 54777→11005→54777, and off/on: real future geometry/numbered stops
  restored and the current road remained visible when future routes were hidden.
  The browser error log was empty. HTTP-independent replay is verified by the
  deferred component tests; the live check is not a timed latency benchmark.
- No server source or DB changes and no remote deployment. The Next Loads endpoint
  reads saved routes rather than calling TomTom. A truly cold current-dispatch
  identity still needs an authoritative server response; missing prepared geometry
  still depends on background preparation. Server query batching and long-run
  browser/GPU measurements remain separate work.

## Saved first display and server-cost review, 2026-09-08

- Added an authenticated, per-truck saved-preview read. It resolves current
  assignment and validates saved geometry against current stop inputs/effective
  profile without HOS, telemetry, ETA, providers, background enqueue or DB writes.
  The first unsaved remaining load retains its identity rather than yielding to
  a later saved route. Preview fuel recommendations are omitted.
- Cold automatic selection uses this optional preview with a two-second fallback.
  Next Loads can start before live planning; known geometry is reused in the
  subsequent planning request. Warm selection still restores cached geometry.
- Fixed the fleet-preview shared-stripe nesting hazard with a separate gate and
  a single clone-safe, eight-MiB/30-second entry. Date and read generations,
  including committed route/profile updates, invalidate it. Current assignment
  resolution is shared, with batched fleet input reads rather than per-load
  fallback assignment queries.
- Added Client guards for cancellation, disposal, session clear, newer same-key
  plan/no-plan responses and both fleet/direct-preview response orders. Optional
  bulk preload tracks at most 256 written URLs for its active lifetime only;
  overflow discards that snapshot instead of retaining unbounded tombstones.
- Final `bash test.sh all` passed 409 Server, 97 Client C# and 99 Node tests
  (605 total), with no skips. The preceding strict solution build passed with
  zero warnings; the final Client-only guard also passed a separate strict build
  with zero warnings.
  Server preview fixtures observe seven cold/one warm SQLite SELECTs and reject
  provider/HOS/telemetry calls and writes. These are fixture counts, not measured
  production query latency.
- Restarted the local API/Client and reloaded the authenticated in-app browser.
  Checked Next Loads enabled before selecting 54777, 54777→11005→54777, and
  off/on. Current blue geometry and future purple geometry/stops restored;
  hiding future loads retained the current road. The browser error log was empty.
  API liveness returned 200; the new preview endpoint returned 401 anonymously.
  This narrow-viewport interaction check is not a timed HTTP/GPU benchmark or
  a complete desktop/mobile visual audit. The local app remains running.
- Additional source-based opportunities and safeguards are listed in
  [the server optimization review](server-optimization-2026-09-08.md). Those
  follow-ups are proposals, not implemented cost savings. No DB schema/business
  mutation or remote deployment is part of this follow-up.

## Audit acceptance reconciliation

The [original audit](full-project-audit-2026-09-07.md) remains a historical,
pre-remediation report. The rows below reconcile all 25 findings against current
implementation and named executable checks. "Implemented" describes code, not
unmeasured production performance or external operational activation.

| Finding | Implemented change and regression evidence | Acceptance boundary |
| --- | --- | --- |
| A01: credential packaging | API content exclusions and fail-fast publish guard; `ArtifactPackagingTests`, clean API publish. | Existing artifact distribution and credential rotation are operator reviews; no compromise was established. |
| A02: split assignments | Whole-load truck validation and stop-assignment signatures; `RoutePlanningServiceTests` and next-load selection checks. | Ambiguous loads are rejected; split-load routing was not invented. |
| A03: verified coordinates | Unchanged source preserves the verified address/point; changed input invalidates verification; `StopAddressServiceTests`. | No live address/provider repair performed. |
| A04: attempted-call accounting | Commit quota reservation before HTTP; malformed/cancelled/interrupted attempt regressions in `TomTomAccountingTests`. | Fake HTTP/SQLite only; live billing reconciliation and multi-replica contention remain unmeasured. |
| A05: account-crossing retries | Stable login identity, session-bound HTTP, atomic Web Locks storage; `ClientRefreshTests`, `SessionLifecycleTests`, `authStorage.test.js`, six real-Chrome assertions. | Older open tabs need reload; no real-account mutation was used to test a race. |
| A06: duplicate roles | Serialized role assignment, conservative duplicate reads and filtered uniqueness; `UserRoleTests`; approved migration applied and verified below. | Empty-table live metadata checks are not PostgreSQL multiwriter/load tests; legacy no-role Admin compatibility remains deliberate. |
| A07: board polling | One captured query/view identity; `DispatchBoardPollingTests` exercises Table/Papers polling, delayed view changes and superseded search cleanup. | Controlled clock/HTTP fixtures, not a production outage test. |
| A08: stale ETA | One bounded freshness/retention policy; `ArrivalDisplayMemoryTests` and `ArrivalEstimateComponentTests`. | Expired values do not retain an On time claim; live ETA accuracy remains provider-dependent. |
| A09: same-version metadata | Metadata/tracking refresh independent of roads; `routeLayer.test.js`, `routePayload.test.js`, interop acknowledgement checks. | Fixture verifies geometry reuse; live provider calls are unnecessary for text changes. |
| A10: hidden searched truck | Search routes visibility through the renderer owner; `FleetMapComponentTests` checks toggle, visible state and focus. | Real map rendered after Client restart; exhaustive provider interaction is not claimed. |
| A11: preview truncation | Preview reads all board pages without calculating routes; `RoutePreviewTests` places an assigned truck after 100 empty rows. | Complete-board projection remains a separate potential optimization. |
| A12: onward pickup location | Shared full-address/verified-point resolution; `FuelRegionPlannerTests` covers missing and coarse imported points. | No live geocoding performed. |
| A13: unresolved stations | Durable signature-keyed attempts and cross-instance lease; prepare before import transaction, defer pending/failure states without consuming email, preserve unchanged points and re-resolve corrected input. Lifecycle/store/import regressions passed in the 393-test Server run. | Latest prepared revision wins in either application order while historical prices import; no email-chronology inference. Existing schema, no live provider or multi-replica load test. |
| A14: lifetime history reads | `IDeadheadHistoryReader` bounds candidates to two and uses one parameterized PostgreSQL LATERAL batch; `DeadheadHistoryReaderTests` verifies translation and history/tie equivalence. | SQLite retains a per-load fallback; PostgreSQL plans, allocations under load and p95 latency were not measured. |
| A15: stop-layer invalidation | Stable stop IDs/data and paired opaque circle/number ordering; `stopLayers.test.js`, `gpuScene.test.js`, stop appearance/data checks. | Real overlapping markers, future/current priority and hover promotion still need the full live map matrix; no FPS/VRAM claim. |
| A16: bridge geometry transfer | Acknowledged geometry revision and metadata-only polling; `MapRoutePublisherTests`, `fleetInterop.test.js`, HTTP geometry tests. A 10,000-point fixture sends more than 400 KB initially and less than 1 KB unchanged. | Fixture byte bounds are not production latency measurements; renderer reset/mismatch recovery is tested. |
| A17: noisy polling | Metrics remain; fast normal polls and duplicate failure timing logs removed; `RequestDiagnosticsTests`. | Production log volume was not measured. |
| A18: mutable deployment tag | Unique build identity and validated returned image digest; stubbed `releaseGate.test.js` rejects wrong/failed/missing artifact identities. | Cloud Build/Cloud Run and overlapping real deployments were not executed. |
| A19: Gmail watch lifecycle | Explicit Admin registration, durable expiry/lease/retry and two-hour catch-up; `GmailWatchLifecycleTests`, `GmailWatchStoreTests`. | Activation, OAuth/Pub/Sub ownership and idle-host execution remain operator tasks; no registration performed, recovery window remains two days. |
| A20: duplicate palette values | Themes derive semantic roles from palette primitives; `styleTokens.test.js` checks contract and contrast. | Components continue to use semantic roles; not all visual combinations are covered. |
| A21: dead truck CSS | Removed unused DOM selector families/partials and updated renderer ownership. Style and architecture guards plus staged UI smoke pass. | No significant performance gain or full live-map visual certification is claimed. |
| A22: heading alignment | Shared PageHeader outside constrained bodies; staged actual-WASM desktop/mobile/theme/font-size checks. | Native browser zoom and every page/state are not covered by the 40-case matrix. |
| A23: behavior coverage | Real Client assembly, bUnit delayed HTTP/clock tests, Next Loads on/off/on and A→B→A tests, executable JS numbering/highlight/current-load guards. | Full live GPU overlap/hover matrix and PostgreSQL multiwriter experiments remain external safe-target work, not source-pattern proof. |
| A24: unenforced release gate | Shared strict gate for deploy/CI, isolated test output, exact staged asset/import validation, stubbed fail-stop deployment regressions. | Final local gate passed; actual cloud deployment was not performed. |
| A25: stale next-load error | Successful unchanged response clears errors without sending geometry; `FleetMapComponentTests`. | Cached routes remain visible; covered with controlled HTTP failures. |

## Remaining external verification and measurements

- Operations owner: coordinate existing Gmail renewal ownership before explicit
  registration; verify credentials, topic/label, authenticated push and background
  hosting. Repair outages older than the two-day import window deliberately.
- Release owner: review previously distributed artifacts and secret rotation needs;
  preserve rollback images and verify deployed identity only when deployment is
  authorized. This work did not rotate credentials or deploy.
- UI owner: verify future geometry, opaque overlaps, pickup/delivery distinction,
  label centering and hover priority in the live map. Run actual zoom and long
  browser/GPU memory/frame-time probes separately from the map-stub smoke.
- Database/operations owner: use a disposable PostgreSQL target for concurrency and
  query-plan/load measurements; compare p95, lock waits, allocations and provider
  attempt reconciliation before numerical speed or zero-leak claims. No shared-DB
  mutation/load experiment was used for this purpose.
- Recovery owner: verify a full restore and backup/key-table access controls.
  Data Protection XML encryption at rest and deliberate legacy Admin-role migration
  remain pre-existing operational decisions, not silently changed behavior.

## Database operation, 2026-09-08

With explicit approval, applied only `20260908034738_EnforceUniqueApplicationRoles`
to `neondb` at `ep-blue-cherry-aefp3o1q-pooler.c-2.us-east-2.aws.neon.tech:5432`,
using the configured Development User Secrets target. Cloud Run configuration
equivalence was not checked. Read-only checks before and after confirmed the
20-to-21 migration transition, unchanged zero claim/role/duplicate counts, and the
new index's uniqueness, validity, and exact predicate. Existing indexes remain.
Execution used one transaction with 5-second lock and 60-second statement timeouts.

Private custom-format database backup:
`/Users/antonarlazarov/Developer/amftms-db-backup.YEV95x/neondb-before-20260908034738-verified.dump`.
Directory/archive permissions are 0700/0600; size is 108,674,407 bytes.
SHA-256: `16f106b572e9a11ecb5c01776fe59f8c680b2fc0a395ba834f4eaa4a8fd9a860`.
`pg_restore --list` passed with 166 entries; no full restore was tested. See
[security rollout](../../operations/security-rollout.md) for SQL and verification details. These
checks do not measure loaded PostgreSQL concurrency or production performance.
No other database changes, provider calls, Gmail registration, or deployment were
performed by this operation.

## Safety and completion

The user authorized necessary database changes. Do not treat this as permission to
run unbounded provider calls, destructive test fixtures, or load tests on shared
production data. Any applied migration needs a reviewed target and recovery plan.
Keep recorded findings separate from measured production impact. Mark unverified
external/runtime requirements explicitly; a green unit suite is not visual or
performance certification. No deployment has been performed for this workstream.

## Approved server optimization and release follow-up, 2026-09-08

The user approved the remaining improvements in
`server-optimization-2026-09-08.md`, configuration-registration cleanup, and
deployment of both API and Client to the existing Cloud Run/Firebase targets.
This section supersedes the earlier no-deployment statements only when the final
deployment results are recorded below; historical verification snapshots remain
unchanged.

- Grouped Application options binding/startup validation in API's composition
  root. Policy types remain in Application, adapter registrations in Infrastructure.
- Added fresh managed-address reuse, saved complete base/onward-route reuse,
  metadata-only Next Loads database reads and planning-display deserialization.
- Added bounded preparation demand, repair horizons, retry preservation and
  targeted invalidation for assignment changes, corrected predecessor addresses
  and profiles changed during an in-flight calculation.
- Shared safe saved-route reads and recovery for missing/corrupt deadhead geometry;
  same-input financial mileage remains available during a budgeted repair.
- Removed global unrelated-truck/driver serialization where safe using separate
  bounded owner-local gate stripes; retained TomTom's durable shared attempt budget.
- Preserved HOS failure availability semantics while retaining its immutable
  recovery baseline; shared the driver catalog and added bounded incremental
  telemetry with periodic full reconciliation.
- Verified actual Npgsql reader execution on a disposable local PostgreSQL 17:
  21 assertions, including missing geometry, ambiguity and inserted predecessors.
  The temporary container/database was removed; no application database was used.
  This did not measure loaded query plans or production latency.
- Managed-allocation fixture: matching display metadata allocated 2,680 bytes
  versus 117,680 bytes for the full curved display fixture. This is one cache read,
  not an end-to-end speed or billing claim.

Source backup for this follow-up:
`/Users/antonarlazarov/Developer/amftms-server-optimization.ruGVMl/source-before.tar.gz`.
The prior UI report and synthetic PostgreSQL harness are retained in that private
directory. The configured TomTom limits remain 1,000 attempts/day and 30/minute;
the separate truck recalculation budget remains disabled. No new schema migration
is required by this follow-up.

Pre-release Cloud Run rollback identity: `amftms-api-00076-r6l`, image digest
`sha256:3e51b3a7a17ab8513bb2477b28d09d85a6638b503e15b7dd144f8107e94f85f8`.
The existing Cloud Run connection target was checked against the previously
approved Neon migration target; host/database match. Credentials were not logged.
Release verification passed: 514 Server, 97 Client C# and 99 Node tests (710 total,
no skips), strict Release build/publish with zero warnings/errors, 222 staged
asset hashes and six JavaScript dependency graphs. All 40 offline UI cases passed
with no reported browser errors, unexpected requests or geometry-check failures.
The verified artifact is `artifacts/release.0xVQ3N/publish/wwwroot`. The optional
native `wasm-tools` optimization workload was not installed.

A strict Debug solution build also passed after stopping the two owned local
servers; both were restarted successfully. API liveness returned `200 Healthy`.
The authenticated in-app browser reloaded the new Client and rendered the map.
Next Loads enabled before 54777 selection, off/on, and 54777→11005→54777 restored
the current and future roads; disabling future loads retained the current route.
The browser error log was empty. This was a narrow-viewport interaction check,
not a new long-running map/GPU soak or an end-to-end latency measurement. Ordinary
app/background behavior was active; no manual forced provider calculation was used.

`bash deploy-server.sh` completed successfully through the Cloud Build release
gate. Build `954b10eb-f87a-4ad7-b66d-bd2beb8d7bf0` deployed
`amftms-api-00077-bgr` to `amftms-api`, `us-east4`, project `amftms`, serving 100%
of traffic. The deployed immutable image digest is
`sha256:8f7fdd849392b4618a5e2f169bed1937895a079278bfefde950333b0530bcace`.

`AMFTMS_RELEASE_UI=1 UI_TEST_BROWSER_CHANNEL=chrome bash deploy-client.sh`
repeated the complete 710-test gate and all 40 offline UI cases, then published
the exact 222-file `artifacts/release.T8549i/publish/wwwroot` to Firebase site
`amftms`. Hosting release completed successfully at `https://amftms.web.app`,
also served by the existing custom domain `https://tms.amfcarrier.com`.

A fresh browser tab on the custom domain booted the published WASM and reached
Login without browser errors. There was no authenticated production session, so
production map interaction was not claimed. Local Dispatch Cards showed loaded,
empty and total mileage and both RPM values, including #1370/#1376. Returning to
Papers and reloading confirmed planned/unassigned loads; the original Papers view
preference was retained. The temporary production check tab was closed; the local
Fleet Map remains open. Provider savings and production latency are still unmeasured.

Post-deployment read-only verification confirmed the expected ready API revision,
digest and 100% traffic. Both API hostnames returned `200 Healthy` for liveness;
anonymous readiness, stations and preview checks returned `401 Bearer`. The custom
domain index and 68 fetched assets matched the staged SHA-256 bytes and expected
non-HTML MIME types: 12 JavaScript files, 55 WASM files and the main stylesheet.
This includes the new `authStorage.js`, which now serves JavaScript rather than
the previous SPA fallback. Firebase live release `1788851401370000` identifies
finalized version `71da137c3f0d3123` (2026-09-08 07:10:01 UTC). Its service metadata
counts 224 hosted files; the release gate validated 222 manifest assets.
The uncapped new-revision error-log query found zero matching error entries at
07:11:56 UTC. This is a post-release observation, not an ongoing monitor or proof
that future requests cannot fail.
