# Local optimization verification — September 12, 2026

## Scope

Local implementation only. No deployment, new server, database migration or cloud
configuration change was performed. Firebase cache rules were edited locally for
the next explicitly approved deployment. Existing unrelated working-tree edits
were preserved. This follows the [read-only baseline audit](localhost-performance-audit-2026-09-12.md).

## Implemented

- Dispatch core rendering no longer waits for HOS, financial enrichment or ETA.
  Independent supplemental reads return compact values and ordered revision guards,
  not repeated addresses/cargo. Late replies cannot overwrite changed assignments,
  stops, search/page selection or a newer driver's displayed forecast.
- ETA batches assignment/root metadata reads and filters truck projection to
  requested/referenced IDs and imported numbers. PostgreSQL parses each selected
  route JSON once in a materialized CTE; compact metadata never transfers geometry.
- Fleet/Dispatch HOS starts independently and uses the existing shared per-instance
  snapshot. Provider freshness and refresh cadence are unchanged.
- Coalesced display reads cancel HTTP transport when the final consumer leaves;
  other consumers remain unaffected. Client JSON reads avoid full-body buffering.
- Opt-in operational JSON responses use Fastest Brotli/Gzip. A real isolated
  loopback Kestrel test verified encoding, decompression and exclusion of anonymous
  and unmarked credential responses. It does not test production proxies.
- Two bounded route-refresh consumers prevent a slow job from blocking all unrelated
  work. Per-truck mutation locks, provider limits, deduplication and cooldowns remain.
  Queue wait and job time have separate metrics. No paid provider build was forced.
- Fingerprinted framework/modules and generated chunks have local immutable cache
  rules; HTML, configuration, CSS and unhashed aliases retain revalidation.

## Read measurements

Probe: `--read-only --performance-reads`, fresh process, configured Neon connection,
explicit read-only transaction and ten-second SQL timeout, no hosted jobs. Four
trucks and five loads. A single bounded Samsara read is included separately.

| Operation | Baseline | Final sample | SQL commands, baseline → final |
| --- | ---: | ---: | ---: |
| Full board, cold application caches | 3,832 ms | 2,434 ms | 37 → 21 |
| Full board, warm | 2,134–2,473 ms | 886–955 ms | 30 → 14 |
| Core board, warm, no enrichment/HOS | — | 43 ms | 1 |
| Compact financial enrichment | — | 320 ms | 6 |
| Compact ETA enrichment | — | 577 ms | 8 |
| Saved single-truck preview, warm | 89–91 ms | 84–88 ms | 2 → 2 |
| HOS source, including credential read | 703 ms | 664 ms | 1 → 1 |

Supplemental financial JSON is 3,727 bytes versus 21,330 bytes for the corresponding
full board response; supplemental ETA is 18,806 versus 36,984 bytes. These are
uncompressed serialization measurements, not browser transfer. The new core plus
two supplemental reads still perform independent authoritative database work;
compact payloads do not eliminate all repeated server page hydration.

The initial bundle comparison used an isolated Release artifact: 62 framework
files/variants, 11.36 MiB raw and 3.38 MiB with Brotli variants. This excludes Google
Maps and API data, includes alternative ICU files, and is not an exact startup
download. The existing Debug observation was 24.23 MiB across 209 framework reads.
The size reduction is Release trimming/compression, not a claim that this patch
alone removed those bytes. Native WASM relinking/AOT was not enabled or installed.

Samples exclude HTTP authentication, browser rendering, connection-pool acquisition,
Google Maps initialization and real route builds. They are not p95/p99 or large-fleet
capacity measurements; network/database conditions can vary between runs.

## Verification

- Final `bash test.sh all`: 1,662 Server, 770 Client C#, 475 Node checks passed.
  Includes architecture, identity, finance/routing, cancellation and allocation checks.
- Strict Client and API Debug builds: zero warnings/errors; used for local restart.
- Strict isolated Client Release publish passed; 264 staged assets and seven JS
  entry-point dependency graphs passed integrity verification. Typed JS check passed.
- Offline staged Chromium scenario at 1440px/light passed cold-preview loading,
  independent HOS, forecast retention, same-element layout assertions and sixteen
  SPA map/Dispatch transitions. No browser errors or unmocked requests.
- After map disposal, no further map-route polls were observed. Settled document
  count stayed four, DOM nodes 1,155 and event listeners 36 from cycle four through
  fifteen. Post-GC JS heap increased from 6,065,576 to 6,380,008 bytes (about 0.30 MiB).
  This does not prove zero leakage or measure the managed .NET heap/GPU/provider map.
- The complete hours/forecast browser matrix also passed twelve cases, six widths
  from 390 through 2344 pixels in both themes, with no errors or unexpected reads.
  Existing enlarged-text, retained-element and pending-reply assertions stayed intact.
- Four route allocation checks also passed separately: known-version reads allocated
  2,680 bytes versus 117,680 for a full 3,001-point fixture. Replaced cached geometry
  became collectible before expiry; matching did not allocate per segment. These
  validate existing safeguards, not new measured reductions in production RAM.
- `git diff --check` passed. Local API/Client were rebuilt and restarted with
  `Database:ApplyMigrations=false`. No new migrations were introduced or applied.

Managed evidence paths (subject to generated-output retention):
`artifacts/managed/diagnostic-kejiqD`, `diagnostic-lTqTse`,
`browser-hours-forecast-9VrPhP/report.json`, and `browser-hours-forecast-dwaKBp/report.json`.

## Remaining boundaries

No isolated PostgreSQL test fixture was available; those integration checks were
not run. SQL translation and SQLite behavior tests plus an operational read-only
Neon diagnostic are distinct evidence, not substitutes for that fixture.
No production deployment/cache-header check, authenticated real-Google browser
soak, long-duration server retained-heap comparison, provider-build latency
distribution, multi-user/fleet load test or cloud bill forecast was performed.
The separate whole-application UI smoke was not run in this pass. The ordinary
telemetry response is not a new field-level delta protocol. Large-fleet readiness
or an objective application-wide score of 9/10 has not been established.
