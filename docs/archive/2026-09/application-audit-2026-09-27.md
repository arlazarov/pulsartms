# Application audit, load and safety — September 27, 2026

## Scope and evidence

Read-only review of the whole application after the partial release of
`79bd0517` (API live, frontend awaiting a Firebase login). Source HEAD
`40fc88cb` (`79bd0517` plus release records). Nothing was changed in
code, schema, configuration or data; production was read only through
read-only sessions.

Method. Five inventories were delegated as read-only code reviews (API
surface, background work, shared reads and caches, Client request load,
data safety and tests). Every finding ranked P1 below, and the
correctness claims, were then checked directly against the source, and
where possible against production. Each finding says which: **verified**
(read in code by this audit, and live where stated) or **reported**
(from the delegated inventory, file and line given, not re-read).

This is not a production performance measurement except where a live
figure is quoted, not a penetration test, and not a line-by-line proof.
It follows the [September 26 audit](application-audit-2026-09-26.md),
whose findings A1 to A10 are not repeated.

## Live load evidence

Table scan rates, `pg_stat_user_tables`, two samples 60 s apart at about
04:50 UTC; they cover every consumer (background work, users, this
audit's two reads). These are cumulative scan counters, not
statements: one statement can scan several tables, and a scan is not
attributable to its caller. `pg_stat_statements` is not installed, so
per-statement counts are not available. Raw samples: managed run
`diagnostic-Ge9izA` (`table-stats-sample-1.txt`, `-2.txt`).

| Table | Rows | Seq scans / min | Index scans / min |
| --- | ---: | ---: | ---: |
| Companies | 2 | 271.8 | 2.0 |
| Dispatches | 418 | 210.3 | 775.8 |
| ExecutionLegs | 98 | 185.5 | 709.3 |
| Trucks | 4 | 176.6 | 160.7 |
| SourceRoadRequests | 168 | 6.0 | 24.8 |

A two-row table scanned 4.5 times a second, and four trucks' work
tables scanned about 15 times a second, is the steady cost with at most
a few people using it. Any saving quoted below from these counters is
an estimate until a per-statement count confirms it.

## Coverage inventory

- **API:** 28 controllers, 171 endpoints (92 non-GET, about 77 changing
  state), 4 minimal-API mappings; one MediatR request per action except
  `MessagingController.Context`, which sends four.
- **Background:** 17 hosted services (15 `ApplicationWorker` operations,
  `DatabaseInitializer`, `CacheInvalidationWorker`); only synchronization
  reports a heartbeat.
- **Caches:** ReadCache groups and item families (`planning-inputs`,
  `profile-rows`), PlanningSummaryCache, EtaMemory, RouteDisplayCache,
  the Client's PlanningDisplayCache, FleetNames, and the shared unbounded
  IMemoryCache (HOS clocks, telemetry, route previews).
- **Client polling, visible tab, per minute:** Fleet Map 10 without a
  selection, about 18 to 26 with one; Dispatch Cards 14 (19 while any
  truck lacks a plan), Table and Papers 13, Completed 0; the messaging
  long-poll and unread count about 4 on every page, also while hidden.
- **Consistency auditor:** 10 rules, all registered and reported.
- **Schema:** 73 migrations; 6 rollbacks guard data before dropping it.
- **Tests:** Server categories led by Fuel 84, Dispatch 83, Routing 81;
  42 PostgreSQL tests that skip without the recorded fixture; about 31
  browser probes, of which `uiSmoke` and `messagingTabsSmoke` are gated.
- **Providers:** TomTom (database-reserved daily cap), Google geocoder
  and Places (no daily cap), Samsara, Torque, WhatsApp, Gmail.

## Findings

### F1 — P1: historical road requests retry for ever and say nothing

Verified live and in code. Loads 1341 and 1355 are completed, each with
one completed native leg at revision 1. Their explicit road requests
showed 39 attempts at 04:4x UTC and 43 at 05:06 UTC, no completion,
and the next attempt about five minutes ahead; both legs' stops
have coordinates but no `AddressVerifiedAt` and no `AddressRetryAfter`;
no deadhead or route plan row exists.

1. `ExecutionStopAddressService.VerifyAsync` returns for a leg that is
   not active or planned (`ExecutionStopAddressService.cs:28`), which
   protects accepted history, so a completed leg's stops are never
   verified.
2. `StopLocation.ReliablePoint` accepts imported coordinates for such an
   address only while `AddressRetryAfter` is in the future
   (`StopLocation.cs:187-199`).
3. `StopLocation.ResolveAsync` then geocodes the street address through
   Google (`StopLocation.cs:144-161`), which can fail permanently
   (ambiguous address, no retry time) or temporarily (a five-minute or
   one-hour retry time, `GoogleAddressGeocoder.cs:54-72,150-164`).
4. `BaseRouteOperation.PrepareAsync` catches every
   `RoutePlanningException` without a log
   (`BaseRouteOperation.Prepare.cs:165-168`); `FinishAsync` takes no
   reason, uses the exception's retry time when it has one and
   otherwise backs off to at most an hour (`BaseRouteOperation.cs:248-271`).
   `SourceRoadRequests` has no reason column, and a lost lease on
   completion goes unnoticed (`SourceRoadStore.cs:223-253`).

Not established: which exception it is. Nothing records it; Cloud
Logging has no application entry naming either load in six hours, and
no application warning at all in the last two. The five-minute cadence
(the backoff alone would be an hour by the 43rd attempt) says the
exception carries a finite retry time of about five minutes, so the
permanent ambiguous-address failure is unlikely; a transient provider
failure or another step's pending state fits. Provider cost is
therefore unknown. The same reason-dropping pattern is in
`PlanningRefreshOperation.cs:160`, `FleetSynchronizationOperation.cs:283-291`
and `FleetSynchronizationOperation.Planning.cs:96-104` (reported).

Proposals: (a) first, record the reason: the last failure message,
time and retry on the road request, or one Information log per
dispatch, input signature and message; this identifies the exception
without guessing. (b) Then park a permanent failure until the request's
input signature changes, as the deadhead worker does. (c) Only with an
authoritative policy from the owner: how a completed leg's unverified
stop may be located. Accepting imported coordinates is one possible
policy, not a finding. No forced routing, no edit to accepted stops, no
reset of pending work.

### F2 — P1: the carrier list is read several times a second while idle

Verified in code and live. `FleetSynchronizationOperation.RunJobAsync`,
called without a company, reads the active carriers
(`CompanyPasses.ForEachCompanyAsync` → `CompanyRoster.ActiveAsync`, an
uncached query) before it checks whether the job is due
(`FleetSynchronizationOperation.cs:253-268`). Its loops wake every 1 s
and 5 s (`FleetSynchronizationOperation.Feeds.cs:81,110,155,174`,
`...Planning.cs:164`). Live: 271.8 sequential scans a minute of a
two-row `Companies` table.

Proposal, preserving each company's own schedule and the discovery of a
new company: keep the roster in ReadCache under the company-catalog
group, invalidated by the commands that create, activate or deactivate
a company and bounded by `ReadCacheSeconds`, so each tick still runs
every company's due check against its own `NextRun`. Moving the
`NextRun` check ahead of the roster alone would not do: the per-company
schedule lives inside the per-company call. Estimated effect, from the
scan counters only: up to about 270 fewer `Companies` scans a minute;
to be confirmed by a query-count test over idle ticks and a repeated
sample. Tests: idle ticks read the roster once per cache lifetime; a
new company is run within one lifetime; each company keeps its
schedule; an inactive company stops.

### F3 — P1 (latent): HOS clocks are cached for all carriers under one key

Verified in code. `SamsaraDriverHosProvider` caches the clock table under
the fixed key `samsara:hos-clocks` with one process-wide lock
(`SamsaraDriverHosProvider.cs:15-16,32,82`), while Samsara credentials
are per company. With one carrier there is no exposure today; with a
second, one carrier's clocks, or an empty table, can be served to the
other for up to a minute. `RoutePreviewService.cs:30-35` fixed the same
pattern. Proposal, first in the post-publication batch: company in the
key and one gate per company in a bounded map, credential change
invalidating that company's entry. Tests with two companies: cache hit
per company; refresh per company; an empty table for one never served
to the other; a provider error for one not cached for the other;
concurrent reads for both neither cross nor serialize; the gate map
stays bounded; a credential change clears only its own entry.

### F4 — P2: board planning summaries are read for a different page

Verified in code. `GetDispatchPlanningSummariesHandler` builds the board
query from page, search, truck and date only, and
`PlanningReadService.ReadBoardInputsAsync` asks with
`IncludePlanned = false` (`PlanningReadService.cs:96-110`); the screen's
board uses `includePlanned=true` and `InChosenGroup = true`
(`DispatchBoardRequest.cs:18`, `DispatchController.cs:105,123,136`).
Page 1 usually agrees; later pages can name other trucks, whose
summaries the Client drops (`DispatchList.razor.cs:733`), and the
different filters build a second board index. Not reproduced live.

Authorization: the chosen group is a dispatcher's view preference, not
an access boundary ("Narrow only the page a dispatcher reads, never
shared planning reads", `5f4c6733`; `docs/features/driver-groups.md`).
Every read stays company-scoped through the query filter, and the
planning endpoint has the fallback (signed-in) policy only, like the
board. So the mismatch is identity, not a leak. Proposal: the Client
sends the truck ids on screen (at most 12); the server keeps only ids
of its own company and resolves them through planning inputs; tests
for a foreign id, a page with planned-only trucks and a narrowed group.

### F5 — P2: work is read two or three times per request or cycle

Messenger (verified, measured): cold 17 statements, warm 5, recorded as
debt at the release. Reported beside it:

- `GetDriverDutyStatus` in the same Messenger request reads the planning
  inputs again (`GetDriverDutyStatus.cs:63`, `PlanningSummaryReader.cs:84`);
  verified to be a hit on the entry `GetDriverWork` just filled, so no
  database statement, only the HOS clocks from memory. Not a repeat.
- Verified: the summary worker reads each running truck's work fresh,
  cached, and fresh again every 30 s
  (`PlanningSummaryOperation.cs:170-186`), each
  read loading the whole `Trucks` table
  (`ExecutionWorkReader.cs:58-72`), for up to 128 trucks.
- Verified: the board ETA step reads the page's itineraries afresh
  (`EtaChainInputsService.cs:94-110`) moments before the summaries read
  them through planning inputs.
- Verified: a board page view runs the board handler three times: the board and
  two enrichments, each re-reading execution loads and details
  (`GetDispatchBoardEnrichment.cs:21-29`,
  `DispatchList.Enrichment.cs:76-79`).
- Verified: the itinerary re-reads older (source, no execution leg)
  loads with every stop (`TruckItineraryReader.cs:81-103`) already read
  in the same snapshot.

Proposal, one change of owner rather than five local ones:
`ExecutionWorkReader` owns the relevance filter once (the SQL
pre-filter is wider; `ExecutionWorkRelevance.IsCurrentOrUpcoming`
decides), the itinerary carries the rows it read (customer, order), and
consumers inside one snapshot derive their views from that read instead
of reading again. Snapshot and assignment checks stay; freshness stays
where it is (Messenger keeps its fresh read unless root accepts the
planning-inputs lifetime). Each step lands with a query-count test and
a before/after table-scan sample.

### F6 — P2: the map and board poll planning harder than needed

Verified in code: the map's 10 s location poll awaits the selected
truck's planning refresh (`FleetMap.razor.cs:379-393,727-767`), whose
only throttle applies after a validation failure (`:743,791`), and
`PlanningDisplayCache.RefreshAsync` coalesces concurrent calls only;
the board's planning post stays at 10 s while any truck lacks a plan
(`DispatchList.razor.cs:678-687`); `next-routes` skips the poll only
when no route is pending and every deadhead is known
(`FleetMap.NextLoads.cs:84-93`). Reported: the longest routes, over the
Client cache's geometry bound, come down in full on each refresh
(`PlanningDisplayCache.cs:12,218-223,467-469`), and switching Cards,
Table and Papers downloads the same board again
(`DispatchBoardRequest.cs:7,12-18`). Proposal: send the known plan
version on every planning read, back off the pending cadences, and key
the board by URL, not view.

### F7 — P2: an unrelated peer resets a failing request's backoff

Verified in code. `GetNextLoadRoutes` requests preparation with one
geometry revision computed over every upcoming load's versions as the
demand identity (`GetNextLoadRoutes.cs:185-201`), and the store resets
`Attempts` and `AvailableAt` whenever the identity changes
(`SourceRoadStore.cs:85-124`). So a failing load loses its backoff when
another load on the truck changes, although its own inputs did not.
Claims order by priority then time, 10 a minute for all carriers
(reported, `SourceRoadStore.cs:160`). Not reproduced, and not shown to
have caused the historical starvation seen during recovery.

Proposal: the identity for a load's own request is that load's own
input and connection signatures. Unchanged inputs keep the backoff;
a relevant change (its base inputs, its predecessor connection)
resets it so recovery is immediate; a peer's change does not. Tests
for those three cases.

### F8 — P2: manual syncs are open to every signed-in user

Verified in code. `POST /api/dispatch/sync` and `POST /api/fleet/sync`
carry only `[Authorize]`, their controllers have no class policy, the
handlers (`SyncDispatche.cs:48`, `SyncFleet.cs:26`) check no role and no
cooldown, and both take the server-wide lock background sync uses
(`DispatchController.cs:95-98`, `FleetController.cs:59-62`,
`ProcessGates.cs:9-10`). Fuel import and IFTA sync require Admin.
Proposal: Dispatch policy and a cooldown. Also reported: controllers
relying on the fallback policy (`DispatchController.cs:77,100,107,113,156`,
`FleetController.cs:64-107`), planning writes without the Dispatch
policy (`RoutePlanningController.cs:61,73,91,102,113,152,203`), and writes
without concurrency tokens (`PUT planning/profile`, `POST planning/route`,
user PATCH, driver-group DELETE and others).

### F9 — P2: stateful work the auditor does not watch

Verified against the rule register. No rule covers: a leg whose tracking
passed every stop but was never completed (1403 and 1385 on
September 26 and 27); road requests past an attempt bound without
completion (F1); deadheads and rates against their inputs; fuel sends.
Proposal: add the first two as bounded, company-scoped rules.

### F10 — P3: writes and provider use repeated without change

Verified: truck positions are rewritten on every publish, because the
publish stamps `ObservedAt` with the current time
(`FleetSynchronizationOperation.Publication.cs:90,101`) and the store
skips only rows not older than that (`TruckLocationStore.cs:70`); with
four trucks the cost is small. Reported: the base-route scan re-reads 100 loads and
upserts each every minute, with uncached per-truck profiles
(`SourceRoadInputs.cs:68`, `BaseRouteOperation.cs:228-245`); Google
Places has only a per-pass batch of 15 and no daily cap; prewarm shares
the TomTom daily cap with dispatchers (`TomTomRoutingProvider.Budget.cs:175-214`).

### F11 — P3: rollout adoption loop runs for ever

Verified in code, severity lowered from the inventory's report.
`DatabaseInitializer` checks every table for ownerless rows every five
minutes for the life of the process (`DatabaseInitializer.cs:39`); for
`ExecutionLegRevisions` it disables the immutability trigger only when
such a row exists, inside the adopting transaction, and re-enables it
before commit (`:115-133`), so other sessions never see it disabled.
Remaining cost: one look per table every five minutes, and a table lock
with owner privilege when it acts. Proposal: stop the loop once a pass
finds nothing after the previous revision has drained.

### F12 — P3: verification and schema hygiene

Reported. The gate passes while the 42 PostgreSQL tests skip for lack of
a fixture (`Support/RequiresPostgres.cs`); 22 Server test classes lack
the Kind trait and one uses an invalid Kind (`MessageSearchPostgresTests.cs:20`);
the Persistence category has no `test.sh` group. Most migration rollbacks
drop data unguarded, and `migrate.sh:20` applies to whatever database
the API configuration names. The API references Domain in seven files
although the rules forbid it; the architecture test only rejects
`Domain.Entities` (`LayerBoundaryTests.cs:248`). `.dockerignore` lacks the
secret patterns `.gitignore` has.

### F13 — P1: the key ring that protects every secret is not protected

Verified in code. Data Protection keys persist to PostgreSQL with no
`ProtectKeysWith` (`Infrastructure/DependencyInjection.cs:80-83`), and
the integration credentials (`IntegrationCredentialStore.cs:24,279-280`),
storage refresh tokens (`StorageSecrets.cs:27-32`) and border private
data (`BorderDataProtection.cs:9-10`) are encrypted with those keys. Any
reader of the database or of a backup in `local-backups/` can therefore
decrypt WhatsApp, Gmail and Drive secrets. The release guide already
notes the plain key XML; this audit ranks it. Proposal: protect the key
ring with a key held outside the database (Cloud KMS or a Secret
Manager certificate), rotate once protected, and treat existing backups
as secret-bearing. Needs an owner decision on key custody.

### F14 — P2: the sign-in limit is probably one bucket for everyone

Verified in code: anonymous callers are partitioned by
`Connection.RemoteIpAddress` (`RequestLimits.cs:108-109`), sign-in and
refresh share 10 per 5 minutes (`:65-77`, `AuthController.cs:31`), and no
forwarded-headers handling exists anywhere in `Server`. Inferred, not
reproduced: behind the Firebase Hosting rewrite and Cloud Run the
connection address is the proxy's (the request log shows a Google proxy
address, `66.102.6.196`, as the caller), so one client could exhaust
sign-in and refresh for all users, and the lockout (5 failures, 15
minutes) lets anyone who knows an address lock that account. Proposal:
forwarded headers restricted to the platform proxies, partition by the
forwarded client address, and a check against real request logs.

### F15 — P3: authentication and provider details

Verified by the delegated review (file and line given), not re-read:
refresh tokens are stateless and remain valid after use, with no reuse
detection (`AuthService.cs:70-110`); tokens live in `localStorage` with
no Content-Security-Policy (`authStorage.ts:41`); logout clears the
session cache on its own instance only (`ReadCache.cs:156-160`,
`SessionValidationMiddleware.cs:42-49`); Drive disconnect deletes the
stored secret without revoking it at Google (`StorageConnections.cs:190`),
and a Drive consent link is bound to company and connection, not to the
browser that started it; the WhatsApp webhook answers 404 for an unknown
company key and 401 for a bad signature (`ReceiveDriverMessages.cs:77-97`);
every Gmail push runs a full fuel-discount import and ignores the
history id (`ImportFuelDiscounts.cs:72`); production maps use Google's
`DEMO_MAP_ID` (`fleetMap.ts:74`, `dispatch.ts:87`). Sound as reviewed:
the WhatsApp signature (HMAC-SHA256 over the raw body, per-company
secret, constant-time, size-capped, replay harmless by message id), the
Drive OAuth flow (nonce, PKCE S256, one-time state, HTTPS redirect from
configuration, `drive.file` scope), Gmail push validation (audience,
verified email, service account). No secret value of eight or more
characters appears in any committed JSON file in the 650 commits.
Migrations that drop or rewrite data in `Up` are listed in the evidence
run; `RebuildExecutionStorage` refuses to run on populated tables.

## Proposed order, for root

First batch after the frontend publication (tenant isolation and
safety first, each small and separately tested):

1. F3 HOS clocks per company, with the two-company cases listed there.
2. F2 roster through ReadCache, preserving per-company schedules and
   new-company discovery; a query-count test over idle ticks.
3. F1 (a): record the road-request failure reason; then decide (b) on
   evidence, and (c) only with an owner policy.
4. F8 Dispatch policy and cooldown on the manual syncs.

Then: F14 forwarded addresses; F13 key custody (owner decision); F4
page identity; F7 per-load identity; F5 shared work read in steps; F6
cadences; F9 auditor rules; the P3 items.

Each fix: affected test groups during work, a before/after table-scan
sample for load claims, the full gate only for publication.

## Gaps

- No per-query production counts (`pg_stat_statements` absent) and no
  latency or payload measurement; costs outside the table sample are
  inferred from code.
- Findings marked reported were not re-read line by line; every P1 and
  every finding named in root's review was verified.
- Not covered: database roles and who can read backups; Cloud Run
  ingress and the proxy chain; deployed key-rotation settings; the
  cross-instance cache relay; admin unlock paths; Google API key
  restrictions; Samsara and BVD beyond timeouts; tests' content.
