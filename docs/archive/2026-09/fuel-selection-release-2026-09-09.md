# Fuel selection and numbered gauges follow-up — 2026-09-09

## Scope and initial evidence

The owner requested the circular arrival/departure fuel indicators, clear numbered
station visits, correction of the expensive-station search, and deployment.
This follows the unresolved findings in the [earlier audit](fuel-ui-audit-2026-09-09.md).

Read-only checks found that both public domains still served the CSS and map
scripts from `release.rvrGVW`, before the fuel gauges and number layer. Current
compiled UI assets matched the previously verified `release.3huzn7`. The missing
production UI was an unpublished frontend, not removal of the gauge implementation.
Firebase project access succeeded on this follow-up; no authentication settings
or permissions were changed.

The owner subsequently clarified that LOVES #305 should also be considered on
the outbound journey, not only the return visit, and explicitly chose to retain
the configured $20 stop cost. No reserve, stop cost, fill target or detour setting
is changed to force a preferred station.

## Persistence correction

The durable truck fuel store now aligns dispatch-count validation with its existing
40-mandatory-stop envelope instead of rejecting more than three loads. Ordered
ownership, geometry, payload size and newer-only replacement checks remain intact.

New isolated SQLite regressions reproduced the failure before the correction, then
passed for four and forty assigned dispatches, exact geometry/readback, and rejection
of forty-one stops without replacing the saved plan. The real automatic fuel
recalculation/commit scenario now covers both three and four loads. Targeted store,
automatic-planning and architecture checks passed 113 tests.

## Search correction

Version 16 fills spare candidate slots with price/access alternatives that are
nondominated within their route section and visit leg, preserving the existing
backbone and corridor candidates. Repeated variants anchored by the same physical
station more than five estimated access miles away are deferred behind different
anchors and near-road alternatives. The 24-candidate and 12-route-check defaults
remain unchanged; deferred chains are not deleted. Saved versions 11–15 remain
displayable when otherwise valid, but explicit recalculation searches again.

The historical 54777 replay now retains the previous pair and includes #333 at
the fourth check. Only one of the first twelve alternatives uses #397, instead
of eight. These are candidate-search observations, not verified road savings.
Five new pure regressions cover the spare-slot omission, leg-local dominance,
anchor diversity, shared near-road bridges and a reachable cheaper outbound visit
with a separate return visit. The affected Fuel/Routing/Architecture server run
passed 502 tests. Actual road checks, reserve, capacity, minimum purchase and
terminal fuel policy remain authoritative.

The existing expensive-bridge regression explicitly purchases 20 gallons before
the cheaper station, then 70 gallons there, while preserving reserve and charging
$20 for each stop. Production quantity search starts at the existing 10-gallon
minimum; it does not force full fills or a fixed bridge purchase.

## Verification and release status

The complete local release gate passed 871 Server tests, 453 Client C# tests,
211 Node checks, strict builds, 222 published-asset hashes, six JavaScript entry
graphs and 44 offline staged UI page checks. Verified frontend artifact:
`artifacts/release.tIel2w/publish/wwwroot`.

The GPU smoke exposed a test-only race: a frame could finish before the queued
single-visit number update. The fixture now observes exact fuel-layer numbers in
Deck's completed render, retaining the original assertions. Two consecutive
40-case matrices passed with 52 screenshots each and no errors or unexpected
requests (`Client/test-results/fuel-release-sync-{1,2}-2026-09-09`). The previously
failing mobile dark card was visually inspected. No production code changed for
this synchronization fix.

Firebase Hosting successfully published the verified artifact. Both public domains
return matching SHA-256 hashes for index, CSS, fleet map and GPU scene scripts.
Cloud Build `800b1253-7a1c-4204-b106-fd440a1656da` repeated the automated gate
successfully and deployed API revision `amftms-api-00086-h2b`, serving 100% of
traffic. Its ready/healthy conditions passed and `/api/health/live` returned
`Healthy`. Verified image digest:
`sha256:760066bb60a27e49dca023df07770c70c927f497f56b052382fa08701d4621d8`.
One scoped live 54777 recalculation completed and durable readback confirmed
selection version 16 at `2026-09-09T16:48:09.248039Z`, without checked-route reuse.
It used 29.587 gallons of starting fuel and covered both assigned dispatches over
1,239.893 remaining miles. It selected:

| Visit | Station | Purchase | Arrival / departure | Assigned load |
| --- | --- | --- | --- | --- |
| 1 | LOVES #682 | 102 US gal | 28 / 130 US gal | AMF1375 |
| 2 | LOVES #706 | 165 US gal | 25 / 190 US gal | AMF1373 |

The selected checked score was $2,003.851, including the terminal-fuel comparison;
actual planned purchases were $1,507.851 plus $40 stop costs. Twelve route checks
were retained. The now-checked #682 / #333 / #305 alternative scored $2,023.842
(17.292 extra miles, 21.588 extra minutes), about $19.99 more. The #682 / #305
pair scored $2,007.847. The first expensive station therefore remains selected;
this release does not claim that a small bridge or a globally cheapest plan won.

The production map completed the request, restored the Calculate Fuel button and
displayed the recommended-station ring. No captured browser warnings/errors were
reported during this check. Exact live popup/number-layer visual confirmation
was not completed; those assertions are covered by the staged GPU matrices above.

No schema migration is required. No isolated PostgreSQL fixture was available;
PostgreSQL execution tests are not run. Production routing latency and global
optimality are not established by offline tests. Saved operational route data is
used only in an isolated pure replay, never as a database test fixture.

## Full-tank and low-fuel follow-up — locally verified

The owner explicitly approved a 100% fill target for every truck. The existing
production Settings form saved this value; read-only durable verification found
revision 3 at `2026-09-09T18:49:06.984327Z`, with fill 100%, reserve 25 US gal,
stop cost $20, maximum extra driving 30 minutes and IFTA disabled. No per-truck
override, fuel-reading substitution or default-code change was introduced.

The owner also reported a 1% reading rejected before station search, requested a
compact truck toolbar, and requested one always-visible Next recap in the Dispatch
truck header instead of repetitions in load cards. The implementations below are
locally verified but not deployed. The earlier deployed version 16 result above is
historical, not proof of the new behavior.

The first Fleet Map toolbar adjustment passed 87 Server, 178 Client C# and 205
Node checks, a strict isolated Client publish with 222 checked assets, and eight
staged Blazor browser scenarios. At 2344px and 1440px the actions sit 12px after
the rendered duty-status text; the close button remains at the far edge. Mobile
controls retain at least 44px touch height without overflow. Evidence:
`Client/test-results/header-actions-2026-09-09/report.json`. These results precede
the additional Dispatch Next recap change.

### Low-fuel recovery

Selection version 17 accepts a positive measured starting quantity below reserve.
Only the first purchase may be reached using that reserve, with a nonnegative
arrival balance. That purchase must restore the normal reserve; later legs and
the final arrival retain their ordinary minimums. Zero, invalid and unreachable
readings do not become a driveable plan. No fuel is invented. Small bridge buys,
full-tank alternatives, the $20 stop cost and the existing search/check budgets
remain intact. Projection and checked-route reuse apply the same first-stop
policy, including the existing nearby-stop tolerance without negative mileage
credit. Ordinary reads can retain otherwise valid versions 11–16; explicit
recalculation uses the new selection version.

The affected Fuel/Routing/Architecture server run passed 527 tests, including
new pure recovery cases and an isolated service/store/projection/reuse scenario
at 1% fuel. An independent review found no blocker. These fixtures do not establish
that the moving truck 54777 can reach a particular live station. After the local
API restart its live fuel reading was unavailable, so no successful live recovery
calculation is claimed.

### Compact Dispatch header and current-driver recap

Route metrics, driver duty/Next recap and HOS gauges are adjacent in the truck
header. Next recap is one date-and-hours row there instead of a repeated row in
each Dispatch stop card. PU/DEL cycle balances and With recap alternatives remain;
the Fleet Map stop cards retain their recap row. Unknown recap renders a neutral
dash. Telemetry no longer changes the displayed driver name independently of the
board's driver/HOS/recap snapshot.

The additive board CurrentCycle contract uses the authoritative current-driver
CycleAtCalculation baseline, not a future delivery forecast. Existing saved reads
and chain identity validation are reused; there are no new provider/history calls,
DI registrations or migrations. Matching, verified future recap can remain visible
for the existing 15-minute display-only grace without changing its original
timestamps or home-time-zone offset. Changed driver/truck/root/input identity,
invalid dates and past/unknown recap are rejected.

The server contract passed 257 targeted tests, including 18 isolated SQLite
regressions. The combined affected UI checks subsequently passed 390 Server,
443 Client C# and 207 Node tests, with 222 strictly published assets. Forty-four
responsive cases covered both themes and 200% text. The subsequent hours-refresh
matrix also reproduced an existing real ETA blanking defect: an expired complete
response cleared refresh state before a replacement was available. Both card
containers stayed mounted while their forecast children disappeared. This was not
a GPU-frame test race; the no-empty-render assertions were retained. Diagnostic
evidence is in `Client/test-results/dispatch-header-probe-2026-09-09/report.json`.
The fix classifies a valid, expired complete ETA as pending only when a successful
planning HTTP response arrives. It uses immutable copies without changing the
original timestamps. Passive cache reads and component rerenders do not manufacture
refresh state. Fleet display memory also blocks fallback to a later pending
snapshot after the original retained deadline. No provider request is added.

The exact observed cache-to-display timeline now has a deterministic regression.
Additional cases cover newer-but-expired replies, original grace bounds, passive
rerenders, invalid windows, completion and unavailable data. A future-stop test now
distinguishes the newly pending chain's own pickup estimate from an expired direct
details response: it excludes the prior delivery and foreign-dispatch values,
requires expiry at the original deadline, and prohibits another details request.
The affected rerun passed 603 Server, 451 Client, 170 map JavaScript and 18
JavaScript architecture checks. Independent review found no remaining blocker.

### Final combined verification and local handoff

The complete `verify-release.sh` gate passed 914 Server tests, 476 Client C# tests,
213 Node tests, strict solution build/Client publish, 222 published-asset hashes,
six JavaScript entry-point graphs and 44 staged responsive UI page checks.
Verified artifact: `artifacts/release.0F7ig2/publish/wwwroot`.

The hours/header browser matrix passed twice against that exact artifact: eight
cases per run, 32 replacement probes and 76 observed mutation samples, with no
empty forecast-card samples, browser errors or unexpected network requests. The
128 screenshots cover wide/mobile and both themes; selected final header images
were visually inspected without horizontal clipping. Reports:

- `Client/test-results/dispatch-header-final-hours-2026-09-09/report.json`
- `Client/test-results/dispatch-header-final-hours-repeat-2026-09-09/report.json`

Local API and Client were rebuilt and restarted on ports 5086 and 5067. Migrations,
fleet synchronization and Gmail background maintenance remained disabled for the
local API; liveness returned Healthy. Authenticated local Dispatch inspection
confirmed approximately 99px route headers and exactly one header Next recap for
each of the three displayed trucks, with none in their load-card sections:
11005 Sep 15 +2h 12m, 11006 Sep 12 +5h 50m, and 54777 Sep 11 +3h 05m.
The resulting live desktop layout was visually inspected.

The new code has not been deployed; only the explicitly approved 100% fleet
setting was saved in production. No migration is introduced. PostgreSQL execution
checks and the authenticated map lifecycle soak were not run. The current local
54777 reading remained unavailable, so a live low-fuel recalculation was not
attempted or claimed. Provider latency, production performance and global fuel
optimality remain unmeasured.

## Presentation-only follow-up — service balance and address emphasis

The owner requested removing `After service` altogether, not renaming it. The
shared Razor stop-hours component and map label builder no longer emit that row,
regardless of whether its balance differs from arrival. Arrival Cycle remaining,
shortage warnings, Next recap and conditional With recap remain. Server calculations
and forecast fields are unchanged. Regression assertions now require absence of
the removed row while preserving arrival values, status colors, identity and quiet
refresh behavior.

The first affected run passed 390 Server, 454 Client C#, 170 map JavaScript and
18 JavaScript architecture checks; the strict Client build and generated JavaScript
build passed. This is a category run, not a new full release gate.

The owner then requested a one-line dynamically sized current-route address but
withdrew that request before implementation. The final request keeps two lines and
the existing column layout: street/building in normal weight, city/region/postal
code/country in bold. The complete original copy value and other popup address
layouts remain unchanged. The final combined verification below covers both
presentation changes. Local Fleet Map inspection confirmed street weight 400,
locality weight 600, no `After service` text and no horizontal page overflow.

## Economic detours — local version 18

The owner reported dispatch `5269044d-c35c-4977-8023-92eedcbcf62d` failing after
all checked purchase chains exceeded the old aggregate 30-minute detour limit.
The reported three-load horizon was 3,116 miles, including a 13.1-mile/47.9-minute
variant. This rejection happened before fuel quantity and schedule evaluation;
the listed alternatives were not demonstrated to be fuel-infeasible.

The owner then explicitly replaced fixed detour caps with economic comparison.
Version 18 no longer rejects checked routes solely for exceeding the saved minute
or mileage limit. Actual checked mileage determines fuel consumption and purchase
quantities. Ranking includes all nonnegative extra road time at the configured
hourly cost, the existing purchase-stop cost, and additional schedule delay once.
The previous under-five-mile time exemption is removed. Collapsed checked-route
candidates clear estimated access minutes on copies, avoiding a duplicate time
charge or mutation of the shortlist. Settings exposes the existing hourly-cost
preference instead of the obsolete visible detour limit. No production preference
was changed; the owner's $20 stop-cost policy is preserved.

Reserve, capacity, terminal requirements, complete ordered assigned-load coverage,
and existing appointment/cycle ranking remain unchanged. The 24-candidate,
12-complete-route and concurrent-search bounds are unchanged; wider economic
eligibility does not imply an unlimited geographic search. Safe checked versions
11 through 17 remain displayable; explicit recalculation no longer locks onto an
older-version purchase chain.

Synthetic integration regressions accept the reported 13.1-mile/47.9-minute
shape and a 55-mile/85-minute checked detour, with exact purchase + $20 + one time
charge. Competing-route cases prove a tiny discount loses to an extra hour and
fuel consumption while a large discount wins. Isolated unit cases cover short
distance/long time, nonnegative costs, unchanged stop penalties and cloned metadata.
The initial four new integration failures were fixture assertions against the
read-only projected display (which intentionally omits historical route checks),
plus a short journey that did not need fuel. Final cases inspect the durable checked
plan and include a journey requiring a purchase. Production behavior was not
weakened to satisfy those assertions.

The affected `bash test.sh fuel dispatch map` run passed 661 Server, 460 Client C#,
170 map JavaScript and 18 JavaScript architecture checks. The subsequent complete
`verify-release.sh` gate passed 931 Server, 485 Client C# and 213 Node tests, strict
solution build/publish, 222 asset checks and six JavaScript dependency graphs.
Exact verified artifact: `artifacts/release.53n79L/publish/wwwroot`.
The strict Debug Client build also passed with zero warnings/errors.

Offline visual verification passed 44 UI page cases and eight hours scenarios,
including Settings, two-line address emphasis, removal of `After service`, and
quiet forecast replacement: 16 probes, 42 mutation samples, no empty forecast
samples, browser errors or unexpected requests. Selected desktop/mobile and both
theme screenshots were independently reviewed. Reports:

- `Client/test-results/fuel-economics-ui-2026-09-09/report.json`
- `Client/test-results/fuel-economics-hours-2026-09-09/report.json`

Local API was restarted from the verified Release build on 5086 with migrations,
synchronization and Gmail background maintenance disabled; liveness is Healthy.
Client was rebuilt/restarted on 5067 and the local map reloaded. No deployment or
new live fuel search was performed. Actual truck 11005 recalculation success is
therefore not claimed. No migration was added. PostgreSQL execution checks and
the authenticated map lifecycle soak were not run.

Independent source review found no blocker in the scoped change. Baseline routes
can reuse saved current/base/deadhead timings while alternatives use independently
cached traffic-aware provider results. Different calculation times remain a known
comparison limitation, not an established cause of the reported failure. No
provider-cache policy or call budget was expanded. Production latency and global
economic optimality remain unmeasured.

## Version 18 production deployment

The owner subsequently authorized deploying the current local changes. The normal
Cloud Build/Cloud Run and Firebase Hosting workflow was retained; no environment
override, fuel setting change, manual fuel search or database data repair was part
of this deployment. The upload exclusion check found no known credential/local
configuration paths in the 1,066-file source archive.

Cloud Build `063467e1-1998-4fa4-9c72-8d150fe2741d` completed successfully, including
the full release gate: 931 Server tests, 485 Client C# tests, Node checks, strict
builds and artifact validation. The API was deployed by immutable image digest
`sha256:47325188d455386c38fc6775e65adcf35b1ba1b006d8ea57c15a1db95cd0de7a` as
revision `amftms-api-00087-fg7`. Cloud Run reports it Ready and serving 100% of
traffic. The previous revision was `amftms-api-00086-h2b`, using image digest
`sha256:760066bb60a27e49dca023df07770c70c927f497f56b052382fa08701d4621d8`.

The local release gate was also rerun for the exact frontend artifact
`artifacts/release.CfceXt/publish/wwwroot`: 931 Server, 485 Client C#, 213 Node,
222 published-asset checks, six JavaScript dependency graphs, 44 offline UI cases
and eight hours/refresh scenarios passed. Browser scenarios reported no failures,
browser errors or unexpected requests. Reports:

- `Client/test-results/fuel-economics-deploy-ui-2026-09-09/report.json`
- `Client/test-results/fuel-economics-deploy-hours-2026-09-09/report.json`

Firebase successfully published that exact verified 222-file directory. After
publication, both `https://amftms.web.app` and `https://tms.amfcarrier.com` returned
HTTP 200 and exact staged SHA-256 matches for `index.html`, `css/main.css`, the
fingerprinted Client WASM and the Fleet Map JavaScript entry point. Both proxied
`/api/health/live` endpoints and the direct Cloud Run endpoint returned Healthy.
The new revision's initial error-severity log query returned no entries; this is
a point-in-time rollout check, not ongoing monitoring or a performance claim.

No new migration was introduced. PostgreSQL fixture execution, an authenticated
map lifecycle soak and live truck 11005 fuel recalculation were not performed.
The deployed optimizer will be used on the next explicit fuel recalculation;
deployment itself does not replace existing purchase recommendations.
