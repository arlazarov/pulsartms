# Execution and fuel recovery — September 16, 2026

## Incident and scope

Truck 11005 reported an address-confirmation failure while its active load was
AMF1383. Read-only production diagnostics reproduced the failure for the two
imported Drop/Hook records at 145 Major Grahams Road. The current delivery
address resolved successfully and already had valid verification provenance.

The incoming native execution already contained a confirmed Hook and the
delivery. Two query-time merges added the imported transfer records again by
truck identity, mixed source and native sequence numbers, and changed the route
without advancing the assignment revision. Those merges have been removed.
Native reads now use the saved execution; source changes use the existing
transactional reconciliation. No city-center fallback, address override or
manual production stop edit was used.

Unambiguous future source stops can be reconciled into their existing leg. This
updates the leg revision, endpoints and planning outbox together. Mapped
transfers, completed history and conflicting resource assignments cannot be
reimported as new stops. Ambiguous topology retains the saved execution for
review.

## Simplification and safeguards

- Both route and board reads use native snapshots without source-stop hydration.
- Reconciliation indexes links and parses snapshots/fingerprints once per source
  within the request; it does not add a long-lived cache.
- Actual-event reconciliation no longer sorts temporary timestamp arrays or
  makes a second full stop copy. A local 49-stop allocation check measured
  39,288 bytes. This is not a production memory or latency measurement.
- Allocation regressions enforce at most 53,900 bytes for 49-stop
  reconciliation, less than 4 KiB for joining 50,002 route points without
  copying their buffers, less than 180 KB for the dense-route search index and
  less than 4 KiB for forty geometry matches. These are focused allocation
  bounds, not process-RSS or end-to-end production latency claims.
- Fuel requests again require the expected execution identity/revision. A stale
  explicit fuel revision cannot be silently replaced by a newer database
  revision.
- Fuel horizon calculation no longer tries to repair routes itself. Existing
  route preparation owns rebuilds; fuel consumes checked saved geometry.
- Removed the unconditional US-only price filter. Station access must stay in
  the matched road point's country; legitimate cross-border route sections
  remain eligible. Post-delivery access cannot invent a border crossing for
  cheaper fuel.
- Automatic fuel policy version 29 requests recalculation of older automatic
  snapshots even if prices are unchanged. Manual plans remain protected.
- The same refresh checks the authoritative assignment signatures, so adding,
  removing or changing future work no longer waits for a price change. It reuses
  the existing board read without HOS, financial or ETA hydration.
- Fuel refresh has its own retry checkpoint. A rejected calculation cannot be
  reported as a successful fuel refresh or block upcoming route preparation.
- Future assigned routes enter the existing bounded preparation queue, including
  work outside the default date horizon. Repeated demand preserves retry
  deadlines.
- A native leg ending at ordinary delivery can continue into uniquely assigned
  legacy loads. Each itinerary stop keeps its own leg/revision. Route
  production, fuel, next-load display and ETA share native predecessor
  interpretation.
- Deployment now verifies the exact build digest and Ready revision, explicitly
  assigns 100% traffic and verifies observed traffic before reporting success.

## Verification

Full local follow-up build: zero warnings/errors; Node 561/561, Client
1011/1011, Server 2035/2035. Published manifest verification checked 273 assets
and seven JavaScript entry-point dependency graphs.

The production-shaped execution regression builds confirmed Hook → verified
delivery with a geocoder that throws if the duplicated imported address is used.
Tests also cover durable additions/idempotency, eight missing/stale fuel
identity cases, historical/resource boundaries, country-aware fuel access,
optimistic fuel replacement, deployment mismatch/traffic failures and allocation
bounds.

Read-only pre-release checks used normal application services in a PostgreSQL
read-only transaction with outbound HTTP denied. They observed:

- 11005: current AMF1383; upcoming AMF1388 and AMF1384. Its saved route was
  rejected against the corrected execution, and fuel still used an older
  snapshot.
- 54777: current AMF1387; valid saved route, two arrival estimates and a saved
  fuel recommendation with 77.41 miles of estimated detour.
- 11007: current AMF1390, then AMF1389; valid route and four saved arrival
  estimates. A September 17 arrival used an explicitly estimated September 15
  quote, not an invented future price.

The 54777 border diagnostic confirmed that the selected LOVES #870 station was
in the US while its exact matched mandatory-road occurrence was in Canada. Its
25.803224-mile geographic separation produced the saved 77.409673-mile
round-trip allowance. The new country guard rejects that occurrence; it was not
an assigned border crossing. The check used saved geometry and local geographic
lookup with no outbound provider request.

These are operational reads, not database tests. No isolated PostgreSQL test
fixture was available; PostgreSQL integration checks were not run. No schema
migration was introduced. The separate automation browser initially opened at
Login. Later, the existing user browser tab was accessible: a manual production
check showed 11005's current AMF1383 route and 81% / 202-US-gallon delivery fuel
estimate. This does not replace the full authenticated browser scenario suite.

The initial offline responsive UI smoke passed: six pages at 1440px and 390px in
both themes and both 100%/200% root-font modes, plus wide cases at 2344px.
Evidence is pinned in `artifacts/managed/browser-ui-HUGTLV`; the verified
release artifact is pinned in `artifacts/managed/release-Z7OpLd`. These fixture
checks are not an authenticated production browser test.

The first release used Cloud Build `b431ed2c-ef97-45c7-9e1f-58ceba827918`,
verified revision `amftms-api-b-b431ed2c-ef97-45c7-9e1f-58ceba827918` and digest
`sha256:a7a450e340b6b0372a6ac415b20bd734ea4d0c7b0e3eae74d7864969444f8456`. The
revision received 100% traffic; the public liveness endpoint returned Healthy.
Read-only follow-up showed 11005's accepted route version 4 and automatic fuel
calculation at 03:56:53 UTC; no address correction or fallback was required.
11007 recalculated at 03:58 UTC with four arrival estimates. Truck 54777
retained its earlier automatic snapshot, requiring further investigation.

The follow-up automated gate corrected nine fixtures that omitted the truck or
current assignment now required by the production guard. No identity check was
weakened. Exact native-to-legacy terminal scope, independent fuel retries,
bounded future preparation and native predecessor geometry have regression
coverage. Follow-up release and responsive evidence are retained in
`artifacts/managed/release-ks0zxL` and `artifacts/managed/browser-ui-QO2eMe`.

The second release used Cloud Build `27dd7a5f-4070-4d88-b8bc-2cec3032abc2`,
verified revision `amftms-api-b-27dd7a5f-4070-4d88-b8bc-2cec3032abc2` and digest
`sha256:c4fe4a742b9750d2cec36d63a3aff01f820059719348cfb7dfb8a376ec85086f`. The
deployment wrapper verified the exact image and 100% observed traffic. The
public liveness endpoint returned Healthy.

Read-only checks at 04:33 UTC showed 11007 on selection version 29, calculated
at 04:30:26 UTC, with September 16 pricing and four matching arrival estimates.
September 17/18 arrivals without published quotes explicitly used estimated
September 16 prices. The operational fuel refresh for 54777 retained the
previous snapshot: no eligible post-delivery priced station remained after
rejecting the unplanned border crossing. The independent fuel job now exposed
that refusal instead of reporting a successful refresh.

Truck 11005 retained its one-stop version-28 fuel snapshot. Diagnostics
identified two additional connection blockers. Completed native history and a
delivered source load still marked `sent` could outrank the active native leg by
imported appointment dates. The later 1388-to-1384 connection retained an
indefinite address retry even though its endpoints were now usable under the
existing saved location policy. Deadhead input identity did not include location
readiness.

The final follow-up makes active native delivery ownership take precedence over
completed history while preserving the ordering of future assigned loads.
Deadhead signature version 2 includes stable endpoint-readiness booleans, not
verification/retry timestamps. This requires a one-time bounded rebuild of saved
connections; subsequent verification renewal does not force another rebuild. An
unresolved address remains blocked until its usable inputs change.

Selection version 30 handles missing post-delivery quote coverage with a
conservative reserve-only policy. It preserves at least half the physical tank
and the configured buffer plus reserve without inventing an exit station,
cross-border access or replacement price. Unknown post-delivery costs are not
presented as a quoted free purchase. Finite positive quotes remain required for
priced exit policies. Manual edits use the same validation and the terminal
itinerary's execution identity.

The third release used Cloud Build `95876bcd-852d-4de5-b65e-05540abed5d1`,
verified revision `amftms-api-b-95876bcd-852d-4de5-b65e-05540abed5d1` and digest
`sha256:852be6108ba0c863b68ada9001e5363f3ca1f147d751ea53089575ddbdc6bc91`. The
full gate passed Node 561/561, Client 1011/1011 and Server 2086/2086. Exact
image readiness and 100% observed traffic were verified; public liveness
returned Healthy. Release evidence is pinned in
`artifacts/managed/release-JqApxe`. The preceding responsive smoke is pinned in
`artifacts/managed/browser-ui-GdKqpD`; Client code did not change between gates.

Operational reads at 05:18 UTC confirmed 11007 on selection version 30 with
September 16 pricing and four matching arrival estimates. They also identified
two unresolved paths rather than proving full recovery. Truck 11005's calculated
multi-dispatch native horizon was rejected by obsolete persistence validation
requiring every stop to share the root execution scope. Truck 54777 had no
eligible priced candidate after removing the cross-border occurrence, and its
configured 125-gallon arrival floor could not be met without purchasing fuel.
The existing user browser reproduced the failed automatic calculation. Neither
retained old snapshot is evidence of a successful new calculation.

Fourth-release integration fixes preserve the exact native root while allowing
signed, ordered legacy future groups in fuel persistence and scheduling. Four
end-to-end SQLite cases calculate and save that horizon through the real fuel
service/store. Fourteen store round-trip/corruption cases retain ownership,
geometry and optimistic-concurrency checks. Concurrent deadhead preparation now
accepts the independently saved winner instead of overwriting it or surfacing an
expected race as an unexpected calculation failure.

Further diagnostics found that 54777 had fresh CAD station quotes but no CAD/USD
conversion rate. Conversion excluded those stations before route selection. The
fourth release adds an official Bank of Canada rate through Application
contracts and an Infrastructure adapter. The existing synchronization loop
refreshes a separately leased checkpoint hourly. Profile reads use its bounded
cache without provider calls; explicit manual rates take precedence. Invalid,
future or expired rates are rejected, and failed refreshes preserve the last
valid record. No schema migration or new background worker was needed. Fuel
profile changes trigger the normal protected refresh without invalidating
unchanged road choices.

Manual fuel editing now uses the same route-country access guard as automatic
selection. An old cross-border recommendation cannot be saved as a newly valid
manual plan by bypassing the automatic guard. Failed validation preserves the
previous saved plan.

The fourth full release gate passed 2,175 server tests. The published artifact
passed its 273-asset and seven-module-graph checks. Responsive fixture checks
passed 52 page/viewport/theme/font combinations with no reported browser errors
or unexpected requests. Evidence is pinned in `artifacts/managed/release-LBEz3e`
and `artifacts/managed/browser-ui-rTWiCB`. These checks stub the map
provider/GPU module and do not establish live map behavior or production
performance. The fourth release used Cloud Build
`89d5b824-1b4e-4e9c-8102-a5a341c61886`, verified revision
`amftms-api-b-89d5b824-1b4e-4e9c-8102-a5a341c61886` and digest
`sha256:f57d60c8a8b5e01ce71f3481903bc226477440ee7a2ffbf802ec7713735c7197`. The
wrapper verified Ready state and 100% observed traffic, rechecked after live
acceptance. Public liveness returned Healthy. No schema migration was required.

Post-deployment production reads and the existing authenticated browser
confirmed:

- 11005 automatically saved selection version 30 at 06:17:50 UTC. Its accepted
  AMF1383 native route remains version 4/revision 2, without the address
  warning. The fuel horizon includes AMF1388 and AMF1384: five remaining stop
  estimates, matching assignments and a LOVES #706 purchase before the final
  delivery. The browser shows the full five-stop timeline, 81% / 202 US gallons
  at the current delivery, and the future purchase. No stop/address override was
  made.
- 54777 automatically saved selection version 30 at 06:19:30 UTC. Both saved
  arrival estimates are present: 46.57% / 116.42 US gallons at pickup and 23.16%
  / 57.91 US gallons at delivery. The browser shows 47% / 116 US gallons at
  pickup and 58 US gallons at finish. Its current fuel is sufficient under the
  existing priced-exit policy, so no purchase is required; the obsolete LOVES
  #870 US detour is absent. This is not a claim that a Canadian purchase was
  added. Canadian quotes are now eligible for normal optimization. A subsequent
  browser click on Calculate automatically also completed without the former
  calculation error. The live Google map displayed the route line.
- 11007 automatically recalculated at 06:16:39 UTC with September 16 pricing,
  four matching arrival estimates and its two future purchases. September 17/18
  arrivals still use explicitly estimated September 16 prices where no later
  quote has been published.
  The browser showed AMF1390, 59% / 147 US gallons at pickup and the LOVES #657
  purchase with 173 US gallons remaining at the combined itinerary finish.

The rate checkpoint contains 0.7185456635769203 USD per CAD, observed September
15 and retrieved at 06:16:19 UTC on September 16. A provider-free candidate read
found 11 same-country nearby occurrences for 54777, instead of zero without FX.
All three truck fuel jobs and the exchange-rate job subsequently reported zero
failures and successful independent checkpoints. A filtered read found no
Warning-or-higher entries on the new revision during these acceptance checks.
This is a bounded observation, not a guarantee of future error-free operation.

The operational probe was subsequently extended with rate provenance and numeric
arrival balances, rebuilt with zero warnings/errors, and run read-only. That
diagnostic-only extension did not change deployed application code.

The first final-follow-up local gate passed Node 561/561, Client 1011/1011 and
Server 2080/2080, with zero build warnings/errors and successful offline
responsive smoke. Further background-preparation regressions were then added
before rollout; that earlier gate is not evidence for subsequently changed code.

Cold historical deadhead recovery remains conservative when a completed native
predecessor has no saved connection and is absent from the bounded
source-history candidates. No predecessor is guessed from the most recently
recorded leg. This does not replace route preparation from the authoritative
current GPS position.
