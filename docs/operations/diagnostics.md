# Operational diagnostics

- `GET /api/health/live`: process liveness, anonymous, no database or upstream calls.
- `GET /api/health/ready`: database connectivity, Admin policy, health status only (no connection details).
- `GET /api/diagnostics/requests`: Admin-only request count, failures, cancellations, total and maximum milliseconds by request type. Counters are per process and reset on restart; they are not fleet-wide or durable metrics.
- Meter `PulsarTms.Application`, histogram `pulsartms.request.duration` (milliseconds), tags `request`, `outcome`. Export through a metrics collector when one is configured. Slow requests and routing requests also emit `RequestTiming` logs, so diagnosing them does not depend on a collector. When upgrading a collector from the former product identity, update its meter subscription; historical series are not rewritten.
- `AdminAudit` is an Application pipeline behavior. It records caller identity ID, command name, target ID when present, outcome, trace ID and allowlisted role/activation/planning-setting values. It never serializes commands, profiles, passwords or tokens. These are action records, not before/after database snapshots. Hosting retention controls durability; no separate audit database is introduced.
- Background route failures include dispatch ID and exception stack.
  Synchronization job failures include job name and exception stack. A planning
  exception with an explicit retry time is an expected job deferral; the fleet
  scheduler sets its next attempt without logging or counting a failure.
- Unexpected exceptions are logged once at the HTTP or worker boundary, not again by request timing. Cancellation requested by the caller is not logged as a failure. Provider HTTP exceptions retain status codes without embedding response bodies or query strings. HOS fallback warnings are bounded by the existing one-minute retry cache.

## Dispatch data and search

`dotnet run --project tools/RouteMemoryProbe --artifacts-path artifacts/eta-diagnostic
-- --read-only --eta-forecast=<dispatch-guid>` inspects one saved ETA forecast using
the configured application connection. This mode requires `--read-only`, uses a
parameterized query in a read-only transaction with a ten-second statement timeout,
and limits the forecast JSON to one MiB. Output is restricted to snapshot timestamps,
stop identifiers, projected arrival/departure, driving/rest minutes and cycle/recap
fields; it does not request providers or print raw provider payloads or credentials.
This is a scoped application-data diagnostic, not a disposable database test fixture.

The same probe accepts `--read-only --truck-fuel-status=54777` for one truck's
durable fuel summary. Use a managed scratch build as below. A read-only
transaction, ten-second statement timeout and 512 KiB summary bound protect this
read. It prints plan ownership, calculation/pricing dates, manual status and
purchase identities, without geometry, raw provider payloads or credentials.
It does not validate current GPS, query prices or recalculate the saved plan.

For a bounded local read-path comparison, run
`node scripts/artifacts.mjs run scratch -- dotnet run --project tools/RouteMemoryProbe --artifacts-path '{artifacts}/build' -- --read-only --performance-reads`.
This invokes board and saved-preview reads in a read-only transaction, without
starting hosted workers, and makes one Samsara HOS read with a thirty-second limit.
It does not rebuild routes or save application data. Output contains command
counts, table-level durations and serialized sizes, never response contents or
credentials. SQL timings include remote database/network wait; JSON sizes exclude
HTTP compression and the probe does not measure browser rendering. The shared
transaction is intended for these sequential reads, not concurrent telemetry reads
or a substitute for an isolated database test fixture.
Add `--payloads-only` to compare each displayed truck's saved preview and
known-version planning response sizes instead. That mode makes no HOS provider
request; its process-local HOS/ETA caches start empty. It reports JSON sizes and
offline Fastest Gzip/Brotli sizes, not measured HTTP compression. Route refresh
demand stays in the diagnostic process because no workers are started.

The cached board index contains assignment, schedule and search fields. Full addresses, cargo and notes are hydrated only for the selected page. Grouping and pagination still run over the lightweight index in memory; this preserves stop-level, number-only and unassigned truck associations. A database-owned assignment index would be the next step if load tests show this stage becoming significant.

Dispatch suggestions use the same filtered page returned by the server (up to 12 trucks), including matches for loads anywhere in the queue. Exact truck numbers take priority, followed by truck/trailer/driver matches, then load/order/customer/location matches. Truck/trailer/load/order numbers use prefixes; names and locations use substring matching. Telemetry polls independently and is reused during search.

## Token storage assessment

Access and refresh tokens currently remain in localStorage. Any script executing on this origin can read them; moving to sessionStorage only changes persistence and does not protect against XSS. Existing logout/security-stamp validation and active-user checks mitigate session lifetime, not token extraction.

A complete migration should put refresh credentials in Secure, HttpOnly cookies, keep access tokens in memory, add CSRF defenses for cookie-authenticated refresh/logout, and verify same-origin production hosting plus cross-origin localhost development. Include reload, multiple tabs, logout, role changes and refresh concurrency in tests. Do not silently replace storage with cookies without implementing these linked changes. This pass assesses the migration but does not change the authentication protocol.

## Verification boundaries

### Process memory

`GET /api/diagnostics/memory` is an Admin-only, process-wide snapshot. It reads
runtime counters and cache statistics without database/provider calls, forced
collection, cache eviction or payload enumeration. It returns no cache keys,
driver identities, geometry, credentials or provider response contents.

Interpret the fields separately:

- `workingSetBytes` is the current process working set.
- `managedBytesEstimate` is `GC.GetTotalMemory(false)`; it can include objects
  awaiting collection and is not a measured live-object census.
- Heap, fragmentation and committed bytes describe the last GC snapshot.
  Compare `lastGcIndex` and generation collection counts across observations.
- `totalAllocatedBytes` is cumulative since process start; its delta measures
  allocation volume, not retained memory. Compare `startedAt` as well as PID,
  since container processes can reuse the same PID after restart.
- Linux cgroup usage, limit, anonymous, file and kernel counters are included
  when available. Missing or unlimited values stay null. Cgroup counters,
  process RSS and GC committed bytes overlap and cannot be added together.
- Cache byte sizes are the owners' existing admission estimates. ETA timing
  caches use work units, not bytes. Shared-cache and current-ETA byte sizes are
  explicitly unmeasured; an entry count does not establish their memory cost.

Sample sequentially at a modest interval while reproducing normal work. Growth
in heap after comparable full collections is different from temporary allocation
pressure or memory committed for reuse. A large gap between process and managed
memory needs separate native/runtime analysis; these counters alone cannot name
object types or prove a leak. The endpoint does not replace a heap/root profile.

The instrumentation was deployed with explicit approval on September 21.
Production samples and local allocation probes have different measurement
boundaries; neither alone attributes native memory to an owner.
See the [investigation record][memory-investigation].

[memory-investigation]: ../archive/2026-09/runtime-memory-investigation-2026-09-21.md

Meter `PulsarTms.Performance` exposes `pulsartms.stage.duration` (milliseconds)
and `pulsartms.stage.items` (counts), tagged only by fixed `operation` and `stage`
names. Planning exposes `queue-wait` and `background-job` separately, so provider
latency is not confused with waiting behind other trucks. Other stages cover
route-preview display projection, TomTom slot/reservation/body
waiting, fuel station matching/optimizer reuse/quantity choices, and integration
provider waits/reconciliation. Attach the configured metrics collector to this
meter to record distributions. Instrumentation alone is not a durable export or
a production benchmark; HTTP request timing remains available independently.

Unit/integration tests do not substitute for production load tests. Process metrics do not prove memory cost per truck or fleet-scale capacity. Audit logs require appropriate hosting retention and access controls before they can serve as a long-term compliance record.

### Linux mapping attribution

`GET /api/diagnostics/memory/map` is a separate Admin-only diagnostic. It reads
fixed `/proc/self/smaps`, `/proc/self/maps` and `/proc/self/status` paths inside
Infrastructure, without accepting a PID or path from the caller. Only fixed
category labels and aggregate counters leave the reader: no addresses, file
paths, mapping names or process-memory contents are returned. Sampling is
coalesced and cached for 30 seconds, including unavailable results. No worker,
provider request, database query, forced GC or dump is started.

The map is capped at eight MiB of text; invalid/oversized detailed reads do not
return partial totals. The reader falls back to `maps` with `virtual-only`
status, or reports `unavailable`. Other operating systems report
`unsupported-platform`. Missing resident/PSS counters stay null, never zero.
`ObservedAt` identifies the cached observation, not the request's arrival time.

Categories are mapping labels, not proven allocation owners. Anonymous mappings
can contain GC heaps, native allocator arenas and unlabelled thread stacks.
`labelled-process-heap` is not the complete native heap. The runtime's
`memfd:doublemapper` aliases can refer to the same physical code pages; RSS
sums are not unique physical memory, and PSS is useful when available. A DLL or
shared-library mapping can also contain private anonymous copy-on-write pages.
Virtual reservations are not resident memory. `Process` exposes the available
coarse status counters and thread count; they need not equal a separately timed
map scan. GC, status, mapping and cgroup observations are not atomic.
See [Linux proc documentation](https://docs.kernel.org/filesystems/proc.html)
and [.NET double-mapping notes](https://github.com/dotnet/runtime/discussions/81752).

This is an OS mapping summary, not a managed-heap/root census or an allocation
stack trace. Do not subtract GC committed bytes from RSS/PSS and label the
remainder an exact native allocation total. Availability in Cloud Run requires
verification on the deployed revision; local parser tests cannot establish it.
