# Server performance and provider-cost review — 2026-09-08

This began as a source-based review; the approved follow-up implements the changes
below. It is not a production benchmark or billing audit. Executable verification,
ordinary local UI checks and deployment identity are recorded separately in
`stabilization-work.md`. No forced live provider calculation or shared-database
load test is part of the optimization verification.

## What is already reused

- Next Loads reads persisted base routes and deadhead connections. Opening it is
  not itself a TomTom calculation. Its revision protocol can omit unchanged
  geometry and send label-only updates.
- Regular planning reads saved routes, cached telemetry and cached ETA. It may
  enqueue background work; it does not synchronously calculate a new provider
  route for every map poll.
- TomTom has a durable exact-query cache and committed attempt reservations.
  The configured shared limits remain 1,000 attempts per UTC day and 30 per
  minute. Failed attempts count; cache hits do not. The truck-specific budget
  remains disabled as requested.
- Base routes and delivery-to-next-pickup connections have input signatures.
  A moving truck does not change a future delivery-to-pickup connection's identity.
- Google Places fuel-station lookups already have durable attempt ownership,
  retry suppression and successful-result reuse.

## Immediate display work

The associated implementation separates a per-truck saved preview from the live
planning read. The preview resolves the authoritative current dispatch and saved
geometry without HOS, geocoding, provider routing, fuel recommendations, or a
refresh enqueue. A missing saved current route must not select a later load.
The Client waits at most two seconds for this optional cold-selection path, then
falls back to regular planning. Warm selection skips it. Normal planning still
refreshes metadata and uses the known geometry revision.

The fleet-preview cache previously called the shared read cache while holding
one of that cache's striped gates. A nested cache miss mapped to the same gate
could wait indefinitely. Fleet preview now owns a separate gate and one bounded
serialized cache entry; the per-truck path does not acquire that fleet gate.
Final executable and browser results belong in `stabilization-work.md`.

## Approved implementation

| Priority | Finding | Implemented change | Experience/safety constraint |
| --- | --- | --- | --- |
| 1 | Persisted verified points were geocoded again. | Reuse valid points with managed source provenance and a verification timestamp younger than 29 days; propagate all provenance/retry fields through address and deadhead reads. | Address changes, expiry, future timestamps, retry state and unverified imported street points cannot reuse verification. Reuse avoids the original/canonical query-key mismatch without creating new provider cache aliases. |
| 1 | Base preparation resolved stops before testing saved geometry. | Check the exact input signature and complete saved base/non-live plan first. | Assignment, profile and stop validation remain; malformed/incomplete geometry is not accepted as a route. |
| 1 | Next Loads repeated database reads before its revision check. | `INextLoadRouteReader` joins base/deadhead metadata in one read, loading geometry only for a changed revision; cold reads skip the redundant metadata query. Already-loaded dispatches feed the bounded history reader. | Application still owns chronology and ambiguity. PostgreSQL folds the undated flag into its top-two LATERAL read; SQLite keeps its bounded fallback. No concurrent queries share a DbContext. |
| 2 | Fuel-region planning rebuilt a saved onward connection. | Reuse the exact previous/current/profile/stop-signature match before provider fallback. | Reassignment or an inserted intermediate load invalidates the connection; live truck movement does not. |
| 2 | Background preparation repeatedly processed a broad load scan. | Deduplicated versioned queue, bounded rotating repair scan, current/next priority, 14-day assigned horizon and three-day unassigned prewarming. Missing Next Loads can enqueue explicit demand outside that horizon. | A read returns saved/pending data immediately, never waits for the queued provider work. Changed inputs and failed attempts have distinct invalidation/backoff behavior. |
| 2 | Unchanged planning reads deserialized full display geometry before removing it. | Immutable serialized display and metadata projections; exact matching revisions deserialize only metadata. | The cache also retains exact GPS matching geometry and counts both projections toward its 32-MiB bound. Background reads retain full geometry. |
| 2 | Cold work for unrelated trucks/drivers shared global gates. | Each owner has 64 bounded key stripes, same-key in-flight deduplication and gate-wait diagnostics. | Stripes may collide; they are not unlimited concurrency or cross-instance locking. TomTom's durable shared quota/reservation gate remains unchanged. |
| 3 | Telemetry fallback repeatedly requested the full previous minute. | Retain a bounded 60-second stream window, request incremental data with a ten-second overlap, reconcile the full window every 30 seconds, and honor the high-frequency flag. | Commit a watermark only after all pages succeed. Fleet changes, gaps and clock rollback bootstrap again; oversized windows are not retained. Fresh stats and the ten-second UI cache policy remain. |
| 3 | HOS failure discarded the successful merge baseline. | Preserve an immutable baseline separately from availability and retry state; share a bounded driver catalog between fleet and HOS reads. | Failure still reports unavailable. Recovery can use the two-day tail, while initial/history reconciliation retains the 16-day fetch and 15-minute reconciliation. A cached baseline never becomes fresh hours merely because HTTP failed. |

### Preparation controls

`RoutePreparation` is startup-validated alongside the other Application options.
Defaults are a 60-second tick, ten jobs per tick, 100 rows per repair page, six-hour
successful repair interval, 512 pending jobs and 2,048 tracked states. Retry starts
at five minutes and backs off to an hour unless a provider supplies a later retry
time. Each job has a two-minute timeout. Dispatch synchronization dirties changed
loads and affected truck/successor connections; saved signatures still prevent
unchanged provider calculations. Queue state and caches are per process; restart
reconstructs work from persisted inputs/routes, not from an assumed durable queue.
Address expiry processes at most 100 rows per scan. Invalid stored source JSON
must not prevent the remaining rows from being processed.

Saved-route deserialization is shared and validates complete geometry. Malformed
base/deadhead JSON becomes pending repair rather than a failed Next Loads read.
A deadhead with valid same-input financial mileage but missing geometry retains
that mileage during repair. The attempt budget is persisted before provider work;
failures/cancellation retain cooldown, and only complete geometry clears it. A
changed input signature cannot retain the previous connection's financial values.

### Allocation evidence

`RouteDisplayCacheAllocationTests` measures managed allocation with deterministic
synthetic geometry. In the recorded run, an exact-known metadata read allocated
2,680 bytes for both three-point and 3,001-point routes; the full display read
allocated 117,680 bytes in the curved fixture. Cached display/metadata JSON sizes
were 39,164/1,233 bytes. This isolates one cache read, not an entire request, loaded
database behavior, production latency or provider savings.

### PostgreSQL execution evidence

A disposable localhost-only PostgreSQL 17 container executed the actual Npgsql
readers, with 21 assertions passing and a strict temporary harness build. The
checks cover metadata without route JSON, the full joined geometry read, absent
predecessors, top-two LATERAL execution, equal-start and undated ambiguity,
cancelled-undated recovery and inserted-load successor invalidation. Reusing
current loads required two history commands versus three when loading them in the
reader. The fixture container was stopped and automatically removed; application
connection settings and business data were not used. These are query-execution
and command-count checks, not production query plans or latency measurements.

### Source anchors

- Address reuse: `Server/Application/Features/Routing/Services/Addresses/StopLocation.cs`,
  `StopAddressService.cs`; `Server/Infrastructure/Integrations/Google/Places/GoogleAddressGeocoder.cs`.
- Saved-route lookup order: `Server/Application/Features/Routing/Services/Routes/BaseRouteService.cs`.
- Next Loads reads: `Server/Application/Features/Routing/Queries/GetNextLoadRoutes.cs`,
  `Server/Infrastructure/Persistence/DeadheadHistoryReader.cs`.
- Onward connection: `Server/Application/Features/Routing/Services/FuelPlanning/FuelRegionPlanner.cs`
  and `FuelHorizon.cs`.
- Preparation schedule: `Server/Application/Features/Routing/Background/BaseRouteOperation.cs`;
  hosted independently of `Synchronization.Enabled` in `Server/Infrastructure/DependencyInjection.cs`.
- Allocation and contention: `Server/Application/Features/Routing/Services/Routes/RouteDisplayCache.cs`,
  `PlanningReadService.cs`, `RoutePlanningService.cs`, `BaseRouteService.cs`;
  `Server/Application/Features/Routing/Services/Deadheads/DeadheadService.cs`.
- Telemetry: `Server/Application/Features/Fleet/Queries/GetFleetLocations/FleetLocationStream.cs`,
  `GetFleetLocations.cs` and `FleetTelemetryCache.cs` in that same query folder.
- HOS: `Server/Infrastructure/Integrations/Samsara/SamsaraHosHistoryProvider.cs`.
- Paid-call controls: `Server/Infrastructure/Integrations/TomTom/TomTomRoutingProvider.cs`;
  `Server/Application/Features/Fuel/Services/FuelStationLookupService.cs`.

## How to verify improvement

Measure separate stages: database round trips, cache hit/miss, gate wait,
serialization allocation/bytes, provider attempt reason, and first saved/current
and next-route presentation. Compare cold selection, warm A-to-B-to-A, unchanged
polls, assignment insertion, address/profile changes, quota exhaustion and outages.
Log stable identifiers and reasons, not addresses, credentials or provider payloads.

Use fixture-based regressions first and a disposable PostgreSQL target for query
plans/concurrency. Reconcile actual provider attempts before claiming monetary
savings. Do not extend HOS freshness, weaken truck restrictions, round GPS blindly,
or increase quotas as a substitute for removing duplicate work.

## Configuration ownership

The current project convention places configuration binding and startup validation
in API's composition root, while option types and policies remain in Application.
`Server/API/OptionsRegistration.cs` now groups these registrations behind
`AddApplicationOptions(configuration)`, including the new preparation controls.
The shared helper retains `Bind`, `ValidateDataAnnotations` and `ValidateOnStart`;
tests exercise valid binding and invalid startup values without a database or
providers. Moving the registration does not change request-time performance.
