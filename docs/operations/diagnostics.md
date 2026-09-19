# Operational diagnostics

- `GET /api/health/live`: process liveness, anonymous, no database or upstream calls.
- `GET /api/health/ready`: database connectivity, Admin policy, health status only (no connection details).
- `GET /api/diagnostics/requests`: Admin-only request count, failures, cancellations, total and maximum milliseconds by request type. Counters are per process and reset on restart; they are not fleet-wide or durable metrics.
- Meter `AMFTMS.Application`, histogram `amftms.request.duration` (milliseconds), tags `request`, `outcome`. Export through a metrics collector when one is configured. Slow requests and routing requests also emit `RequestTiming` logs, so diagnosing them does not depend on a collector.
- `AdminAudit` is an Application pipeline behavior. It records caller identity ID, command name, target ID when present, outcome, trace ID and allowlisted role/activation/planning-setting values. It never serializes commands, profiles, passwords or tokens. These are action records, not before/after database snapshots. Hosting retention controls durability; no separate audit database is introduced.
- Background route failures include dispatch ID and exception stack. Synchronization job failures include job name and exception stack.
- `Microsoft.EntityFrameworkCore` logs at `Warning` outside Development, so executed SQL is not written to production logs. Development keeps `Information` for command inspection.
- API responses are compressed with Brotli or Gzip when the client accepts it (`HostingTests`). Response sizes in logs or metrics captured before compression describe uncompressed JSON.
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

The cached board index contains assignment, schedule and search fields. Full addresses, cargo and notes are hydrated only for the selected page. Grouping and pagination still run over the lightweight index in memory; this preserves stop-level, number-only and unassigned truck associations. A database-owned assignment index would be the next step if load tests show this stage becoming significant.

Dispatch suggestions use the same filtered page returned by the server (up to 12 trucks), including matches for loads anywhere in the queue. Exact truck numbers take priority, followed by truck/trailer/driver matches, then load/order/customer/location matches. Truck/trailer/load/order numbers use prefixes; names and locations use substring matching. Telemetry polls independently and is reused during search.

## Token storage assessment

Access and refresh tokens currently remain in localStorage. Any script executing on this origin can read them; moving to sessionStorage only changes persistence and does not protect against XSS. Existing logout/security-stamp validation and active-user checks mitigate session lifetime, not token extraction.

A complete migration should put refresh credentials in Secure, HttpOnly cookies, keep access tokens in memory, add CSRF defenses for cookie-authenticated refresh/logout, and verify same-origin production hosting plus cross-origin localhost development. Include reload, multiple tabs, logout, role changes and refresh concurrency in tests. Do not silently replace storage with cookies without implementing these linked changes. This pass assesses the migration but does not change the authentication protocol.

## Verification boundaries

Unit/integration tests do not substitute for production load tests. Process metrics do not prove memory cost per truck or fleet-scale capacity. Audit logs require appropriate hosting retention and access controls before they can serve as a long-term compliance record.
