# ETA chain and map/card refinement — September 8, 2026

## Scope

- One driver-clock forecast across remaining current stops, saved deadhead
  connections and ordered future loads. Default pickup/delivery service is two
  hours each. Future appointment slack and explicitly assumed qualifying off-duty
  gaps carry forward; actual current stop and ELD records are not changed.
- Persisted per-load ETA snapshots behind `IEtaForecastStore`, with assignment,
  predecessor, route, schedule, stop activity and driver signatures. Additive
  migration `20260908160522_StoreDispatchEtaForecasts` creates the snapshot table.
- Compact saved root metadata reads through `IEtaRootRouteReader`; no full
  `PlanJson`/`RouteJson` transfer to validate an already saved forecast. Database
  JSON parsing is still work, and per-truck metadata queries are not eliminated.
- Immutable country/timing profiles compiled per saved geometry revision;
  subsequent forecasts replay durations without rescanning the road coordinates.
  Existing saved base/deadhead JSON already contains travel seconds.
- Future-load map selection is click-pinned rather than hover-driven. Separate
  details state does not change the authoritative current dispatch. Exact load/stop
  IDs join forecasts to map labels and every Dispatch Card stop.
- Neutral, regular stop-label text aligned with the fuel popup. Company/identity,
  ETA and Empty/Leg appear above Total; distance units include miles and kilometers.
- Truck glyphs are 30 instead of 34 CSS pixels, with cached solid, contrasting
  light/dark keylines. Truck-number text/padding and status colors are unchanged.

## Verification evidence

Local deterministic tests cover continuous chain timing, appointment slack,
daily-versus-cycle rest, stale input rejection, completed-stop service, bounded
timing caches, saved snapshot ownership/atomicity and map/card selection races.
The snapshot store preserves full timestamp identity in JSON while comparing
row metadata at PostgreSQL microsecond precision.

The offline production GPU fixture renders current/future stop cards beside the
real fuel popup and six representative truck glyphs. Four light/dark and DPR 1/2
cases passed without unexpected network requests or browser errors. Screenshots
and the structured report are local, ignored files under
`Client/test-results/stop-cards`. This does not exercise Google Maps integration,
real traffic tiles or crowded stops.

The separate staged Blazor UI smoke uses synthetic current/future loads, including
completed stops and a late estimate. It checks each stop's identity, appointment,
ETA and bounds in the existing desktop/mobile, light/dark and font-size matrix.
The complete `AMFTMS_RELEASE_UI=1 UI_TEST_BROWSER_CHANNEL=chrome bash
verify-release.sh` gate passed: 563 Server tests, 130 Client C# tests and 114 Node
tests; strict Release builds reported zero warnings/errors. All 40 staged UI
cases passed. Artifact verification checked 222 published assets and six generated
JavaScript dependency graphs. The verified local artifact is
`artifacts/release.9geKQU/publish/wwwroot`.

The first combined test run exposed two new fixture signatures calculated before
SQLite's decimal roundtrip; fixtures now create their signatures from reloaded
persisted entities. Production signature validation was not loosened. Separate
regressions also verify that conflicting legacy truck assignments cannot share a
row's forecast and that a changed future route removes a prior On time verdict.

## Measurement scope

Synthetic warm ETA replay removed repeated geometry lookup. Earlier local
10,001-point calculations with eight days of synthetic HOS history changed from
approximately 13.2 MB/23 ms to 17,112 B/0.024 ms on the final Release code. The final
1,001-point real local timezone lookup case used 2,768 B/0.004 ms per warm replay.
Both recorded zero warm geometry lookups; current and destination lookups remained.
These are local pure-calculation diagnostics, not end-to-end or production latency
measurements. Cold compilation, database reads, provider freshness and browser
startup are outside those warm figures. The allocation regression checks that
warm cost does not scale with geometry vertex count.

## Release boundary

### Subsequent semantic card colors

Map stop cards now use existing pickup/delivery theme roles for headings and
subtle borders, while retaining opaque surface backgrounds and regular text.
Confirmed server appointment/lateness verdicts supply green/red ETA accents;
unknown or retained route-update estimates stay blue. Late estimates also include
the word `Late`. Optional/multiline text carries explicit per-row tones without
parsing user text or recalculating ETA in JavaScript.

The subsequent `bash test.sh map styles` run passed 77 Server and 59 Client C#
tests, plus 82 map, 12 style and 18 JavaScript architecture tests. JS type checking,
JS/style builds and four offline production-GPU light/dark DPR 1/2 cases passed.
This is category coverage, not a new full release-gate run. Visual checks cover
color roles, opacity and matching colored-line/background glyph positions. The
initial run exposed an existing bUnit find/change race during a poll response;
the affected test now locates and toggles on the renderer context without weakening
its selection assertions. Label regressions also preserve no-op updates during
sub-display truck movement. No new provider requests or recalculations were added;
production GPU performance has not been measured.

### Bottom-centered stop details

Future-stop details now reuse the fixed, bottom-centered map-card surface without
replacing the current truck header. Current and future cards include appointment,
exact-stop ETA and miles/kilometers. Future distance includes the current route's
remaining distance and every preceding saved empty/loaded leg; incomplete inputs
remain unknown. Selection cancellation prevents late detail responses from
reopening a dismissed card. Opening a current-stop or fuel card clears the future
selection without closing the newly opened card. Unchanged current-stop display
values do not rebuild popup content during polling.

The subsequent full `bash test.sh all` run passed 563 Server, 136 Client C# and
124 Node tests (823 total). JS checking, style builds and strict Client builds also
passed during this iteration. This is not a new full deployment-gate run.
The staged `stopDetailsSmoke.mjs` fixture passed four desktop/mobile light/dark
cases covering both future stops, with 16 initial/scrolled screenshots and no
browser errors or unexpected requests. Desktop content fits without scrolling;
mobile preserves the shared 240px scrolling card. The final fixture artifact is
`artifacts/stop-details.YqBPWv/publish/wwwroot`. Its map callbacks and API data are
synthetic, not a Google Maps or database integration test.

### Migration and deployment status

After explicit user authorization, EF inspection found exactly one pending
migration on the database configured for the Development API:
`20260908160522_StoreDispatchEtaForecasts`. The reviewed migration only adds
`DispatchEtaForecasts`, its foreign keys and indexes. An explicit EF update to
that migration completed successfully; a subsequent history read showed no
pending migrations. Existing application data was not rewritten or deleted.

An authenticated localhost check after migration restored truck 54777's current
route distances, HOS and ETA. The previous missing-table failure no longer blocked
that planning read. This is an authorized application migration and live read,
not a disposable database test. No suitable isolated PostgreSQL fixture was
available, so separate PostgreSQL integration tests were not run. Neither API nor
Client was deployed; localhost remains running. Production performance and the
complete live Google Maps interaction lifecycle were not measured in this check.

### Current-stop card readability follow-up

At the user's request, current-stop persistent text labels were removed while
numbered pickup/delivery markers and click cards remain. Future selected-load
labels are unchanged. Current cards omit commodity/service notes and separate
company/address from labeled appointment, ETA/status and distance fields. Same-day
appointment windows render one date, such as `Sep 9 · 07:00 – 14:00`; cross-day/year
boundaries remain explicit without timezone conversion. Structured ETA display
fields reuse the existing server estimate and verdict, not parsing label text.

`bash test.sh map styles` passed 77 Server, 65 Client C#, 94 map JS, 12 style and
18 JS architecture tests (266 total). JS type checking and JS/SCSS builds passed.
The date regression exposed repeated identical endpoint minutes, now collapsed.
The offline real-GPU fixture passed four theme/density cases plus four current
popup desktop/mobile theme cases, producing 12 screenshots without browser errors
or external requests. A live localhost check on truck 54777 confirmed the absent
current labels and the compact delivery card with updated ETA/mileage and no cargo
or service notes. Mobile distance remains accessible by scrolling the shared
240px card. This follow-up did not run the full release gate, deploy application
artifacts or change the database. No new provider requests were added by these
display changes; production rendering performance was not measured.

### Final current and future popup consolidation

The subsequent user-requested layout supersedes the floating future labels above.
Current and future pickup/delivery stops now show numbered circles only; clicking
opens the shared bottom-centered card. Selected roads and circles remain pinned.
Removed future-label formatting and caches are no longer updated during polling.

The shared desktop width is 600px through a named size token. The left column
shows company and two address lines; building/unit text remains with the street.
Appointment is at the top right, followed by inline ETA and Total values. Future
cards also show the selected Empty or Leg distance, without the redundant footer.
Commodity and full service notes remain hidden. Explicit appointment references
appear as `Appt #` under the address for both pickup and delivery, respecting
shipper/receiver qualifiers. The bounded display parser rejects dates, BOL and
load/order labels; ambiguous input does not invent a reference. Address splitting
is presentation-only and does not modify stored addresses or copied full text.

Final `bash test.sh all` passed 563 Server, 191 Client C# and 136 Node tests (890
total), including architecture checks. JS type checking, JS/SCSS compilation and
strict Client development build/Release publish passed. The final artifact was
`artifacts/compact-popups.T73T7e/publish/wwwroot`.

The offline GPU/current-popup fixture passed four GPU and eight popup scenarios,
producing 20 screenshots. The staged future-popup fixture passed five scenarios
and produced 20 screenshots, including a missing-reference case. Both fixtures
reported zero browser errors or unexpected network requests. Desktop/mobile,
light/dark, correct pickup/delivery references, omitted notes, right appointment,
inline distances, selection/dismissal and full-address copying were checked.
A live localhost truck 54777 delivery card also confirmed the updated address,
appointment, ETA and Total display. Localhost was restarted with the final source.

This UI follow-up did not deploy, change contracts or the database, add provider
requests, or run PostgreSQL integration checks. The full release/deployment gate
and production performance measurement were not run; offline browser fixtures
do not establish correctness of every live Google Maps interaction.

### Compact label/value spacing

Removed stretched row alignment from current ETA/Total and future Empty/Leg/Total
fields. Values now immediately follow their labels using the existing `sm` spacing
token; appointment placement and data are unchanged. `bash test.sh map styles`
passed, including dependent C# and architecture checks; SCSS compilation and
strict isolated Client publish also passed. The current GPU/popup matrix (4 + 8
scenarios) and five staged future-popup scenarios passed new adjacency assertions
against `artifacts/compact-popups.eZoPXn/publish/wwwroot`. The live localhost
pickup card confirmed an 8px label/value gap. Full-suite and deployment checks
were not repeated for this style-only change; nothing was deployed.

### Vertical spacing clarification

The user clarified that the remaining concern was vertical whitespace, not a
separate column for ETA status. Current and future location/information stacks
and current fact rows now use `xs` (4px) gaps; future header and distance-section
padding are also reduced. Font sizes, line height, status placement and data are
unchanged. `bash test.sh styles` passed 40 Server architecture, two Client
architecture, 12 style and 18 JavaScript architecture tests. SCSS compilation and
strict isolated Client publish passed. The offline current fixture verified all
four GPU and eight popup scenarios with compact vertical-spacing assertions.
The future fixture passed five scenarios/ten selected stops against
`artifacts/compact-popups.BI9FYd/publish/wwwroot`; both browser reports had zero
errors. Desktop and mobile screenshots were inspected.
This style-only check did not repeat the full suite or deploy.

### Final placement, spacing and current-dispatch links

Later requests supersede the intermediate placement above. Future Load/Order stays
at the top left opposite Appointment. Both current and future cards put the
`Route & load details` link at the bottom right beneath a themed divider. Current
links use the exact dispatch ID now included in full and metadata-only Client
bridge payloads. Missing/invalid identity hides the link; route IDs are never used
as substitutes. Link rendering does not request dispatch details or route providers.

Current ETA/Total and future distance values align through shared grid columns.
Content rows have no extra vertical gaps; normal font size and line height remain.
Only the details-link divider adds 4px margin and padding. The redundant
`Times are local to the stop` hint is removed. Pickup/delivery appointment references
remain beneath the address. Future purple route alpha increased from 190 to 240
without changing its RGB, width, outline, current-route priority or selection logic.

`bash test.sh all` passed 563 Server, 192 Client C# and 140 Node tests (895 total),
including the additive Client bridge regression. After the last hint removal,
all 140 Node tests passed again. JS type checking, JS/SCSS builds, strict Client
development build and isolated Release publish passed. The future layout fixture
passed five cases/20 screenshots against `artifacts/compact-popups.Aprxcc/publish/wwwroot`;
the final current-source fixture passed four GPU/eight popup cases/20 screenshots.
Both had zero browser errors or unexpected requests. The current fixture follows
the real popup link through an intercepted fixture navigation to its exact path.
Separate immutable probes avoid animation-dependent spacing measurements.

A fresh localhost truck 54777 delivery popup visibly contained the current-dispatch
link, omitted the timezone hint and reported identical ETA/Total value x-coordinates.
Localhost remains running with the final source. No deployment, server/DB changes,
PostgreSQL fixture checks or production performance measurements were performed.

### Compact stop-card section dividers

The next visual request adds matching 1px themed dividers below the stop type,
above a present appointment reference, and above distances in current and future
cards. A shared kind style and section-start class avoid duplicate presentation
rules. These dividers use micro (2px) spacing; normal rows and the existing
right-side details-link divider retain their spacing. No requests, calculations
or persistence behavior changed.

`bash test.sh map styles` passed 77 Server, 113 Client and 134 Node checks,
including architecture checks. Typed JS, generated JS/SCSS, strict isolated Client
Release publish and the local development build passed. The current browser
fixture passed four GPU and eight popup cases; the future fixture passed five
cases against `artifacts/compact-popups.qrkHHm/publish/wwwroot`, with 20 screenshots
per fixture and no browser errors or unexpected requests. Desktop and mobile
screenshots were inspected. Assertions check actual themed borders, aligned value
columns, compact spacing and no orphan reference divider. Spacing measurements
use separate immutable, non-animated probes.

Localhost was restarted and the open map refreshed successfully. This UI-only
follow-up did not repeat the full suite, deploy or run database checks.

### Minute-sampled mileage and retained recalculation values

The 790/791 oscillation came from rendered route-progress updates, not a server
ETA response field. `routeLayer` now publishes popup mileage and the shared
header/future-distance callback once per 60 seconds while truck playback and the
road continue independently. Geometry totals are compiled once per identity.
Route, truck, dispatch and next/passed-stop changes publish immediately; polling,
fuel metadata, camera changes and ordinary movement do not reset the interval.

FleetMap owns a display-only retained ETA/progress pair, without modifying the
raw planning state or shared planning cache. Empty pending or null ETA responses
reuse the same-identity pair until replacement, bounded by the original ETA
validity plus 15 minutes. Repeated polls do not extend this deadline. Pending
estimates show Updating rather than old on-time/late status; JS popup expiry uses
the same bound. Mileage stays frozen during retention and resumes immediately
when new results arrive. Explicit unavailable forecasts and changed identities
do not reuse another forecast. No additional provider requests are introduced.

The server ETA policy is unchanged: ordinary successful results are cached for
two minutes; structural/off-route/stale-input invalidations may refresh earlier.
This is a display sampling change, not a new absolute server calculation limit.

`bash test.sh all` passed 563 Server, 210 Client C# and 152 Node tests (925 total).
The first run exposed a synchronization error in a new recovery test: absence of
Updating also matched the already-expired empty state. The corrected test waits
for the actual fresh arrival, then asserts status; production did not change for
that test correction. Typed JS, generated JS and strict isolated Client publish
passed. Current GPU/popup browser checks passed four GPU/eight popup cases with
no errors. The future staged fixture passed five cases/20 screenshots against
`artifacts/compact-popups.4HNPVc/publish/wwwroot`, with no browser errors or
unexpected requests. Desktop/mobile screenshots were inspected. The strict local
development build passed and localhost was restarted with the updated source.
No deployment, persistence change, PostgreSQL fixture or production
performance benchmark was performed.

### Production release — 2026-09-08 20:44 UTC

After the user's deployment request and Google Cloud reauthentication, the full
accumulated ETA-chain and Client changes were published. Before deployment, the
live API was still `amftms-api-00077-bgr`, digest
`sha256:8f7fdd849392b4618a5e2f169bed1937895a079278bfefde950333b0530bcace`;
this remains the recorded rollback identity. The additive forecast migration was
reviewed; no manual migration or data rewrite was run during this deployment.
Normal application startup migration policy was left unchanged.

`AMFTMS_RELEASE_UI=1 UI_TEST_BROWSER_CHANNEL=chrome bash verify-release.sh`
passed all 925 tests, strict Release builds with zero warnings/errors, 222 asset
integrity checks, six JavaScript dependency graphs and all 40 offline UI cases.
The exact retained artifact is `artifacts/release.Ieddea/publish/wwwroot`.
Its separate future-stop browser matrix passed five cases/20 screenshots without
errors or unexpected requests. Authenticated provider/GPU acceptance and a
separate PostgreSQL fixture were not run for this release.

`bash deploy-server.sh` passed the Cloud Build gate and deployed build
`d4242913-e29f-4f86-8887-20bf584df8bd` as `amftms-api-00078-hh4`, serving 100%
of traffic in project `amftms`, region `us-east4`. The verified immutable digest is
`sha256:d202d9737edad12fd017c82c437b4833ebb5608460572e2779db233895aecc10`.
Firebase then successfully published the exact verified Client stage using
`firebase deploy --only hosting --public` to the existing `amftms` site.

Post-deployment checks on `https://tms.amfcarrier.com` compared index plus 68
JS/WASM/CSS resources with the staged SHA-256 bytes: all 69 matched, returned HTTP
200 and avoided HTML fallbacks for assets. Liveness returned `200 Healthy` on
both the API service and custom domain; anonymous readiness and fuel-station
reads returned 401. The new revision's error-log query returned no entries at
inspection time. A fresh anonymous Chrome session booted the published WASM and
reached Login without page errors. This is boot/asset verification, not an
authenticated production map or long-running performance test. The user's local
Fleet Map tab was not navigated away.
