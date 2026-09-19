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
