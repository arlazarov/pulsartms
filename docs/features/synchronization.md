# Server synchronization

Settings live in `Server/API/appsettings.json`, under `Synchronization`. `appsettings.Production.json` enables the worker; `appsettings.Development.json` disables it so a local API does not start a second schedule against Neon. Configuration changes apply after restarting/redeploying the API. Environment variables override JSON, for example `Synchronization__AssignmentsSeconds=180`.

Load import has an independent `DispatchImport:Provider` selection. Empty disables
its adapter and loop while other scheduled jobs continue. See
[optional load imports](dispatch-import.md). The checked-in selection remains
`torqueai`; changing credentials is not required to stop its polling.

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Enabled` | false; true in Production | Run scheduled work independently of browsers |
| `AssignmentsSeconds` | 120 | Current Samsara driver/truck/trailer assignments |
| `DispatchSeconds` | 120 | Configured load import |
| `CatalogSeconds` | 3600 | Full Samsara driver, truck and trailer catalog |
| `TelemetrySeconds` | 15 | Incremental GPS, engine and fuel feed |
| `HighFrequencyLocations` | true | Also collect recent high-frequency location points for map playback |
| `CheckpointSeconds` | 120 | Persist one fleet telemetry/cursor/job checkpoint |
| `PlanningSeconds` | 30 | Process the route preparation queue |
| `OnDemandPlanningSeconds` | 120 | Durable cooldown after a successful on-demand check; recently saved routes also skip the first check |
| `RouteDeviationMiles` | 5 | Minimum distance from the saved route before automatic rerouting |
| `RouteDeviationSeconds` | 120 | Time the truck must stay beyond that distance before rerouting |
| `ReadCacheSeconds` | 120 | Shared cache for dispatches, routes, profiles, preferences and station prices |
| `SessionValidationSeconds` | 30 | Cache authenticated session checks; maximum 60 seconds |
| `RetrySeconds` | 60 | Initial failed-job retry delay; exponential backoff capped at 15 minutes |
| `JobTimeoutSeconds` | 180 | Cancel a stalled job |
| `UpcomingRoutesPerTruck` | 1 | Prepare this many upcoming loads after the current one |
| `MaxTrucksPerPlanningCycle` | 10 | Bound route queue work per cycle |
| `DispatchLookbackDays` | 7 | Load import window before today |
| `DispatchLookaheadDays` | 7 | Load import window after today |

`TomTom:DailyRequestLimit` (1000 per UTC calendar day) and `TomTom:RequestsPerMinute` (30) limit uncached TomTom routing attempts across the fleet. Failed attempts also count; cache hits do not. These are application limits, not a statement about the provider's pricing. API keys remain server configuration/secrets; no keys belong in committed appsettings files.

`RouteRecalculationBudget:Enabled` is temporarily `false` by explicit operator request. This disables only the separate per-truck reroute budget, not the shared TomTom limits, cache, duplicate-request protection or sustained-deviation checks. Set it to `true` and restart the API to restore the per-truck policy; prior attempt history is retained. See [route planning](route-planning.md) for the policy.

## Database and API traffic

- Feed and high-frequency locations have independent jobs and retry state.
  Failed or blocked feed reads cannot prevent current positions from publishing.
  Provider reads occur outside the snapshot publication gate. Feed parameters
  remain `gps,engineStates,fuelPercents`; the saved cursor must not be reused
  with additional decorations. Google Weather owns selected truck temperature.
  Provider failures log once at the job boundary, with a status code but no
  URL, opaque cursor, credentials or raw response body.
- High-frequency GPS updates both the live snapshot and checkpoint vehicles.
  Followers therefore read the same latest known coordinates after checkpoint
  publication. Older feed positions cannot overwrite newer stream positions;
  stream merges preserve sensor values and the incremental feed cursor.
  An absent reverse-geocoded address clears the old address, not the newer GPS.
  GPS freshness checks and route-progress validity rules remain unchanged.
- HOS clock reads consume a same-instance snapshot without waiting for Samsara.
  A separate operation in the existing server refreshes it every 45 seconds while
  the synchronization lease owner keeps it warm or there is recent local demand.
  Clocks older than 60 seconds are unavailable, not silently re-dated. Cold ETA
  results retry after 10 seconds and do not fetch HOS history without a clock.
  Driver-specific catalog settings use keyed immutable entries.
- TorqueAI reconciliation fingerprints each load separately (at most 8,192
  remembered loads). Unchanged loads skip aggregate hydration; changed loads use
  indexed driver/equipment/customer matching. Catalog/manual changes invalidate
  reuse and each remembered load is rechecked after 30 minutes. The import window
  is unchanged. Torque scheduling is independent after initial catalog/assignment
  readiness, within the same leased server worker.
- Provider JSON is streamed through an 8 MiB/page and 64 MiB/import byte budget,
  with 256-page and 100,000-row bounds. Oversized input fails before replacing the
  last usable snapshot or watermark. This reduces duplicate buffering; it does
  not promise constant total memory for the materialized bounded result.
- Background route discovery reads an identity-only board projection rather
  than hydrating display addresses, cargo and notes for every candidate load.
- TomTom allows two distinct in-flight provider bodies per instance. A short
  shared reservation lock retains quota accounting, and same-request callers
  share request-key exclusion. Completed keys are not retained indefinitely.
  Durable attempt limits, cache rechecks, cancellation and truck restrictions
  remain in force; this does not increase configured rate/daily allowances.

- Catalog and assignment synchronization are separate. Identical provider payloads are skipped before loading entity tables. If a payload changes, EF writes changed values only. Assignment swaps release only changed relationships before assigning their new owners within one transaction.
- TorqueAI matches source visits through the shared stop matcher and preserves
  stable stop identities when source identity is unchanged. Repeated visits are
  not merged by address. Native execution snapshots and locally owned workspace
  itineraries have separate reconciliation boundaries; source sequence is not
  authority to overwrite recorded history.
- The [load workspace](dispatch-workspace.md) retains explicit local edits.
  Unoverridden commercial fields and matched-stop content continue receiving
  updates. Locally owned stop order, assignments and appointments require review
  when the source changes them. Conflicting actuals are not overwritten.
  `LastSyncedAt` identifies the last applied or reconciled import, not the
  provider's modification timestamp. The checkpoint records successful checks
  even when nothing changed.
- For source-owned itineraries, newly imported sequences are explicitly tracked
  as inserts, including when an existing load grows after its first import.
  Separate provider sequences remain separate visits even when their facility
  and address match. Removed sequences delete only their own source records;
  changed imports invalidate affected routes after a successful save. Native
  execution and local workspace ownership retain their review protections.
- Changes to existing accepted execution stops use the same Application owner
  as manual itinerary editing and explicit source acceptance. Inserting a visit
  retires obsolete automatic planned movements in the same transaction as the
  stop revision, history and rebuild request. Observed movement or user-recorded
  mileage decisions protect the affected path and produce a review notice.
  Reimport does not repeat that revision or the supersession decision. Recording
  time is not substituted for an unknown assignment start when checking actuals.
- All browsers use the server's current telemetry snapshot when background synchronization is enabled. They do not each poll Samsara. The feed cursor is stored together with the telemetry it represents. High-frequency location history is requested centrally for the fleet and kept in memory for two minutes.
- Route progress is calculated against cached route geometry. Meaningful stop-tracking events and new routes/fuel plans are saved immediately. Ordinary GPS movement does not write route rows. Repeated route reads from a warm cache make no route-table queries.
- Dispatch board, route inputs, settings and fuel prices share a memory cache. Writes invalidate affected groups on the current server; changes from another process become visible within `ReadCacheSeconds`.
- Session validation has a separate short TTL. Logout, password/email changes, user deactivation and deletion invalidate the current server's session cache; another instance observes changes within `SessionValidationSeconds`. Login and refresh still validate against storage.
- One database lease is renewed every 45 seconds. One checkpoint row is saved every `CheckpointSeconds`; it contains latest telemetry, feed cursor, pending truck IDs and job success/retry state. This is bounded background traffic, not zero database traffic. State survives a process restart; up to one checkpoint interval may be replayed safely from the feed.

The scheduling intervals are delays after completion, so slow calls do not create overlapping runs. Source errors retain the last usable data and do not prevent other loops from continuing. A database lease allows one worker owner at a time; lease loss cancels its jobs. An abandoned lease expires after three minutes. Source locks also serialize scheduled and manually invoked syncs within a process.

Fuel exchange-rate refresh keeps its separate lease and hourly successful
cadence. The provider request finishes before the stored observation enters
the shared planning publication transaction. Busy publication retains the
previous rate, releases the lease and schedules another attempt.

A RoutePlanningException with an explicit retry time defers a scheduled job
without recording success, incrementing its failure count or logging a failure.
Prior success/error history remains until an actual successful run resets it.
If the supplied retry time has already passed, RetrySeconds supplies the delay.
Other failures retain the existing exponential retry policy and boundary log.

## Manual stop completion

When TorqueAI has not supplied a pickup/delivery completion, an active Admin or
Dispatch operator can open a load's Details and select **Mark completed** on the
specific stop. The confirmation requires an actual date and 12-hour local time;
the server stores UTC and rejects future times or times before a reported arrival.
Opening or cancelling the form does not write anything.

Manual completion is separate from provider actuals. Normal synchronization keeps
it, and undo removes only the manual confirmation. Later provider completion still
wins. The current confirmation shows its actor and time; an append-only event table
retains each confirmation and undo with actor ID, recording time and revision.
Optimistic concurrency and a stop identity fingerprint reject stale editor saves.
The endpoint's Dispatch policy resolves current application roles through
`IUserRoleService`, like the Admin policy; it does not require standard role claims
in the bearer principal. The command independently checks the active operator.
Identity follows the existing imported dispatch/stop ID and sequence; removed
provider sequences are removed from the active itinerary, but their audit events remain.

Only the selected visit is manually completed, even at repeated addresses. The
board, stop tracker, ETA activity and fuel signatures consume this fact. Completing
a prefix of the saved itinerary advances tracking without rebuilding the road:
the command atomically updates the route input signature and completion metadata,
preserving geometry, version and calculation time. Undo also reuses the road when
it still covers the restored stop and remaining itinerary. An interior skipped
visit, a restored stop absent from the current road, or changed routing inputs
requires preparation from fresh GPS. Actual persistent route deviation retains
the normal rerouting checks and request budgets. Provider availability and GPS
freshness can delay a necessary rebuild, but do not block a simple completion.
Existing fuel purchases are retained but
marked stale until an explicit fuel recalculation succeeds. Completing every stop
moves the load to Completed history, where its manual confirmations can be undone.
Undo prevents old GPS inference from immediately completing the same stop again.

Deployments containing this feature require migration
`20260911145347_AddManualStopCompletion` before serving the new API. This guide does
not assert that the migration has been applied to any environment.

## Truck starting stop

The first explicitly truck-assigned stop starts the truck itinerary. Earlier
driver-only stops remain in Dispatch and retain their actual completion state,
but are excluded from truck geometry, fuel and truck-stop ETA. Missing trailer
assignment does not exclude a stop: bobtail travel is still truck travel.

Dispatch operators can confirm an active truck and exact starting visit when the
provider omits assignments. Confirmation is stored separately with actor, time
and an optimistic revision. Imports preserve it while assignments are missing or
still belong to the confirmed truck. If the header and every stop explicitly
resolve to one different active truck, synchronization releases the old truck
confirmation and starting anchor, increments its revision and invalidates route
preparation for both trucks. Existing completion timestamps remain unchanged.
Mixed or unresolved assignments, manual stop operations and a deleted manual
anchor still require review. A stale saved road
is not evidence of assignment. Mixed-truck itineraries are not automatically split.
The truck's connection from its preceding load still ends at the truck starting
stop, not at the driver's personal-travel stop. ETA remains unavailable across an
unresolved driver change; the previous driver's clocks are not reused and personal
travel is not assumed to be rest.

Apply `20260911183435_AddTruckAssignmentStart` before starting the updated API.
This guide does not assert that the migration or a load confirmation is applied.

## Route preparation

Active truck assignments are discovered from the dispatch board. A bounded queue rotates through the current loads without depending on which trucks a browser displays. The queue/checkpoint and persisted route inputs allow recovery after a restart. Current roads are reused until their relevant inputs change or the truck persistently leaves the route. Shared price preferences do not change the road geometry.

Upcoming loads are prepared between their own pickup/delivery stops, without applying the truck's current GPS or marking future stops passed. Their approach from actual GPS is handled when they become the current load. Completed/history loads are excluded. Fuel quantities still require actual tank capacity and MPG; no values are invented by the scheduler.

With synchronization enabled, automatic planning HTTP endpoints serve saved
plans and current progress. Missing or stale plans request a durable refresh.
With synchronization disabled, reads also request periodic checks of older
saved plans. Reads commit the request before returning a queued status and do
not wait for TomTom or fuel calculations. Previous saved results stay available
while work runs. Provider errors retain their display policy and are keyed to
the captured work, full effective profile and route choice.

PlanningRefreshRequests coalesces demand by load, optional execution leg and
assignment revision. Changed itinerary/profile/choice inputs advance the
request version without stealing an active lease. Repeated demand keeps the
retry deadline. Workers claim different unlocked rows, acknowledge only their
captured version under an unexpired lease, and recover abandoned work after
lease expiry. Newer demand survives older success or failure. Success uses
OnDemandPlanningSeconds; failure starts at RetrySeconds with exponential
backoff capped at fifteen minutes. A claim lasts JobTimeoutSeconds plus one
minute; the calculation timeout remains JobTimeoutSeconds.

PlanningConcurrency bounds active refreshes per instance. A local coalesced
signal wakes idle consumers; a five-second poll also finds requests from other
processes and restarts. A five-second display memo avoids repeated demand
writes but owns no work. Completed requests are pruned after seven days.
Deleted, completed or superseded assignments are acknowledged without provider
work. The separate source-road repair queue and fleet scheduling checkpoint
retain their existing policies. Delivery claims do not replace publication's
fresh-input and optimistic-result checks.

This queue is included in the pending `20260917055902_RebuildExecutionStorage`
transition. Downgrade refuses to drop pending requests. This guide does not
assert deployment or application of the migration to the working database.

## Hosting and operations

`AddSynchronizationCheckpoint` adds only the `SynchronizationCheckpoints` table. The migration has been applied to the configured Neon development connection. Apply migrations to the target database before starting a new production revision.

The current deployment target is Cloud Run. `deploy-server.sh` now specifies `--min 1 --max-instances 1 --no-cpu-throttling`. This keeps a single instance available to run background work without incoming browser requests. It changes Cloud Run to always-allocated CPU and has ongoing hosting costs. In-memory live telemetry is designed for this single-instance deployment; additional instances can read the saved checkpoint but do not share the live 15-second snapshot. Scaling out should include a shared live cache or separate worker deployment.

Required production configuration remains `ConnectionStrings:DefaultConnection`, `Samsara:ApiToken`, `TorqueAI:ApiKey`, `TorqueAI:BaseUrl`, and `TomTom:ApiKey`, along with the application's existing authentication/integration configuration. Existing Cloud Run environment variables are retained by the deployment script. This code change does not itself deploy the service.

Authenticated `GET /api/synchronization/status` reports whether scheduling is enabled, whether this instance owns the worker lease, pending truck count, and each job's last success, next run and error type. It reads memory rather than the database and returns no credentials or telemetry payload.

Verification covers restart/cursor recovery without browser requests, exclusive worker ownership, stale-owner write rejection, idempotent assignment/dispatch updates, assignment swaps under unique constraints, unchanged stop identifiers, warm route reads without database or routing calls, cache isolation, retry backoff, and existing authorization revocation. A real read-only Samsara feed request validated GPS, engine/fuel parsing and a continuation cursor.

References: [Samsara telemetry feeds](https://developers.samsara.com/docs/telematics), [Cloud Run background CPU](https://docs.cloud.google.com/run/docs/configuring/billing-settings), [Cloud Run minimum instances](https://docs.cloud.google.com/run/docs/configuring/min-instances).
