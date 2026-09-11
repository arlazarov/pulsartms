# Dispatch card readability — 2026-09-08

## Changes

- Removed the narrow 45rem stop strip. Wide truck cards place two stop panels
  beside the mileage/rate and future cycle summary; narrow cards stack them.
- Main stop locations and financial values use 16px-equivalent named tokens;
  facilities, appointments and ETA use 14px-equivalent tokens. All scale with
  the root font. Shared arrival typography defaults remain unchanged outside
  Dispatch through optional composition properties.
- Bounded the financial grid to three columns, with two/one-column reflow when
  there is insufficient room. Kept top route appointment/ETA near its destination
  instead of stretching the two groups across the available width.
- Hid actual pickup/delivery timestamps in Cards without changing completion
  detection, persisted events or ETA logic. Completed stops retain their status
  and do not show a missing-ETA placeholder.
- Kept stop ordinals and displayed each stop's structured `StopNo` as `Ref #`.
  Explicit job-specific appointment references use the existing bounded
  `StopAppointmentReference` extractor, cached by notes/job. `Appt #` remains
  distinct from `Ref #`; unstructured notes are not displayed or guessed as IDs.

## Verification and limits

- `bash test.sh dispatch styles`: 253 Server, 79 Client, 12 style and 18 JavaScript
  architecture tests passed. This was an affected-category run, not a full suite.
- Strict Client Release publish and local development build passed. Published
  asset verification checked 222 assets and six JavaScript dependency graphs.
- Final stage: `artifacts/dispatch-readability.O5ymrZ/publish/wwwroot`.
- The final staged browser matrix passed 44 pages with zero browser errors,
  unexpected requests or checked layout failures. Dispatch was checked at
  390/1440/2344px in light/dark themes and 100%/200% root font sizes; the existing
  Users, Settings and Fleet Map fixture checks remain included at 390/1440px.
  Eight mobile stop/summary detail screenshots supplement the full-page captures.
  Inspected desktop and mobile output, including enlarged text. Report:
  `Client/test-results/dispatch-readability-final-ui-smoke/report.json`.
- No server, provider cadence, finance formulas, database schema or saved event
  values were changed. No new HTTP calls or timers were introduced. No migration
  is needed; no PostgreSQL execution checks or production performance benchmark
  were run for this presentation-only change.

The Client was rebuilt/restarted on localhost:5067 and its Dispatch page returned
HTTP 200. Production deployment was not performed. Offline browser fixtures do not
prove provider rendering, real authentication or production visual correctness.
