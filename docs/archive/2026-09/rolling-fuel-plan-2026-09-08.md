# Rolling truck fuel plan implementation — 2026-09-08

Deployment follow-up: the [September 9 release](fuel-eta-release-2026-09-09.md)
records the applied migration, deployed revisions and final verification.

## Scope

Implemented the approved fuel-planning redesign in the working copy. Search stays
explicit; this work did not deploy the application or apply a database migration.
Maintained behavior is documented in [fuel rules](../../features/fuel-planning-rules.md),
[onward planning](../../features/fuel-regions.md), and [architecture](../../ARCHITECTURE.md).

- The truck owns a durable ordered fuel itinerary spanning the current load and
  up to two future assignments. Checked fuel geometry does not replace dispatch
  route geometry or financial mileage.
- Repeated visits to a station retain independent leg/stop ownership. Completion
  advances within the itinerary; passing a purchase is not proof it occurred.
  Current measured fuel rebases the remaining recommendation, while explicit
  manual quantities take precedence over older telemetry for a bounded interval.
- Replacement writes the compatibility copy and truck snapshot atomically.
  Failed recalculation retains the prior snapshot, subject to independent read
  validation. Both the immediate response and ordinary read use that validation.
- Reuse checks current prices, settings, exact ordered remaining assignments,
  current road position, and the next pickup beyond the horizon. Moving that
  pickup under the same load ID invalidates its escape/reserve policy.
- Schedule comparison uses one driver-clock/history snapshot. Known added
  lateness is not lost when another appointment is unknown, and a prior cycle
  shortage does not hide a newly introduced shortage at another stop. The UI
  displays the dated historical check, not a continuously verified HOS claim.
- UI failure revalidation cannot let an older in-flight response overwrite a
  newer result. Invalid recommendations hide purchase quantities without clearing
  the current route.

## Bounded work and observed checks

- Two manual searches per application process, serialized per truck, with
  cancellable waits. A three-context SQLite test verified the shared limit,
  cancellation before routing and release of the waiting truck's gate.
- Explicit warm recalculation made zero new fake-router calls. Changed prices
  used the checked cold-search path. Rollover reads made no routing calls.
- Preview timing samples at most every two road miles, with a 5,000-sample and
  100,000-point bound per candidate. The dense 100,000-point/100-mile test used
  50 timing-region lookups (52 including current-position/stop lookups during
  replay), preserving original road miles/seconds and the live timing cache.
- Region price buckets are indexed once per grid rather than scanned/sorted
  on every cell lookup. Existing classification tests passed; no production
  timing claim is made for this change.
- The geometry/price memory cache has an 8 MiB accounting limit. Ordinary durable
  reads exclude the large geometry column, and only the requested current leg is
  compiled/cached. Summary and combined saved roads have separate 512 KiB and
  8 MiB payload limits.
- Existing road-search/provider attempt budgets remain in force. Normal planning
  reads retain the existing HOS provider cache policy; expiry can still cause its
  usual telemetry refresh, independently of fuel search.

## Verification

- Full `bash test.sh all`: **746 Server + 372 Client + 173 JavaScript = 1,291 tests
  passed**, zero failed/skipped. Includes strict solution compilation and
  architecture checks. Targeted runs during iteration are not additional unique
  tests in this count.
- Strict Client Release publish to `artifacts/fuel-rolling/publish` passed;
  JavaScript type checking, generated assets and SCSS compilation passed.
- Artifact verification passed: 222 published assets and six JavaScript
  entry-point dependency graphs.
- Offline staged UI smoke: 44 scenarios, zero reported failures/browser errors/
  unexpected requests. Report: `Client/test-results/ui-smoke/report.json`.
- Extended staged ETA/fuel smoke: four scenarios at 1440px/390px in light/dark
  themes, zero failures/browser errors/unexpected requests. Verified future fuel
  quantity/distance, historical impact, retained content during pending ETA, and
  horizontal bounds. Screenshots were inspected. Report:
  `test-results/fuel-rolling-hours/report.json`.
- Six representative compiled-CSS renders also checked actionable, unknown and
  invalid states at desktop/mobile widths. These are fixtures, not live map data.
- SQLite integration checks cover transaction rollback after the route update,
  older-writer protection, rollover, geometry/stop continuity, visit ownership,
  compact reads, response freshness and cancellation.
- Two offline Npgsql checks passed: the model matches its migration snapshot and
  the additive PostgreSQL upgrade SQL is generated with the expected table,
  indexes and relationships. The connection stayed closed.

## Release boundary and limitations

`20260909020907_StoreTruckFuelPlans` is prepared but **unapplied**. Apply it before
starting/deploying the new API code. The currently running localhost processes and
the deployed server were not restarted or changed by this work.

No safe isolated PostgreSQL execution fixture was available, so real PostgreSQL
migration/upsert execution was **not run**. No local SQL server/container was
started and no application/production database was used as a test fixture.

No live routing, pump availability, actual station visits, measured production
latency/memory, billing comparison, authentication flow or full Google Maps/GPU
integration was verified in this turn. Offline browser scenarios stub the provider
and intercept all APIs. Bounded samples can approximate regional boundaries;
fuel/schedule forecasts remain planning estimates, not a legal HOS certification
or proof of a globally cheapest route. Existing 800-liter working-capacity and
35 L/100 km calibration were deliberately unchanged.
