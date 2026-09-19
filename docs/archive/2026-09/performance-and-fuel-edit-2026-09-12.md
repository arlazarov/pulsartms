# Same-server performance and immediate fuel editing

Date: September 12, 2026. Implementation record for the current working copy;
not a deployment record or a production latency benchmark.

## Approved scope and implementation

| Area | Change | Preserved boundary |
| --- | --- | --- |
| Route options response | Lightweight per-leg display projection; saved baseline is distance/time only | Exact provider geometry stays in server draft/save |
| Route option selection | Selection metadata delta; existing polylines reused | New geometry is published for a different preview |
| Dispatch planning | One batch of at most 12 page summaries | Separate from complete map-geometry cache |
| Dispatch telemetry | Visible-truck status-only response | Uses existing shared telemetry ownership |
| Stop edit invalidation | Affected truck/load keys only | In-flight stale responses cannot repopulate invalidated keys |
| Client rendering | Unchanged telemetry avoids whole-board render; stop presentation reused | Stop identity/order and edited content invalidate derived data |
| Fuel context | Initial board data reused by horizon and region rules | Final assignment/revision checks remain fresh |
| Fuel matching/optimizer | Coarse radius rejection, reused corridor membership and request-local memo | Exact reachable matching and financial rules retained |
| TomTom concurrency | Two in-flight bodies, short reservation gate, retiring request-key gates | Rate/daily limits and identical-request exclusion retained |
| HOS reads | Same-server background snapshot refresh | Expired clocks unavailable; no invented rest/freshness |
| Torque reconciliation | Per-load fingerprints and indexed matching | Manual fields preserved; catalog/manual/periodic invalidation |
| Background route discovery | Identity-only board pages; independent Torque scheduling | Existing server/lease, no service split |
| Integration memory | Streamed bounded JSON, incremental feed merge, keyed HOS settings | Oversized responses fail without advancing the usable watermark |
| Browser polling | Hidden-tab pause and coordinated resume | Existing selection, request ownership and cache lifetimes |
| Fuel slider | Selected-stop server choices with downstream redistribution | No financial formulas or request per slider movement; server validates save |
| Measurement | Fixed-label stage duration/work histograms | No credentials, raw payloads or high-cardinality identities in tags |

The editor uses 25 gallons minimum and five-gallon manual choices to support
25 → 35 directly. Increasing the first purchase by 10 decreases the next by 10
where reserve/capacity allow. Later minimum purchases carry the remaining delta
forward. An invalid choice is visible but cannot be saved. Automatic planning's
ten-gallon rounding, route restrictions and economic stop-selection policy remain.

## Verification

The final complete gate passed 1,589 server tests, 718 Client tests and 439 Node
tests. The strict Release build reported no warnings/errors, and the staged
artifact check verified 264 assets and seven JavaScript dependency graphs.
Intermediate runs caught fixture incompatibilities and polling/retention
regressions; their partial passes are not release evidence.

The final staged browser pass verified route editing (8 cases), fuel editing
(8 cases), the general UI matrix (44 pages across 12 combinations), and forecast
retention (10 cases). The final artifact was `release-WKNfjP`; all matrices used
intercepted deterministic APIs, with no browser errors or unexpected requests.
Route selection transferred 147 bytes of metadata instead of the fixture's
1,970–1,974-byte initial geometry message. Every fuel case verified
25→35 / 100→90 with zero preview requests.
An additional deadline-sensitive ETA probe caught a brief Dispatch blank while
awaiting telemetry. Dispatch now enters its bounded retention state before that
wait and rechecks visibility afterwards. Regression tests cover all three views;
all 20 final browser replacement probes retained their cards without an empty or
intermediate removed-card sample. Desktop and mobile Dispatch, Route options and
Fuel editor screenshots were also inspected; these checks do not establish
complete visual correctness with real data or live map rendering.

Deterministic work-count checks observed two provider response bodies entering
before either completed, with a cancelled third waiter spending no request or
reservation. A distant-station fixture reduced detailed segment examination from
4,000 to zero while retaining nearby exact-match equality. An intentionally large
in-memory straight-road projection fixture shrank 13,975,551 serialized bytes to
1,660; it exceeds the production draft's 8 MiB ceiling and is not an API-eligible
or representative route benchmark. These are fixture work/payload measurements,
not end-to-end production speedups.

No production benchmark, paid provider probe, live authentication or PostgreSQL
execution check is claimed. There is no safe isolated PostgreSQL fixture; no
database server was created and the application database is not a test fixture.
This performance change adds no migration and does not split servers, move the
database, deploy or change cloud billing settings. Earlier unrelated migrations
in the working tree are outside this record.

## Maintained references

- [Route and fuel editing](../../features/route-planning.md)
- [Client ownership](../../architecture/fleet-map-client.md)
- [Synchronization and limits](../../features/synchronization.md)
- [Metrics and diagnostics](../../operations/diagnostics.md)
- [Test selection](../../testing.md)
