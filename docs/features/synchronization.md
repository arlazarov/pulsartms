# Server synchronization

Settings live in `Server/API/appsettings.json`, under `Synchronization`. `appsettings.Production.json` enables the worker; `appsettings.Development.json` disables it so a local API does not start a second schedule against Neon. Configuration changes apply after restarting/redeploying the API. Environment variables override JSON, for example `Synchronization__AssignmentsSeconds=180`.

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Enabled` | false; true in Production | Run scheduled work independently of browsers |
| `AssignmentsSeconds` | 120 | Current Samsara driver/truck/trailer assignments |
| `DispatchSeconds` | 120 | TorqueAI dispatch synchronization |
| `CatalogSeconds` | 3600 | Full Samsara driver, truck and trailer catalog |
| `TelemetrySeconds` | 15 | Incremental GPS, engine and fuel feed |
| `HighFrequencyLocations` | true | Also collect recent high-frequency location points for map playback |
| `CheckpointSeconds` | 120 | Persist one fleet telemetry/cursor/job checkpoint |
| `PlanningSeconds` | 30 | Process the route preparation queue |
| `OnDemandPlanningSeconds` | 120 | Minimum interval between successful local on-demand checks; recently saved route and fuel results also skip the first check |
| `RouteDeviationMiles` | 5 | Minimum distance from the saved route before automatic rerouting |
| `RouteDeviationSeconds` | 120 | Time the truck must stay beyond that distance before rerouting |
| `ReadCacheSeconds` | 120 | Shared cache for dispatches, routes, profiles, preferences and station prices |
| `SessionValidationSeconds` | 30 | Cache authenticated session checks; maximum 60 seconds |
| `RetrySeconds` | 60 | Initial failed-job retry delay; exponential backoff capped at 15 minutes |
| `JobTimeoutSeconds` | 180 | Cancel a stalled job |
| `UpcomingRoutesPerTruck` | 1 | Prepare this many upcoming loads after the current one |
| `MaxTrucksPerPlanningCycle` | 10 | Bound route queue work per cycle |
| `DispatchLookbackDays` | 7 | TorqueAI import window before today |
| `DispatchLookaheadDays` | 7 | TorqueAI import window after today |

`TomTom:DailyRequestLimit` (1000 per UTC calendar day) and `TomTom:RequestsPerMinute` (30) limit uncached TomTom routing attempts across the fleet. Failed attempts also count; cache hits do not. These are application limits, not a statement about the provider's pricing. API keys remain server configuration/secrets; no keys belong in committed appsettings files.

`RouteRecalculationBudget:Enabled` is temporarily `false` by explicit operator request. This disables only the separate per-truck reroute budget, not the shared TomTom limits, cache, duplicate-request protection or sustained-deviation checks. Set it to `true` and restart the API to restore the per-truck policy; prior attempt history is retained. See [route planning](route-planning.md) for the policy.

## Database and API traffic

- Catalog and assignment synchronization are separate. Identical provider payloads are skipped before loading entity tables. If a payload changes, EF writes changed values only. Assignment swaps release only changed relationships before assigning their new owners within one transaction.
- TorqueAI updates existing stops in place by sequence. Status, timing and notes changes preserve stop identifiers, preventing unnecessary route invalidation. `LastSyncedAt` on a dispatch records a changed import; the checkpoint records successful checks even when nothing changed.
- All browsers use the server's current telemetry snapshot when background synchronization is enabled. They do not each poll Samsara. The feed cursor is stored together with the telemetry it represents. High-frequency location history is requested centrally for the fleet and kept in memory for two minutes. Each published snapshot carries a new `Revision`; `GET /api/fleet/locations` returns it as a weak `ETag` with `Cache-Control: private, no-cache`, so a browser poll whose `If-None-Match` still matches receives `304` and no body is serialized. Browsers revalidate automatically; the Client sends no conditional headers itself. The dispatch board polls `?points=false` and receives current positions without the location history; the map keeps the full snapshot.
- Route progress is calculated against cached route geometry. Meaningful stop-tracking events and new routes/fuel plans are saved immediately. Ordinary GPS movement does not write route rows. Repeated route reads from a warm cache make no route-table queries.
- Dispatch board, route inputs, settings and fuel prices share a memory cache. Writes invalidate affected groups on the current server; changes from another process become visible within `ReadCacheSeconds`.
- Session validation has a separate short TTL. Logout, password/email changes, user deactivation and deletion invalidate the current server's session cache; another instance observes changes within `SessionValidationSeconds`. Login and refresh still validate against storage.
- One database lease is renewed every 45 seconds. One checkpoint row is saved every `CheckpointSeconds`; it contains latest telemetry, feed cursor, pending truck IDs and job success/retry state. This is bounded background traffic, not zero database traffic. State survives a process restart; up to one checkpoint interval may be replayed safely from the feed.

The scheduling intervals are delays after completion, so slow calls do not create overlapping runs. Source errors retain the last usable data and do not prevent other loops from continuing. A database lease allows one worker owner at a time; lease loss cancels its jobs. An abandoned lease expires after three minutes. Source locks also serialize scheduled and manually invoked syncs within a process.

## Route preparation

Active truck assignments are discovered from the dispatch board. A bounded queue rotates through the current loads without depending on which trucks a browser displays. The queue/checkpoint and persisted route inputs allow recovery after a restart. Current roads are reused until their relevant inputs change or the truck persistently leaves the route. Shared price preferences do not change the road geometry.

Upcoming loads are prepared between their own pickup/delivery stops, without applying the truck's current GPS or marking future stops passed. Their approach from actual GPS is handled when they become the current load. Completed/history loads are excluded. Fuel quantities still require actual tank capacity and MPG; no values are invented by the scheduler.

With synchronization enabled, the automatic planning HTTP endpoints serve saved plans and current progress. Missing plans show a preparation message while the server queue processes them. With synchronization disabled (local development), HTTP endpoints also return saved plans immediately and enqueue refreshes in a bounded server queue. Refreshes have their own DI scope, survive browser navigation, deduplicate pending dispatches, and use OnDemandPlanningSeconds/RetrySeconds cooldowns. Opening a card never waits for TomTom or fuel candidate calculations. Dispatch also refreshes its displayed assignments/loads without a page reload.

## Hosting and operations

`AddSynchronizationCheckpoint` adds only the `SynchronizationCheckpoints` table. The migration has been applied to the configured Neon development connection. Apply migrations to the target database before starting a new production revision.

The current deployment target is Cloud Run. `deploy-server.sh` now specifies `--min 1 --max-instances 1 --no-cpu-throttling`. This keeps a single instance available to run background work without incoming browser requests. It changes Cloud Run to always-allocated CPU and has ongoing hosting costs. In-memory live telemetry is designed for this single-instance deployment; additional instances can read the saved checkpoint but do not share the live 15-second snapshot. Scaling out should include a shared live cache or separate worker deployment.

Required production configuration remains `ConnectionStrings:DefaultConnection`, `Samsara:ApiToken`, `TorqueAI:ApiKey`, `TorqueAI:BaseUrl`, and `TomTom:ApiKey`, along with the application's existing authentication/integration configuration. Existing Cloud Run environment variables are retained by the deployment script. This code change does not itself deploy the service.

Authenticated `GET /api/synchronization/status` reports whether scheduling is enabled, whether this instance owns the worker lease, pending truck count, and each job's last success, next run and error type. It reads memory rather than the database and returns no credentials or telemetry payload.

Verification covers restart/cursor recovery without browser requests, exclusive worker ownership, stale-owner write rejection, idempotent assignment/dispatch updates, assignment swaps under unique constraints, unchanged stop identifiers, warm route reads without database or routing calls, cache isolation, retry backoff, and existing authorization revocation. A real read-only Samsara feed request validated GPS, engine/fuel parsing and a continuation cursor.

References: [Samsara telemetry feeds](https://developers.samsara.com/docs/telematics), [Cloud Run background CPU](https://docs.cloud.google.com/run/docs/configuring/billing-settings), [Cloud Run minimum instances](https://docs.cloud.google.com/run/docs/configuring/min-instances).
