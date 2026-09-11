# Compact cycle display and reset countdown — September 8, 2026

## Changes

- Dispatch and map stop cards show the arrival balance inline with `Cycle remaining`.
  Equal arrival/departure balances are shown once; a different after-service value
  remains visible. Unknown values and earlier driving shortages retain their
  existing semantics. Recap date and credit sit below the recap label, with the
  duration kept together and the home timezone available as a tooltip.
- Current duty status includes a server-calculated cycle-reset countdown alongside
  the ten-hour-rest countdown. It uses fresh truck-position country, never the
  destination: US property cycles use 34 hours; configured Canadian Cycle 1 uses
  36 and Cycle 2 uses 72. Unknown or unsupported inputs do not invent a countdown.
- Country selection reuses the existing region lookup; no new provider request,
  financial formula, actual duty write or schema change was introduced. Policy 7
  refreshes saved forecasts carrying the additive optional duty-status fields.

## Verification

- Full `bash test.sh all`: 626 Server, 360 Client and 169 JavaScript tests passed,
  including architecture; zero failures or skips.
- Strict Release API build, isolated Client publish, typed JavaScript, JS/style
  builds and 222-asset/six-entry-graph verification passed.
- Staged browser checks passed all four viewport/theme scenarios, including equal
  cycle suppression, changed balances, reset countdown and pending retention.
  The fixture now waits for the independently loaded truck-header forecast before
  comparing the entire board through pending polling.
- GPU/current-stop browser checks passed 4 GPU, 8 popup, 20 hours and 2 constrained
  cases; no browser errors, unexpected requests or checked overflow. Mobile
  screenshots were visually inspected.
- Local evidence: `Client/test-results/reset-countdown-hours` and
  `Client/test-results/reset-countdown-stop-cards`; publish stage:
  `artifacts/reset-countdown/publish`.

These are deterministic fixture checks, not proof of actual ELD compliance or
production performance. Real PostgreSQL checks were not run; no isolated fixture
was available. No new migration is needed. No production deployment was performed.

Rule references: [FMCSA](https://www.fmcsa.dot.gov/regulations/hours-service/summary-hours-service-regulations)
and [Canada section 28](https://laws-lois.justice.gc.ca/eng/regulations/SOR-2005-313/section-28.html).
