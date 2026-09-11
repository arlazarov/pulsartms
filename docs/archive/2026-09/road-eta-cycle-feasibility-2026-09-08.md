# Road ETA and Cycle feasibility — 2026-09-08

## Scope and decisions

Reviewed the changed ETA/HOS algorithms, forecast contracts and persistence,
Dispatch/current/future-stop presentation, retained planning snapshots, JavaScript
popup updates, shared styles and their automated checks. This is evidence for this
working copy, not a claim that every project file or production workflow is perfect.

The primary road forecast now includes daily HOS, saved travel, service and
appointment waiting without silently inserting Cycle delays. A separate pure
Application ledger reports signed Cycle balances and preserves earlier driving
shortages after later recap credits. Verified recap and conditional restart are
separate chain replays. A restart alternative is shown only for a known appointment
it meets; it does not represent driver intent or an ELD compliance verdict.

Fixed double daily-rest counting after a long recap wait, zero-return recap-day
handling, ongoing rest credit toward a restart, historical-arrival Cycle claims,
and loss of earlier results when the ninety-day horizon is reached. Missing or
inconsistent history remains unknown. Legacy JSON remains readable and chain
policy version 5 prevents reusing an old calculation as the new model.

Removed the cumulative `Plan to this stop / Driving / Rest / wait` explanation.
English stop cards distinguish Road ETA, Cycle balances and conditional arrivals;
known lateness stays visible even when Cycle is unknown. Pending refreshes retain
the matching full forecast with muted previous status, not a new green promise.
Current JavaScript popups and Blazor Dispatch/future-stop cards were checked.

## Boundaries and performance

- All HOS and financial calculations remain server-owned. Client code formats
  returned values and statuses; controllers and provider dependencies were not added.
- At most three prepared chain replays run, not one search per stop. Routing,
  geocoding and external provider calls are absent from the replay algorithm.
- Saved route timing is reused; map metadata updates do not rebuild route geometry.
- Planning display memory now has a conservative 262,144 coordinate-unit budget,
  alongside its existing 100-entry/five-minute limits. Counts include aliases and
  structural overhead; this is not a byte-accurate heap limit. Oversized snapshots
  do not evict unrelated entries. Replacement, expiry and clearing release weight.
- Review and the full-suite run exposed a completed-request registration race in
  the planning cache. Sequential refreshes must not reuse an already completed
  in-flight task. Regression coverage retains the exact three-request expectation.

The local allocation probe uses a saved 600-mile/ten-hour single-stop route,
three warmups and ten synchronous calculations. Verified shortage and a future
appointment trigger baseline, recap and restart replays. A 10,001-point route
measured 0.119 ms and 48,088 allocated bytes per warm calculation; sparse geometry
allocated the same amount. Sixty endpoint lookups and zero geometry lookups occurred
across ten calculations. Timing is noisy and synthetic, not measured production
latency, a multistop benchmark or an API response-time guarantee. The probe uses no
database or network.

## Verification and limitations

After the completed-request race fix, `bash test.sh all` passed 1,124 tests:
609 Server, 347 Client C# and 168 Node, with zero failures or skips. The deterministic
reentrant-await regression failed on the old ordering and passed after releasing
the matching in-flight registration before publishing success or failure.
Targeted server checks also passed 164 ETA and Architecture tests and four
allocation probes. A warnings-as-errors Client Release publish passed, as did typed
JavaScript checks and integrity verification of 222 assets/six entry-point graphs.
Final browser checks passed 44 page cases, five legacy future-stop cases and four
new Hours width/theme scenarios (twenty screenshots). The separate production GPU
fixture passed four scenes, eight base popups, twenty Hours popup cases and two
constrained-height cases. No browser errors, unexpected requests or checked bounds
failures were reported. Desktop/mobile and pending-state screenshots were inspected.
Local reports remain under `Client/test-results/cycle-hours-verified-ui`,
`cycle-hours-verified-stop-details`, `hours-forecast-cache-verified` and
`cycle-hours-stop-cards`. The final staged Client is under
`artifacts/cycle-hours-verified.v4ml4p/publish/wwwroot`.

Browser fixtures use actual staged Blazor or production GPU/popup modules with
deterministic intercepted APIs; they
do not prove real provider integration, live HOS data quality or every visual layout.

No new schema migration is required. No SQL database fixture was started, and real
PostgreSQL execution/migration checks were not run. Forecast JSON round trips are
covered locally, not claimed as PostgreSQL evidence. Production latency and
long-run browser heap were not measured. This work does not deploy the application
to the production server. Local API and Client were deliberately rebuilt and
restarted with automatic migrations disabled. Client and API liveness returned
HTTP 200; anonymous protected readiness/settings returned 401. The existing browser
tab was reloaded and its Fleet Map and foreground layer loaded successfully.
