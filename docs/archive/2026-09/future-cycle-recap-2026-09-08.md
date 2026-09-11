# Future-load cycle and recap forecast — September 8, 2026

## Implementation

`HosTravelClock.SnapshotCycle` takes an observational snapshot after each stop's
service. `StopEta.CycleAfterDeparture` carries nonnegative remaining cycle minutes
and the next positive usable recap, using the driver's configured home-day
boundary. Current driving, empty connections, facility time, earlier loads and
planned rest remain on the existing single clock. Snapshot reads do not advance
the simulation or request providers.

Recap requires verified/reconciled history. Missing, stale, inconsistent or
cross-border history leaves its fields unknown. The search is bounded by the
configured cycle length, excludes duty before a restart, and does not assume
unplanned future work or Canada's secondary-limit release. Fixed an existing
ambiguous-DST boundary case where the later repeated-hour boundary could be
treated as reached before its actual instant.

Future Dispatch Cards show the exact final stop's departure-cycle snapshot and
next recap in home time. Current cards do not duplicate the future summary.
Expired values disappear except for the existing bounded pending-update display
grace, explicitly marked Updating. Missing final-stop forecasts do not borrow
earlier-stop or another dispatch's hours.

Snapshots persist in the existing `DispatchEtaForecasts.ForecastJson`; no schema
migration is required. Older JSON without the new field remains readable. Chain
policy version 3 invalidates old snapshots through the usual background refresh,
without changing routing provider budgets or refresh cadence.

## Checks

- `bash test.sh all`: 580 Server + 226 Client + 155 Node = 961 passed, including
  architecture checks. The first run caught a Razor UTC-label interpolation error;
  it was corrected and the complete suite rerun successfully.
- New clock tests cover service depletion, home day-start, both DST transitions,
  repeated-hour no-early-credit, 60h/7-day and 70h/8-day windows, zero-return days,
  history rejection, borders, restart exclusion, forecast duty and Canada limits.
- Chain tests prove current work, empty travel and both future service allowances
  are included. Existing store tests now round-trip the nested forecast, and a
  separate legacy-JSON test verifies missing fields remain unavailable.
- Ten Client component tests cover exact final-stop identity, fresh/unknown recap,
  home offset, no extra HTTP, current-card exclusion, pending grace and expiry.
- Typed JS, styles, JS build and strict Client publish passed. The exact local
  stage is `artifacts/cycle-cards.y6vigy/publish/wwwroot`; artifact verification
  passed for 222 assets and six JS dependency graphs.
- All 40 offline UI pages passed at desktop/mobile widths in both themes and
  100%/200% text size. Inspected cycle-summary screenshots; no clipping or overlap.
  Local report: `Client/test-results/cycle-cards-ui-smoke/report.json`.
- Existing local performance probe passed: with verified history and 10,001
  geometry points, warm synthetic ETA replay including the snapshot measured
  0.030 ms and 19,680 allocated bytes per calculation, with zero geometry lookups
  across ten iterations. This is a tiny isolated fixture, not a production load
  benchmark, latency guarantee or incremental before/after comparison.

Local API and Client were restarted on ports 5086 and 5067. Production deployment
was not performed. PostgreSQL execution tests were not run: persistence checks use
the existing isolated in-memory SQLite fixture, not the application database.
No local SQL server/container was started and no manual database changes were made.

## Per-stop Cards follow-up

Dispatch Cards now show `Cycle after stop` beneath every unfinished stop in current
and future loads. Each value is its exact dispatch/stop `CycleAfterDeparture`,
after waiting and service, not a copy of the final delivery's cycle. The existing
after-delivery/recap summary is retained. The Client reuses its stop-scoped ETA
display memory and a shared duration formatter; completed stops hide the forecast,
missing/invalid snapshots show a dash, and pending values retain the same bounded
grace with an Updating marker. No server, database, timer or HTTP changes were
needed.

- `bash test.sh dispatch styles`: 253 Server + 95 Client + 12 style + 18 JavaScript
  architecture checks passed. This is an affected-category run, not a full suite.
  Sixteen new component cases cover per-stop/current/future values, identity,
  missing/negative/zero values, freshness, pending retention, replacement and
  completion, including no additional HTTP.
- Strict Client publish and development build passed; localhost:5067 was restarted
  and the Dispatch page returned HTTP 200. Exact stage:
  `artifacts/stop-cycle.9G3kdP/publish/wwwroot`. Artifact verification checked
  222 assets and six JavaScript dependency graphs.
- Offline UI checks passed all 44 page cases at 390/1440/2344px, light/dark and
  100%/200% root text size, with zero checked layout failures, browser errors or
  unexpected requests. Inspected mobile stop detail images, including enlarged
  text. Report: `Client/test-results/stop-cycle-ui-smoke/report.json`.

This follow-up was not deployed. Live provider behavior, PostgreSQL execution and
production performance were not re-tested for this Client presentation change.

## Recap baseline, late-stop explanation and pending retention

A scoped read-only inspection of load 1373's saved forecast confirmed the user's
reported September 11 recap of 185 minutes. The former card selected the recap
after forecast delivery on September 12, showing September 13 and 798 minutes.
The earlier 185-minute credit was already present in the simulation; the summary
used the wrong temporal reference.

The chain now captures `CycleAtCalculation` before advancing for ongoing rest,
travel or service. Each load retains that baseline alongside its separate
per-stop `CycleAfterDeparture`. The card shows the nearest driver recap from the
baseline and remaining cycle after the relevant stop. Chain policy version 4
invalidates older JSON snapshots for normal background refresh. No migration or
manual database update is required.

Late-stop Cards show existing cumulative driving and rest/wait values; they do not
invent an exclusive cause or derive missing cycle hours from lateness. Complete
ETA, cycle, recap, duty and route display data survive pending partial responses
within the existing validity-plus-fifteen-minute grace. Pending lateness remains
as `Previously late by` with Updating, not a fresh On time/Late claim. The retention
is exact-identity scoped, rejects older snapshots and stops on completion. Missing
pending plans no longer clear matching Dispatch/Fleet Map summaries, and future
map cards no longer add `ETA —` alongside a retained ETA. Normal complete results
replace these display-only snapshots without changing caches or request cadence.

Verification:

- Final `bash test.sh all`: 582 Server + 277 Client + 157 Node = 1,016 passed,
  including architecture. Both assemblies reference their production projects.
- Strict Client publish, typed JavaScript, JavaScript/style builds and artifact
  integrity passed. Stage: `artifacts/recap-clarity.xeKYn6/publish/wwwroot`,
  222 verified assets and six JavaScript dependency graphs.
- Final offline UI matrix: 44 page cases and 20 detail crops; separate stop-details
  checks: five scenarios. No checked layout failures, browser errors or unexpected
  requests. Inspected desktop/mobile, light/dark and enlarged-text screenshots.
  Reports: `Client/test-results/recap-clarity-ui-final/report.json` and
  `Client/test-results/recap-clarity-stop-details/report.json`.
- Fresh isolated ETA performance probe: three checks passed. Warm dense geometry
  with history measured 0.035 ms and 21,720 bytes per calculation; warm real local
  timezone lookup measured 0.005 ms, with 51.8 ms cold initialization. These tiny
  fixtures do not measure production provider, worker, database or UI latency.

The replay already advances by driving/rest/stop boundaries, not by minute or
kilometre. No speed, split-rest or shift-allowance policy was changed based on the
synthetic measurements. A requested `Cycle short ~X working hours` diagnostic is
not implemented: it requires deadline-aware capacity analysis with recap timing,
not substitution of late minutes or a negative final-cycle balance.

No production deployment or PostgreSQL fixture execution was performed. Read-only
inspection of an existing application snapshot is not a PostgreSQL test fixture.
No database server/container was started and no manual records were changed.
Local API and Client were rebuilt/restarted on ports 5086 and 5067; process liveness
and the Dispatch page returned HTTP 200. Offline fixture checks do not establish
that a live provider refreshed a particular driver's forecast after restart.
