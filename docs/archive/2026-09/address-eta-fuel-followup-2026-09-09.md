# Address, ETA and fuel follow-up — September 9, 2026

Follow-up to the [fuel/ETA deployment](fuel-eta-release-2026-09-09.md).
The original release's one HTTP 400 was later identified by the owner's displayed
error as address confirmation failure, not a server crash.

## Confirmed causes and changes

- AMF1379 pickup used `Arizona Way`; Google returned the same rooftop premise at
  `Arizona Wy`, with the same house number, New Jersey and ZIP 08832. The existing
  street comparator missed that suffix alias. Municipality display differed, but
  existing postal/state matching already supported that distinction.
- AMF1380 pickup used an undelimited nine-digit ZIP. Changing only
  `953309257` to `95330-9257` changed Google's result to a non-partial rooftop
  match at the same premise. Address Validation accepted the canonical input with
  confirmed house, street and postal components. Six bounded approved diagnostic
  calls established both facts; raw responses and credentials were not recorded.
- Normalization now recognizes Way/Wy and formats US ZIP+4 in locality components
  before provider lookup and cache identity. Street/building numbers remain
  untouched. Confirmation requirements and the ban on city-center fallback remain.
  Independent review caught a street-suffix/state-token collision; region matching
  now uses explicit locality/state components, not the normalized street.
- AMF1380's pickup appointment precedes the current delivery appointment. Strict
  pickup order still identifies its predecessor; appointment overlap now affects
  ETA instead of suppressing the physical delivery-to-pickup road and empty miles.
  Tied/unknown pickup order, mixed trucks and missing delivery dates remain guarded.
- A changed address retry timestamp now changes the preparation fingerprint.
  Clearing an explicitly selected retry wakes the next scan rather than retaining
  the old in-memory deadline. Unchanged failures keep their retry budget.
- ETA cards/tooltips no longer expose internal missing-route/preparation reasons;
  unknown estimates use one compact dash and unknown cycle sections are omitted.
  Complete eligible prior forecasts remain unchanged during refresh. Known automatic
  maintenance hints disappear only while an unchanged usable route is displayed;
  actionable warnings and manual fuel errors remain.
- Fuel search previously used all road checks on cheap distant combinations.
  The regression reproduced 1,453 miles/two trips, 97.2 starting gallons, a
  106-gallon arrival minimum, 190.2 working capacity and unchanged 30-minute/40-mile
  limits. All eleven non-direct checks missed a feasible nearby pair. Search now
  retains proximity metadata, near-road section candidates and bounded corridor
  alternatives, reserving an existing check for a feasible preliminary near-road
  chain. Actual road checks still determine quantities and limits; feasible cheaper
  alternatives still win by the existing economic/schedule comparison.

## Recovery boundaries

Source addresses, coordinates and verification timestamps must not be fabricated
or manually marked confirmed. After the verified server rollout, clear only the
two unchanged, unverified pickup retry timestamps, guarded by exact stop/source
identity, and let ordinary background verification and route preparation persist
the confirmed results. No blanket cooldown reset or new polling loop is required.

This change adds no migration. It does not rotate credentials. No local SQL
server/container or production database test fixture is permitted. Operational
read-only checks and any authorized production recovery are not isolated database
tests.

## First verified rollout and operational recovery

The full local release gate passed 789 Server, 423 Client and 180 JavaScript tests,
with strict solution build, 222 published assets and six import graphs verified.
The exact `artifacts/release.RJwh3D/publish/wwwroot` artifact also passed 44 offline
UI smoke cases and four additional hours/refresh scenarios (32 screenshots).
The latter held replies beyond forecast expiry: eligible prior forecasts and fuel
values remained displayed until replacement. These are deterministic fixture
checks, not production performance measurements or PostgreSQL fixture tests.

Cloud Build `465fe086-862e-4d91-bea0-ea41c2415b83` repeated the complete non-browser
gate and deployed API revision `amftms-api-00081-knx` at 100% traffic, image digest
`sha256:fa9e10673e4f7d6e44d78730a37a7968ef8431e4ca12f48bf68b3861173609f9`.
`/api/health/live` returned 200. No new migration was needed.

Exactly two unchanged unverified pickup retry timestamps were cleared with
stop/dispatch IDs, previous retry timestamps and original address components as
guards. Ordinary background verification then confirmed AMF1380 at
`2026-09-09T04:59:07.653423Z` and AMF1379 at
`2026-09-09T04:59:10.624212Z`. Both base routes were prepared; empty miles were
34.001 and 116.464 respectively. No coordinates or confirmation timestamps were
manually supplied. AMF1380 and its successor AMF1378 subsequently had two saved
stop ETAs each.

The first normal 54777 fuel request then reached a checked feasible route but
failed snapshot persistence. A bounded projection of the single fresh cached
route established 79,392 route-summary seconds versus 79,393 summed leg seconds
(five legs, 24,360 points, negligible floating-point mileage difference). The
store's 0.01-second aggregate tolerance rejected this ordinary rounding
difference. Its generic 500 is not a recurrence of the original no-feasible-plan
failure. The correction canonicalizes bounded whole-second rounding at provider
(including existing cache), fuel-collapse and saved-route-join boundaries; the
durable store remains strict. Forty-four focused checks passed, including the
exact discrepancy followed by an isolated SQLite snapshot save/read. Final live
fuel acceptance awaits the follow-up rollout.

All previously blocked future forecasts recovered: AMF1379 and AMF1377 had two
saved stop ETAs by `05:08:56Z`, verified in the live Dispatch cards. Initial ETA
refresh timeouts stopped after `05:04:28Z`; scoped telemetry showed a cold HOS
pagination burst, with one HTTP operation lasting 18.8 seconds, then ordinary
short incremental reads. No database lock/long-transaction wait was observed.
An isolated synthetic GeoTimeZone timing check did not reproduce a compilation
stall (24,360 points took about 61 ms locally). The evidence is consistent with
cold history fetching, not proof of a particular blocked await. No speculative
timeout increase, sampling degradation or history-credit fallback was introduced.

Client publication is held for the owner's additional popup warning alignment
and removal of fuel-plan maintenance placeholders. Final artifact, follow-up
release identities and completed live fuel checks will be recorded below.

## Follow-up server rollout and client verification

Cloud Build `8b38ff44-2c01-40fe-959e-31b60f4a2bfc` completed its full release gate
and deployed API revision `amftms-api-00082-7dc` at 100% traffic, image digest
`sha256:0d8657a5915d5f4703a6a716a96c39e40670ed3aeb54ba7b07db6b919d644496`.
The liveness endpoint returned 200. The matching local full gate passed 798
Server, 440 Client and 191 JavaScript tests, a strict build with no warnings,
222 published assets/six import graphs and 44 offline UI cases.

The intermediate `artifacts/release.IFdimE/publish/wwwroot` client additionally
passed four staged hours/refresh cases (32 screenshots), 34 GPU/current-popup
cases and two startup viewport cases. Current warnings align with the ETA label;
on-time reset alternatives are green; `With recap` and `If reset` use consistent
sentence case. Dispatch truck headers omit duplicate stop/appointment/ETA/cycle
details while retaining live driver hours and route distances. Initial map fitting
is hidden until its correct fleet or deep-linked-truck camera is ready; later
polls do not override manual camera movement. Fuel maintenance placeholders are
omitted without retaining unsafe recommendations.

A normal 54777 fuel calculation saved successfully at
`2026-09-09T05:33:13.824781Z`: two purchases, 1,451.001 miles and 79,393 checked
seconds. This confirms the prior one-second snapshot persistence failure is
resolved. Subsequent projection correctly withheld the recommendation because
the truck's fuel reading was older than the existing freshness limit. The saved
checked plan is retained; no fresh reading or manual gallons were fabricated.

The normal 11005 calculation also completed and its live map displayed LOVES #738,
87 gallons to buy, approximately 239 miles ahead and 57 gallons on arrival. The
unconfirmed future pickup no longer blocked this calculation. Each truck was
calculated once after the follow-up rollout; no repeated provider-heavy recovery
loop was introduced. A scoped check of this revision's application error logs
returned no errors during these two operations.

IFdimE is not the final hosted client: the owner's later requests shorten next
recap to its home-local date and credited hours, remove the fuel-reading age
suffix, and compact the map truck header and filter toolbars. Their final artifact
and verification are recorded after completion. No isolated PostgreSQL fixture
was available, so PostgreSQL-specific fixture checks were not run.

## Final map and Dispatch presentation changes

- Next recap displays only the existing home-local calendar date and credited
  hours. The exact instant/offset and calculations remain unchanged. Positive and
  negative date-boundary regressions cover both presentation implementations.
- Fuel percentages omit the reading-age suffix; freshness checks on actionable
  recommendations remain enforced independently.
- Fleet and Dispatch toolbars use the shared token-based control layout, restrained
  selection states and native accessible checkboxes. Mobile disclosure and view
  behavior remain unchanged. The browser matrix caught and corrected an expanded
  date field overflow at 390px with 200% text in both themes.
- The map truck header lets duty/rest details share horizontal space with HOS
  clocks when available; smaller widths wrap without removing information or
  reducing text sizes. Truck glyphs are 28px instead of 30px; PU/DEL circles have
  13px instead of 14px radii. Number sizes, borders, colors and hover proportions
  remain unchanged.
- Recommended fuel stations no longer create floating distance labels. Their
  filled points and rings render above ordinary stations and roads, while PU/DEL
  stops and trucks keep their existing priority. A station is rendered in only
  one point layer. Touch picking covers both ordinary and recommended points.
  Distance-only progress updates change the open popup without redrawing markers.
- Station popups and the current route summary reuse the existing structured
  street/locality splitters. Street appears first, locality second; copy actions
  retain the full original address. The unused older address-heading formatter
  was removed after replacing its only caller.

The final full local gate for `artifacts/release.biYRMS/publish/wwwroot` passed
798 Server, 449 Client and 198 JavaScript tests, a warnings-as-errors solution
build, 222 published assets/six import graphs and 44 staged offline UI cases.
The intermediate ECcDnV artifact also passed, but was superseded to include the
foreground fuel layer in mobile touch picking. It was not published.

Final acceptance used biYRMS for eight staged hours/refresh cases at 2344, 1440,
1200 and 390px in both themes (72 screenshots), 34 GPU/popup cases and two startup
cases, all without failures, browser errors or unexpected requests. Eight generated
JavaScript assets and the compiled stylesheet used by GPU/startup fixtures matched
the staged artifact byte-for-byte. The selected-truck header measured 86.84px high
at 1440/2344px, down from the comparable 151px baseline; 1200px wrapped safely.
This is fixture layout evidence, not a production speed or provider-performance
measurement. A long future desktop popup with every alternative retains its
existing 320px scroll cap; the bottom details link remains reachable by scrolling.

Firebase published this exact verified biYRMS artifact. All 74 uncompressed public
files matched staged SHA-256 bytes on both `amftms.web.app` and
`tms.amfcarrier.com`; all 222 compressed/uncompressed files had already passed the
artifact gate. A live reload selected the deep-linked 54777 at the expected map
camera and showed its street-first two-line current address, without a stale fuel
maintenance placeholder. No additional server rebuild or migration was required
for the final client-only presentation changes.

Live Dispatch verification also confirmed `Fuel 46%` without the age suffix,
no duplicate stop/ETA/cycle block in the truck header, AMF1373 recap shown as
`Sep 11 +3h 05m`, and AMF1380 alternatives using `With recap` / `If reset`.
The browser was returned to the working deep-linked fleet map.

## Owner-requested presentation removal without checks

After the verified release, the owner explicitly requested removal of every
`If reset` alternative from the UI and the map's separate `Fuel plan` summary,
with no checks before publication. Shared stop cards and GPU popup formatting
now display only recap alternatives. Server calculations, fuel recommendations,
station markers and station detail popups are unchanged. The map no longer mounts
the standalone fuel summary component.

This follow-up uses Client compilation/publication and Firebase Hosting upload
only, as expressly requested. Automated tests, the release gate, browser checks
and post-deployment asset verification are not run; the earlier successful counts
do not validate these later presentation changes. No API or database deployment
is needed.

Final browser evidence: `Client/test-results/final-map-polish/{hours,stop-cards,startup}`
and `Client/test-results/ui-smoke` (ignored, fixture data only).

## Last-reported fuel retention

The owner rejected inferred idle/travel consumption and changes to saved purchase
quantities. Those draft changes were removed before release. The remaining server
change removes the 15-minute fuel-reading age cutoff from saved-plan projection.
The latest valid reported level is used as-is, matching explicit calculation;
missing/invalid/future observations, manual precedence, GPS, route, assignment,
pricing, capacity and reserve guards remain. No new provider calls, optimization,
schema changes or migrations were added.

Regression coverage compares fresh and 16-minute, six-hour and 48-hour-old readings:
station identities, quantities and complete projections remain equal, and the saved
snapshot is unchanged. Immediate calculation responses and ordinary saved reads
agree; missing fuel and stale GPS still invalidate recommendations. Tests for the
previously requested reset/summary removals now assert absence of those UI elements
while verifying that fuel purchases are still sent to the map renderer.

`bash test.sh fuel fleet` passed: 622 Server tests, 308 Client C# tests, 157 map
JavaScript tests and 18 JavaScript architecture checks. PostgreSQL execution checks
were not run (no isolated fixture); production performance was not measured.

Cloud Build `cb024bef-8131-4a23-930e-4be56ee14f3e` passed the full release gate
(802 Server / 449 Client C# tests and the complete Node/artifact phases).
API revision `amftms-api-00083-nrn` serves 100% traffic from digest
`sha256:152ffd690fe4b4eadd7415436dbf83741d343f9fcecee24865df695362562541`.

## Smaller station and stop markers

Shared GPU metrics now use 11px stop radii (was 13), 7px station radii (was 9),
and 12px recommendation rings (was 16). Stop numerals remain 14px; foreground
ordering and touch picking are unchanged. Map dependency checks passed. The full
local release gate passed 802 Server / 449 Client C# / 198 Node tests and verified
222 assets and six dependency graphs. Firebase published the exact
`artifacts/release.4zHyeq/publish/wwwroot` artifact.

The offline GPU probe passed four GPU, eight popup, twenty hours and two
constrained-layout cases, with no browser errors or unexpected requests. Its first
run exposed a fixture camera/resize race; the probe now waits for the focused GPU
frame before clicking. Production interaction code was not changed. Results:
`Client/test-results/compact-markers-2026-09-09-rerun/report.json` (ignored).
This does not constitute live Google Maps acceptance or production performance
measurement. API liveness returned Healthy after rollout.

The owner's follow-up also reduced stop numerals from 14px to 12px; other map
text is unchanged. The 34-case GPU/popup probe passed again, including both device
densities and constrained layouts. Firebase published
`artifacts/release.rvrGVW/publish/wwwroot` after the complete release gate
(812 Server / 449 Client C# / 198 Node tests, 222 assets, six graphs).

## Canadian province import regression

Read-only inspection found 91 Canadian stations with empty regions, representing
364 active price rows (the initial user-facing count incorrectly called these
364 stations). Both the latest and preceding CAD source attachments use `PROV`;
the parser only read `STATE`, and station synchronization assigned that empty
value to Region. All 91 source IDs match exactly one Canadian station with an
empty region. Existing CAD/L diesel rates cover all eight source provinces under
the existing previous-quarter fallback policy; no rate/formula change is needed.

The owner authorized restoring provinces from the source file. The parser now
accepts STATE/PROV/PROVINCE with normalized headers and rejects missing/conflicting
regions. Application preparation and writes separately reject blank regions before
lookups or station mutation. Nine parser regressions and one integration guard
passed with the fuel dependency group (444 Server / 133 Client C# / 18 JS
architecture checks). The later full Client release gate above included these tests.

The scoped recovery script is retained under ignored artifacts as
`artifacts/canadian-fuel-provinces-recovery-2026-09-09.sql`. It shares the import
transaction lock, validates all 91 identities/countries/previous regions, changes
only blank Region fields, and verifies the complete mapping before commit.
It does not change coordinates, prices, purchases or import-source records.

Cloud Build `849bb3e6-cc09-479b-8f38-6fbdc68f337e` was submitted for the province
guard. Its .NET phase passed 812 Server / 449 Client C# tests, but local gcloud
authentication expired while awaiting the build. The deployment script exited
before Cloud Run deployment; resume by reading this build's final status and image
digest, not by submitting another build. Province recovery has NOT been applied.
The smaller 12px numbers are already published; the live API remains revision 83
with last-reported fuel retention. No migration was added or required.

Separate read-only station inspection found wrong saved locations for BVD Blue
River (US address) and BVD Bradford (UK address). This province-only correction
does not certify or repair station coordinates; that issue was reported to the owner.

Reauthentication completed successfully. The existing build finished SUCCESS;
no duplicate build was submitted. The guarded transaction restored exactly 91
Province/Region fields and committed. Subsequent read-only verification found
91 Canadian stations / 364 active CAD price rows, zero missing provinces and zero
missing matching CAD/L diesel rates. No price, address or coordinate updates were
made. The new instance starts after this recovery, avoiding the old price cache.

API revision `amftms-api-00084-pk9` now serves 100% traffic from
`sha256:2cc64fc95a205c22772c1d8d45ddd4d87e324b0a859b838e2632c4a5633888c8`.
Liveness returned Healthy. The full Cloud Build gate passed all steps before this
rollout. No schema migration was required. PostgreSQL checks were limited to scoped
operational recovery/verification, not an isolated PostgreSQL test suite.

## Local fuel search and ranking follow-up

Truck 54777's saved version 12 calculation at 12:11 UTC checked a LOVES #706
chain at $1,797.06 versus the selected #366 chain at $1,850.15. Both passed the
road-detour/reserve checks. Ranking assigned absolute priority to added lateness
minutes before cost and mislabeled schedule rejections as higher cost.

Version 13 protects newly missed appointments and newly introduced cycle
shortages, but prices incremental delay for already-late plans instead of giving
each minute unlimited economic priority. Extra schedule rest/wait is charged at
the configured hourly rate without counting road time twice. Rejections due to
schedule rank now have a distinct internal reason. Versions 11 and 12 remain
projectable under the unchanged price/profile/assignment/fuel/road guards; explicit
recalculation cannot reuse their old selection indefinitely.

Bounded corridor search prioritization and local expansion of two near-road seeds
remain in place; the 12 complete-road-check limit is unchanged. Regression cases
cover skipping an expensive station when cheaper fuel is reachable, a 20-gallon
bridge purchase when it is not, newly missed appointments, new cycle shortages,
cost comparison at already-late stops, and saved-plan compatibility.

Local `bash test.sh fuel` passed 458 Server, 133 Client C#, and 18 JavaScript
architecture checks. These are affected/dependent categories, not the full release
gate. The API was restarted on localhost with migrations and synchronization
disabled; no production deployment or schema migration was performed. One manual
54777 fuel calculation was initiated through the local UI for operational
verification. Isolated PostgreSQL tests and production performance measurements
were not run.

The local UI calculation completed and saved version 13 at 12:18:52 UTC. Its
first purchase is now LOVES #706, 369.3 miles ahead at $5.613/gal, with 44 gallons
on arrival and a 66-gallon purchase. It skips #366 entirely. A later return visit
to #706 is a separate itinerary occurrence. In this same calculation the selected
comparison score was $1,794.17 versus $1,847.31 for #366; all 12 road checks stayed
within the configured count. This is an operational result against the configured
shared data, not a production deployment, global-optimum proof, or a guarantee of
actual fuel consumption. The server-only change remains local.

## Numbered visits and all assigned loads

At the owner's request, version 14 removes the two-future-load, 72-hour and
1,800-mile horizon cutoffs. All assigned loads are joined in dispatch order with
connecting deadheads; the 40 mandatory-stop routing envelope remains a hard
failure boundary instead of silently returning partial coverage. The complete
road-check budget remains 12. No production performance improvement is claimed.

FuelPlanStop.Number is additive in the existing JSON snapshot and Client contract;
no schema migration is required. New calculations number all purchases, and read
projection preserves numbering after prior purchases pass. Older snapshots are
numbered before trimming, without a database write. Repeated physical stations
render once with combined visit numbers and retain each visit's purchase and
distance in the popup. Numbers use the existing 12px map typography; distances
remain inside the card. Unchanged popup polling does not rebuild visit rows.

`bash test.sh all` passed 827 Server, 449 Client C#, and 201 Node tests. Strict JS
checking passed. The local Client rebuilt with warnings treated as errors. The
offline production GPU/popup probe passed 4 GPU, 8 popup, 20 hours, and 2 constrained
cases without browser errors or unexpected requests. Evidence is in the ignored
`Client/test-results/numbered-fuel-visits-2026-09-09/report.json`; the numbered fuel
popup screenshot was visually inspected. This does not certify Google Maps layout
on every device. Isolated PostgreSQL checks were not run.

Local UI recalculation of 54777 saved version 14 at 12:30:32 UTC, covering its
currently assigned AMF1375 and AMF1373. The two #706 visits were stored as numbers
1 and 2, with a 65-gallon outbound purchase and a later fill to the configured limit.
No #366 purchase was selected. API and Client were restarted on localhost; the
production binaries were not deployed. The operational calculation writes the
configured shared truck snapshot, not an isolated test database.
