# Manual stop completion — September 11, 2026

Implemented an explicit per-stop confirmation and undo for missing provider actuals.
The maintained behavior is documented in
[synchronization](../../features/synchronization.md#manual-stop-completion).

The Application handler authorizes active Admin/Dispatch users, validates actual
UTC time and the submitted stop identity/revision, and atomically persists manual
state with its actor/event history. Provider actuals are not overwritten. Server
route, ETA and fuel consumers use manual completion independently; repeated visits
retain separate identities. Current-road preparation retries after unavailable GPS
or an enabled request budget, without relaxing either safeguard.

The shared Client editor lives in load Details and the full dispatch page. Actual
time uses 12-hour input in the browser's local zone. Both confirmation and undo are
explicit writes; cancellation and invalid input remain local. Changed selections
and newer revisions reject late UI replies. Compact cards retain their timeline.

## Verification

- Final `bash test.sh all`: 1,447 server, 662 Client C# and 424 JavaScript tests
  passed, including architecture checks.
- Strict Release solution build: zero warnings/errors; both .NET test assemblies
  passed again. SCSS compilation, typed JavaScript checking and asset builds passed.
- Staged artifact verification: 252 assets and seven entry-point dependency graphs.
- Offline browser smoke: 44 page cases across desktop/mobile, both themes and
  100%/200% root fonts; repeated-visit completion forms also validate, fit and cancel.
  Desktop/mobile screenshots were visually inspected.
- One intermediate Debug run failed an existing fuel geometry allocation bound
  (6,736 bytes against 4,096); the unchanged test passed in the subsequent complete
  Debug run and the Release run. This is not a production performance measurement.
- The initial browser launch lacked Playwright's bundled Chromium. The complete
  successful probe used installed Chrome instead.

Local evidence: `artifacts/managed/browser-ui-3Ia9B2/report.json` and
`artifacts/managed/release-jjM7Ip/publish/wwwroot`. These are managed temporary
outputs, not permanent checked-in baselines.

## Not performed

Migration `20260911145347_AddManualStopCompletion` was generated but not applied.
No deployment or live stop mutation was performed. Database integration tests use
isolated in-memory SQLite; no suitable isolated PostgreSQL fixture was available.
Real PostgreSQL migration execution, live authenticated browser writes and live
provider/GPU-map behavior remain untested by this feature's offline checks.

## Deployment follow-up

Published later on September 11 at the user's request:

- Cloud Build `b2a915f1-c6ad-4bfd-8a18-d4972e738deb` succeeded with the complete
  Node/.NET release gate; all 1,447 server and 662 Client C# tests passed on Linux.
- API digest: `sha256:83f70533fe0088b8b02f71d7b33ce3c7b9cc6cf2404a7db3de97b7f4cf5c62f7`.
- Cloud Run revision `amftms-api-00099-zx9` started and received 100% traffic.
- Startup logs recorded migration `20260911145347_AddManualStopCompletion` and
  its history insert at 15:37:59 UTC. The public liveness endpoint returned Healthy.
- Firebase Hosting published the verified 252-file artifact from
  `artifacts/managed/release-cnHCwx/publish/wwwroot`. The live index matched the
  staged index exactly; versioned CSS and the Firebase-proxied liveness endpoint
  returned HTTP 200. The offline UI gate again passed all 44 page cases.

The first Cloud Build (`4d7492c7-866f-476e-a9fd-51400a5a08a0`) stopped before
deployment because its .NET container lacked Node for the test runner's artifact
retention command. Cloud Build now shares the Node step's executable through a
build-only volume. Gate assertions require that wiring; no checks were disabled.
The full local suite passed again after this build-environment correction.

The authenticated readiness endpoint returned 401 without credentials; it was not
used to claim database readiness. No live pickup/delivery was modified to test the
feature, and no isolated PostgreSQL test fixture was introduced.

## Authorization correction after deployment

Read-only Cloud Run request logs showed five completion PUT requests returning
403 on September 11 at 17:34 UTC. The endpoint incorrectly used standard
`Authorize(Roles = ...)`, while persisted roles use the custom `amftms:role` claim
and the current-role service. The handler was therefore unreachable for normal
application principals. The Client mapped this denial to a misleading actual-time
message.

Replaced the endpoint guard with a registered Dispatch policy using
`IUserRoleService`; active Admin and Dispatch access is preserved, and the command
retains its independent authorization check. The Client now distinguishes
session expiry, permission denial, conflict, input rejection and server failure.
No account roles or production stop facts were changed.

Regression tests combine the actual endpoint metadata and registered policies with
Identity-generated principals and in-memory SQLite roles. Both operator roles pass
without standard role claims; anonymous, unknown and deactivated identities fail.
Client tests cover 401/403/500 messages without blaming actual time.

Final `bash test.sh all`: 1,449 server, 665 Client C# and 424 JavaScript checks
passed. Strict Release Client build passed with zero warnings/errors. This
correction has not yet been deployed. Browser layout and real PostgreSQL checks
were not repeated; no schema change is required.

## Completion progress without road rebuilding

Manual completion previously invalidated the route hash and forced a GPS reroute
even when only the completed prefix changed. The command now checks the persisted
pre-edit inputs and stages compatible tracking/hash updates in the same save as
the stop and audit event. Geometry, route version and calculation time are retained.
Undo can reuse an existing road that still covers the restored itinerary; interior
skips, absent restored stops and independently changed road inputs remain stale.
The unconditional manual-completion reroute was removed, including the assigned
load connection fallback after a completed pickup. Necessary manual-itinerary
rebuilds start from fresh GPS and respect automatic request budgets.

Regression checks cover base/current-position roads, assigned/in-transit loads,
repeated polling without provider calls, completion/undo, repeated visit identity,
interior skips, missing restored stops, changed address/order/truck, all stops
completed and atomic rollback against a concurrent route replacement.

`bash test.sh all` passed: 1,462 server, 665 Client C# and 424 JavaScript tests.
The earlier affected-category run also passed. No schema migration is needed.
PostgreSQL fixture checks, live provider calls and browser checks were not run;
no production completion was changed for testing. This correction is local and
has not been deployed to Cloud Run or Firebase.
