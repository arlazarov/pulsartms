# Cycle sections and sleeper planning — September 8, 2026

## Changes

- Dispatch and current/future map stop cards separate `Cycle remaining` from Road
  ETA, with `On arrival` and `After service` balances. Each unfinished stop owns
  its values; completed stops do not invent historical hours. Pending refreshes
  retain the previous text, values and status colors under the existing grace.
- Application's shared travel clock treats planned facility service and appointment
  waiting as sleeper time for both current and future loads. It advances departure
  without spending Cycle, combines consecutive rest and retains verified recap
  boundaries. This is a forecast assumption, not a write to ELD duty records.
- Each new planned shift charges 15 minutes PTI and 5 minutes fueling, once, plus
  driving. Already-started shifts use remaining live clocks without adding those
  allowances again. Saved fuel recommendations no longer add per-station time or
  independently invalidate a forecast. Financial fuel planning is unchanged.
- Chain policy 6 invalidates older forecast snapshots through normal refresh.
  No schema changes or manual database correction were needed.

## Verification

- `bash test.sh all`: 614 Server, 351 Client and 168 JavaScript tests passed,
  including architecture checks; zero failures or skipped tests.
- Strict Release API build and isolated Client publish passed. SCSS compilation,
  typed JavaScript checks and JavaScript build passed. Published asset verification
  checked 222 assets and six JavaScript entry-point graphs.
- Offline staged hours-card browser checks passed four viewport/theme scenarios,
  including Dispatch, selected future stops and pending retention. GPU/current
  stop-card checks passed 4 GPU, 8 popup, 20 hours and 2 constrained cases. Reports
  contain no browser errors or unexpected requests. Mobile screenshots were
  visually inspected.
- Both development processes were restarted with the new code. Migration startup
  was disabled for the API. Production was not deployed.

## Limits

Browser fixtures use deterministic data, not actual HOS/provider responses. They
do not establish real ELD compliance, production latency or complete geographic
correctness. Real PostgreSQL execution checks were not run: no isolated fixture
was available and no local database server was started. No new migration exists
for these changes. Actual driver status remains authoritative.

Local browser evidence is under `Client/test-results/cycle-sections-hours` and
`Client/test-results/cycle-sections-stop-cards`; the Client stage is under
`artifacts/cycle-sections/publish`.
