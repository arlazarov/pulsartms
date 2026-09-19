# Load testing

Two tools exist. Neither has been run against a deployed environment yet; there is
no baseline, and the thresholds below are starting points, not measured capacity.

## Polling scenario (k6)

`tools/load/polling.js` reproduces what open browser tabs cost the API. Each
virtual user signs in once, then every ten seconds polls the dispatch board, fleet
telemetry (`?points=false` for board tabs, full history for map tabs) and one truck's
planning read. Map tabs also read fuel stations once. The script sends the same
`If-None-Match` validators the Client sends, so `304` answers are counted separately
(`amftms_not_modified`) from transferred bodies (`amftms_body_bytes`).

```bash
k6 run -e BASE_URL=https://api.example -e AMFTMS_EMAIL=... -e AMFTMS_PASSWORD=... \
  -e TABS=40 -e DURATION=10m tools/load/polling.js
```

Run it against a staging deployment with representative data and background
synchronization enabled, never against production. Credentials come from the
environment only; do not commit them or paste summaries that include them. The
account must be able to read the board, telemetry and planning. Export metrics
(`OTEL_EXPORTER_OTLP_ENDPOINT`, see [diagnostics](diagnostics.md)) on the target so
request duration, GC and thread-pool behaviour can be read alongside k6 output.

Record each run under `docs/archive` with the commit, instance size, fleet size,
`TABS`, `DURATION` and the k6 summary. Compare `304` share, p95 per poll and CPU
before drawing conclusions; a single run does not establish a trend.

## Board read harness (in-process)

`tools/LoadProbe` hosts the board handler over synthetic SQLite data and reports
latency, SQL count, allocations and CPU for 100, 300 and 1000 trucks at several
concurrency levels. It exercises the handler and `ReadCache` only: no authentication,
telemetry, ETA, providers or PostgreSQL.

```bash
node scripts/artifacts.mjs run scratch -- dotnet run --project tools/LoadProbe -c Release \
  --artifacts-path {artifacts} -- --sustained
```

One sandbox run is recorded in the
[September 19 archive record](../archive/2026-09/server-load-and-architecture-2026-09-19.md).
Its numbers describe one process on the machine that ran it. They do not predict
Cloud Run throughput, PostgreSQL query cost or browser rendering time.
