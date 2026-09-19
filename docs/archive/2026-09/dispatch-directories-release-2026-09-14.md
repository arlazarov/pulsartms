# Dispatch editor and directories release — September 14, 2026

## Scope

Release the accumulated dispatch editor, broker profile, stop correction and
navigation changes. The localhost preview's synthetic loads, identities and
in-memory edits are not production data and are not imported.

The navigation exposes Trucks, Trailers and Drivers to administrators and
Customers & brokers to authorized users. The existing fleet configuration
screens are reused. The customer directory uses the existing broker API and
revision checks. Generic `Transfer yard` captions resolve to the recorded address;
this does not delete transfer events or unlock confirmed transfer addresses.
Confirmed Drop/Hook address corrections remain a separate outstanding limitation.

## Verification and release identity

The successful Cloud Build is `2d6dda41-6668-43e8-bf97-c95fc2eada57`.
Its API image is
`us-east4-docker.pkg.dev/amftms/amftms/api@sha256:2d0fc1544cbcd63585628502cb1e3bde71338601b64b9094545e035353d0cd34`.
The build passed 1,000 Client and 1,911 Server tests, strict compilation and
published artifact verification. Node passed 543 tests; six artifact-retention
checks were skipped because the worker could not inventory open files. Those
checks are included in the local gate; production cleanup fails closed when
inventory is unavailable.

The first Cloud Build, `f75a53a1-2708-4bab-a413-1a5148bf99a7`, failed the
12-card refresh test while waiting for the loading state to clear. The same full
gate passed on the second build without weakening that test or its timeout.
The cause of that intermittent failure is not established.

Initial local attempts exposed a formatting issue and obsolete browser selectors
for removed controls and mobile panes. The browser tests now select the Stops
pane before inspecting its visible content, retain native stop-identity checks
without requiring removed Visit counters, and check itinerary geometry relative
to the independently scrolling list. Production behavior was not altered to
accommodate these test updates.

## Migration and recovery preparation

The starting production history contained 37 migrations, ending in
`20260914153020_AddDispatchWorkspace`. The two reviewed additive migrations are:

- `20260914204235_AddBrokerProfiles`: customer profile JSON and revision.
- `20260914214701_AddStopCorrections`: revision before-image and completion override.

A private backup was created in
`/Users/antonarlazarov/Developer/pulsartms-release-backup.tXBKXN`.
The custom-format dump is 225,491,529 bytes with 388 archive entries and SHA-256
`a913e23e44449d8bf4b8322978d6642de2a320562f029572e4f2adbd29293752`.
The reviewed SQL SHA-256 is
`f0814d15a84161a2fea3ce3d3088590f74efa856b7fc459cbdfcc07fb680d107`.
The migration runner checks the expected history, applies both migrations in one
transaction, and verifies retained load, stop and customer row counts.

No local SQL server or container was started. Isolated PostgreSQL integration
tests and a full backup restore were not run because no safe isolated fixture
was available. Archive readability is not a restore test. Fixture browser passes
do not prove live-provider correctness or production performance.

## Rollout outcome

The final local `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` completed
successfully: 549 Node tests, 1,000 Client tests and 1,911 Server tests, with no
failures or skips; strict build had zero warnings/errors. It verified 273 assets
and seven JavaScript entry-point graphs. EF reported no pending model changes.
The offline browser report covered 52 page checks across 12 cases with no
regressions: `artifacts/managed/browser-ui-OtsGTT/report.json`.
The exact published Client artifact is
`artifacts/managed/release-Ko2uUm/publish/wwwroot`.

Both migrations committed successfully. Production now has 39 migration records,
ending in `20260914214701_AddStopCorrections`. All four added columns were checked.
The guarded transaction retained 386 loads and 940 stops and checked unchanged
customer counts. Existing workspace indexes remain valid. The old local API
process sharing the production database was stopped before migration; the
isolated localhost:5079 demo preview was left running.

Cloud Run revision `amftms-api-00116-nkf` is ready and serves 100% of traffic.
Its digest matches the successful Cloud Build. Previous revision
`amftms-api-00115-cm9` reports retired/inactive and traffic shutdown. Firebase
Hosting published the exact verified artifact after the API transition.

At `https://tms.amfcarrier.com`, entry HTML, CSS, favicon and Client WASM returned
HTTP 200 and matched local SHA-256 values. Entry HTML, CSS and favicon revalidate;
fingerprinted WASM has the intended one-year immutable cache header. Public
`/api/health/live` returned `Healthy`. The initial new-revision ERROR log query
returned no entries. The production browser reached the login page; authenticated
business workflows were not exercised because that browser has no signed-in
production session. No synthetic business writes were made.

The new synchronization owner advanced the persisted checkpoint to
`2026-09-15T03:09:02Z`. Catalog, dispatch, planning, locations, telemetry and
assignment jobs all recorded post-rollout successes with zero failure counters.
The later WARNING log entries were HTTP 401 responses, consistent with the
unauthenticated browser; no ERROR entries were returned by the checked query.
