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

### F3 — P3 (latent, corrected): a dead HOS read path under a shared key

Verified in code, and corrected after tracing the callers. The Samsara
provider caches under the fixed key `samsara:hos-clocks` with one static
gate (`SamsaraDriverHosProvider.cs:15-16,32,82`). But production reaches
it only through `IDriverHosRefreshProvider.RefreshClocksAsync`
(`IDriverHosRefreshProvider.cs:7`, the one consumer
`DriverHosRefreshOperation.cs:53-54`), and a refresh never reads that
cache; it only writes it. Every request reads clocks from
`DriverHosSnapshot`, a singleton already keyed by company
(`DriverHosSnapshot.cs`, `CompanyState` per `ICurrentCompany.Id`). The
non-refresh `GetClocksAsync` that would serve the shared entry is called
only by tests (`DriverHosTests.cs:74-75,136-147`). So no carrier can be
served another's clocks today. What remains: a dead read path that would
leak the moment anything wires it, a dead cache write, and a static gate
that queues every company's refresh behind the others. The earlier P1
ranking is withdrawn.

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

## Coverage closure (September 27, afternoon)

Root asked for the inventory to be closed before more fixes: per-endpoint
role and tenant scope, migration recovery, financial and idempotency
owners, state and import recovery, shared reads, and test content. Four
more read-only code reviews did this (no build, no database, no
network). Their full reports, with the per-endpoint table of all 171
endpoints, are pinned in the main checkout's
`artifacts/managed/diagnostic-XrL576`. Findings below say **verified**
when this audit re-read the lines itself, **reported** otherwise.
One production read, read-only: `DriverMessages` has 0 rows and
`FuelVisitSends` 0, so WhatsApp fuel hand-over has never been used and
F16/F17 have no existing invalid rows.

Inventory, as counted by the reviews:

- **Endpoints:** 171 in 28 controllers (79 GET, 63 POST, 25 PUT,
  3 DELETE, 1 PATCH) and 4 minimal-API mappings. 45 Admin-only; 79 under
  the Dispatch policy; 41 any signed-in user; 6 anonymous (sign-in,
  refresh, two WhatsApp webhook verbs, Gmail push, Drive callback).
  Every tenant table is filtered by `CompanyId` through the global filter
  (`AppDbContext.Companies.cs:51-106`); the shared tables are listed in
  `SharedTables.cs:18-56`. No endpoint found reading another carrier's
  rows; the exceptions are yes/no probes (F27).
- **Migrations:** 73; 18 change or drop data, 55 are additive. Every
  NOT NULL column added to a populated table has a default; no step runs
  outside a transaction.
- **Money:** every figure has a server owner; the Client formats values
  only, except the two derivations in F27.
- **External sends and inputs:** 16 paths. Conversation and broadcast
  sends are fenced and leased with a reaper; inbound WhatsApp is
  deduplicated by a unique provider id; TomTom calls are reserved in the
  database before they are made.
- **Stateful workflows:** 16; caches: 6 owners plus the shared
  `IMemoryCache`. Auditor rules: 10 in code; the operating guide lists 9.
- **Tests:** Server 2,348 facts in 443 files, Client 806, JavaScript
  641. Category traits are enforced; 42 PostgreSQL tests skip with a
  named reason when no fixture is present.

### F16 — P1 (latent): a stuck fuel hand-over blocks that truck's plans

Verified. A WhatsApp fuel hand-over is committed as `Sending` before the
provider call (`DriverTextDelivery.cs:127-134`). Nothing moves a
`DriverMessage` out of `Sending` after a crash: the reaper and the
auditor rule read `ConversationMessages` only
(`OutboundMessageOperation.cs:109-117`, `OutboundOverdueRule.cs:35-45`).
`FuelIssueRecords.RequireUnchangedAsync` refuses every fuel publication
for the truck while such a row exists (`FuelIssueRecords.cs:48-60`), so
background refreshes fail for ever and dispatchers see "a fuel stop was
handed to the driver during the calculation". A send-again adds a new
attempt and leaves the old row. No test covers publication after it.

### F17 — P1 (latent): an accepted hand-over can go unrecorded

Verified. The hand-over (`FuelVisitSend`) is recorded only after the
provider accepted and the attempt was committed
(`FuelIssueSender.cs:98-106`). If recording fails or the process stops
in between, a retry of the same content returns `AlreadyTaken`
(`DriverTextDelivery.cs:107-108`), which falls to `default: return
Outcome.Done` (`FuelIssueSender.cs:142-143`) and records nothing. Fuel
planning can then drop a stop the driver already holds.

### F18 — P2: the Dispatch policy restricts nothing

Verified. Any active user without exactly one Admin claim resolves to
Dispatch (`UserRoleService.cs:101-102`) and the policy admits Admin or
Dispatch (`DispatchAuthorization.cs:21`). So 120 endpoints, 65 of them
state-changing, are open to every signed-in user: WhatsApp broadcasts
and sends, a driver's messaging number, expenses, IFTA movements,
broker records, truck route profiles. This corrects the F8 proposal:
putting the syncs under the Dispatch policy would change nothing. Whether
a limited role is wanted is an owner decision.

### F19 — P2: one carrier's Admin writes data every carrier reads

Reported, entry points re-read. The fuel-discount import creates and
overwrites the shared `FuelStations` from one carrier's mail
(`FuelController.cs:50-52`, `FuelStationSync.cs:115-147`); the IFTA
sync rewrites the shared rates (`IftaTaxRateSync.cs:19-36`, public
data, lower risk). Gmail push is fixed to AMF
(`GmailPushValidator.cs:11`, verified), so a second carrier's mailbox
cannot be served. Blocks selling the product, not today's operation.

### F20 — P2: one bad email stops the fuel import; old mail is lost

Reported. An empty or unreadable attachment throws for the whole run
(`ImportFuelDiscounts.cs:44-50`, `BvdFuelParser.cs:37-47`); the message
is never marked, so every push and recovery fails on it again. The
mailbox query is `newer_than:2d` (`GmailAttachmentService.cs:25-27`), so
an outage over two days loses messages with no quarantine.

### F21 — P2: a load import can put back older data

Reported. The dispatch gate is per process (`ProcessGates.cs:3-6`); the
leased loop, a manual sync on another instance and the history-import
tool can run together. The provider is read before the transaction
(`SyncDispatche.cs:86-87` vs `:128-132`) and the source carries no
version, so a slower pass can restore an older price, status or miles.
Not reproduced.

### F22 — P2: load cost totals are summed over a truncated list

Verified. `GetLoadCosts.cs:47-64` takes 201 rows, drops the last, then
totals what is left; `truncated` is set but the totals are wrong for a
load with more than 200 cost rows. No such load checked in production.

### F23 — P2: three durable queues retry for ever without escalation

Reported. `SourceRoadRequests`, `PlanningRefreshRequests` and
`ExecutionPlanningChanges` have no attempt cap (`SourceRoadStore.cs:164`,
`PlanningRefreshStore.cs:89`, `ExecutionPlanningStore.cs:31`);
`SourceRoadRequests` has no auditor rule at all. This is the mechanism
behind F1; D1 records the reason, a cap and a rule remain.

### F24 — P2: schema compatibility is procedure only

Reported, startup path re-read. Nothing at runtime refuses an old binary
on a newer schema; `deploy-server.sh` migrates while the old revision
still serves. The company adoption pass is skipped when
`Database:ApplyMigrations` is false, and its first run is not guarded,
so a failing adoption stops every start (`DatabaseInitializer.cs:22-28`).
The 17 migrations after `RecordRouteMovement` drop messaging and file
data in `Down` with no guard, and `IntroduceCompanies` `Down` would merge
carriers; today only the throwing `Down` of
`IsolateCarrierIntegrationCredentials` stops a rollback reaching it.

### F25 — P2: fuel-plan cost formulas have five copies

Reported. Per-stop economic cost and future-fuel cost are computed in
`FuelOptimizer.cs:84-125`, `FuelOptimizer.States.cs:79-82`,
`FuelManualReplay.cs:183-267`, `FuelPlanProjection.Project.cs:270-285`
and `FuelPriceMateriality.cs:44-61`, and the copies already differ
(guards, which costs are repriced, access minutes). All on the server,
but against the one-owner rule.

### F26 — P2: the shared memory cache has no bound

Reported. The host `IMemoryCache` has no `SizeLimit`
(`Application/DependencyInjection.cs:82`) and sits outside the 80 MiB
`CacheBudgets`; stop geocodes stay 12 hours, unbounded in count. The
planning-summary `Committed` notice is per process, so another instance
corrects only on its next signature change or 30-second pass.

### F27 — P3: smaller items

Reported unless marked: IFTA rates keyed without currency
(`IftaTaxRateConfiguration.cs:19-26`, not checked against the source
file); a delivery status that arrives before the provider id is saved is
dropped (`ReceiveDriverMessages.cs:157-202`); a second Total RPM formula
(`DeadheadService.cs:341-345`) and stored savings that the read ignores
(`FuelDiscountSync.cs:49`); the litres-per-gallon constant four times,
one in the Client (`stationQuantity.ts:24`); the Client derives
yesterday's price (`stationPriceComparison.ts:74-78`); fuel-stop price
dates use the offset's local day, not the Toronto business day
(`FuelPriceCalendar.cs:53-55`); stop geocoding has no durable dedup
(`GoogleAddressGeocoder.cs:17-127`); the deadhead publication re-check
compares a copy with itself (`DeadheadService.Ensure.cs:146`); process
diagnostics and readiness are shown to any carrier's Admin; the
credential store and Identity answer yes/no about another carrier's
WhatsApp number and e-mail (`IntegrationCredentialStore.cs:169-195`,
`IdentityService.cs:22-28`); `EtaForecastStore.cs:221,246` upserts
without a company predicate; caches keyed without company (EtaMemory,
route display, Samsara HOS, latent); the auditor guide lists 9 of 10
rules and `storage.file-on-disconnected-storage` is tested only under
PostgreSQL; no expired-lease reclaim test for the planning-refresh and
road stores, none for odometer capture; about nine tests assert only
that some error exists; `CheckpointLeaseStore` mixes the system clock
with the injected one (`:77,99`); `migrate.sh` applies each migration as
soon as it is added.

Checked and sound, as reported: sign-in and refresh take the company
from the database, never the request; the WhatsApp, Gmail and Drive
anonymous paths authenticate as described in F15; conversation sends,
broadcasts, inbound messages, expenses and TomTom calls are idempotent
under retry; every tenant read goes through the global filter.

## Proposed order, for root

Bounded; each item small, separately reviewed and tested, published
only through the gate.

1. **D2** roster through ReadCache — implemented locally (`07631585`),
   waiting for review.
2. **F16 + F17** fuel hand-over recovery (design D6): a reaper that
   turns a `DriverMessage` past its lease into `Uncertain`, publication
   that stops counting such a row, `AlreadyTaken` recorded from the found
   attempt, an auditor rule, and regressions for crash-after-accept and
   crash-before-accept. No existing rows to repair.
3. **D1** road requests say why they wait, plus an attempt cap and an
   auditor rule for `SourceRoadRequests` (F23).
4. **F22** cost totals computed in the query, not over the page.
5. **F20** fuel import: one message's failure is recorded and skipped;
   the window follows the last imported message, not two days.
6. **F8/F18** manual syncs Admin-only now; a limited role waits for the
   owner.
7. **D3** dead HOS read path.

Owner decisions before code: F18 role model, D4 key custody (F13), D5
proxy chain (F14), F19 shared stations for more than one carrier.
Designs needed before code: F21 source versions, F24 schema guard,
F25 one fuel-cost owner, F26 cache bound (a `SizeLimit` makes every
entry declare a size). Then F4-F7, F9 and the P3 items.

## Implementation designs (not implemented)

### D1 — road preparation says why it waits (F1 a)

Owner: `BaseRouteOperation` (Application, Routing background). No schema
change. In `PrepareAsync`'s `RoutePlanningException` branch, log once at
Information when a dispatch's (input signature, message) pair is new or
has changed: template `Route preparation for {DispatchId} waits:
{Reason}; retry {RetryAfter}; attempt {Attempts}`. The operation, a
singleton, keeps the last pair per dispatch in a bounded map
(`RoutePreparationOptions.StateCapacity`, least-recently-seen evicted)
and forgets a dispatch on success. Messages are the application's own
fixed texts (checked: TomTom, Google and stop-location messages carry no
provider payload or address beyond a stop number). Failure semantics: a
logging failure never changes the settle; the settle and retry time are
unchanged. Work counts: no statement, no provider call added. Tests
(`BaseRouteOperationTests`, Kind Integration): one log for repeated
identical failures; a new log when the reason or signature changes; no
log after success; bound respected. Later, with the auditor rule of F9:
an additive nullable `LastFailure`, `LastFailureAt` on
`SourceRoadRequests` written by `SourceRoadStore.CompleteAsync`.

### D2 — the carrier roster is read once per lifetime (F2)

Owner: Application. `CompanyPasses.ForEachCompanyAsync` (15 callers)
reads the roster through a new Application service, `CompanyRosterReader`,
that wraps `ICompanyRoster` in `ReadCache.GetAsync` under a new group
`ReadGroups.Companies` with a 30 s lifetime; `ReadGroupsTests` pins the
new name. No application command creates or deactivates a company today
(companies change by migration or operator), so discovery is bounded by
the lifetime rather than by invalidation: a new carrier starts within
30 s, a deactivated one stops within 30 s. Each company's own schedule
is untouched: `RunJobAsync` still checks that company's `NextRun`
inside the per-company call. The roster is not company data, so it is
read outside any company scope. Estimated effect from the scan counters
only: about 270 `Companies` scans a minute down to about 2 per instance.
Tests (`CompanyPassTests`, Kind Unit, counting fake roster): many passes
within a lifetime read once; after `InvalidateGlobally(Companies)` a new
company is served; each company still runs only when due; an inactive
company is no longer served after invalidation.

### D3 — the dead HOS read path goes (F3)

Owner: `SamsaraDriverHosProvider` (Infrastructure). Delete the
non-refresh `GetClocksAsync`, the cache read and write and the fixed key;
`RefreshClocksAsync` fetches for the current company and returns. Replace
the static gate with one gate per company in a bounded map, so two
companies refresh concurrently and one company's refreshes still do not
overlap. Tests move from the dead path to `RefreshClocksAsync`, plus a
two-company test at `DriverHosSnapshot` level: each company's clocks are
served only to it, an empty or failed refresh for one leaves the other's
clocks intact. Work counts unchanged.

### D4 — protect the key ring without losing what it decrypts (F13)

Owner decision on custody first: Cloud KMS (a runtime dependency on KMS
availability) or a certificate from Secret Manager (no runtime call, a
secret to hold). Recoverable sequence, no rotation and no deletion:

1. Back up the database (the key rows included) and record the count of
   stored credentials that decrypt today, by provider, without values.
2. Deploy `ProtectKeysWith…` for keys written from then on. Existing
   plain keys stay readable: Data Protection decrypts key XML only when
   it is encrypted.
3. Re-protect the existing key XML in place with a one-off tool that
   reads each key element, encrypts it with the chosen protector and
   writes it back in one transaction, keeping a copy of the plain rows
   in the protected backup.
4. Verify the same decrypt counts by provider.
5. Rollback: restore the plain rows from the backup and deploy without
   the protector (or keep `UnprotectKeysWith…` for a certificate) and
   verify the counts again.

Needs production permission changes (KMS or Secret Manager access), so
it is a proposal only; backups made before step 3 remain secret-bearing.

### D5 — sign-in and refresh limits that no forwarded address can fool (F14)

Established: ingress is `all` (the `run.app` URL is reachable directly,
not only through Hosting), Hosting rewrites `/api/**` to the service,
and the request log shows a Google Hosting proxy as the caller. A client
calling `run.app` directly controls every `X-Forwarded-For` entry except
the last one Google appends, and for Hosting traffic that last entry is
the Hosting proxy, shared by many users. So no forwarded entry can be
trusted to name the user without an allow-list of Hosting proxy ranges,
which Google does not publish for this purpose. Proposal: partition
sign-in by the normalised account name (plus a higher global cap), and
refresh by the token's user once validated, instead of by address; keep
the address partition only as a coarse global guard. Lockout stays per
account. Tests: one account's failures do not block another; a flood on
one account is capped; refresh of user A does not consume B's budget.

## Coverage matrix

Each area: what was inventoried and verified; what is left open.

- **API endpoints and auth:** all 171 endpoints tabulated with policy and
  tenant scope (evidence run); F18, F19, F27. The table is now executable
  (below). Open: proof of the proxy chain (D5).
- **Background work:** 17 hosted services and 16 stateful workflows with
  their recovery; F16, F23. Open: liveness of operations without a
  heartbeat.
- **Shared reads and caches:** 6 owners and the shared cache, keys,
  bounds and invalidation; F2, F4, F5, F7, F26. Open: per-statement
  counts.
- **Money and idempotency:** every figure's owner and 16 external paths;
  F17, F20, F21, F22, F25. Open: IFTA source shape, arrival offsets.
- **Client requests:** per-screen rates. Open: payload sizes.
- **Consistency auditor:** 10 rules against the workflows; F9, F16, F23.
  Open: the cost of each rule's SQL.
- **Providers:** 6; exception texts, HOS, ingress. Open: Google key
  restrictions.
- **Authentication:** login, refresh, logout, key ring, limiter. Open:
  proxy chain, key custody.
- **Schema and migrations:** 73, with the 18 non-additive ones' Down and
  rerun behaviour; F24. Open: none beyond F24's design.
- **Tests and gates:** content reviewed for asserts, categories,
  PostgreSQL skips and missing regressions; F27. Open: a PostgreSQL
  fixture for the 42 skipped tests.
- **Live data:** scans, loads 1341 and 1355, hand-over rows. Open:
  per-statement counts.

## Follow-up (September 27, evening)

Focused checks only; nothing released from this branch.

- **Endpoint rules are executable.** `EndpointAuthorizationTests` reads
  every controller action's effective rule from its attributes -
  anonymous, named policies (all must pass), or any signed-in user - and
  compares it with `Server.Tests/Architecture/EndpointAuthorization.txt`
  (171 lines); a new or changed endpoint fails until its line is written.
  A second test pins the six anonymous endpoints (sign-in, refresh, the
  Gmail push, the storage callback, the two WhatsApp webhook verbs). The
  counts match the review once `70eb2e98` (F8 proposal: the two manual
  syncs Admin-only) is counted: Dispatch 79, Admin 46, Admin and
  Dispatch together 1 (mileage policy), signed-in 39, anonymous 6. The
  four minimal-API mappings in `Program.cs` are not controllers and are
  not in the table.
- **Tenant filters are checked in the built model.** Classification
  (`CompanyOwnershipTests`) did not prove the filter: it is applied in one
  loop and a later `HasQueryFilter` on the same table would replace it.
  `EveryCarriersTableIsFilteredByTheServingCarrier` walks each carrier
  table's filter expression for `CompanyId == ServingCompany`.
- **F22 on PostgreSQL.** The totals past the page are summed in the
  database; `LoadCostsPostgresTests` runs that read on the isolated
  fixture (150 USD toll and 60 CAD fuel shares, written as rows in one
  save; through the command the same test took 12 minutes).
- **PostgreSQL skips.** The recorded fixture now runs the PostgreSQL
  tests (the release gate of `0e6add5d` on another branch skipped none);
  the "42 skipped tests" gap stands only where no fixture is recorded.
- **D1 and D2 evidence** still needs this branch released: D1 is done
  when a wait reason is logged for loads 1341 and 1355, D2 when the
  roster read rate is measured after release.

Mutations, each killed (diagnostic-WMUKSO in this worktree): a
controller's policy dropped, an endpoint made anonymous, the tenant
filter without its company comparison, F22 totalling the page alone on
PostgreSQL. Checks: `bash test.sh costs database` with Architecture:
Server 287, Client, JavaScript passed (diagnostic-OYe2Di); a first run
failed on the new worktree's missing Client packages and is marked.

## Follow-up (September 27, night)

- **Tenant scope in handlers.** Every raw SQL and IgnoreQueryFilters use
  in Application and Infrastructure (15 files) is listed with its reason
  (`TenantFilterBypassTests`). One was not safe: `SavedRoutePlanReader`
  read `DispatchRoutePlans` by id in raw SQL without the company, so
  another carrier's plans would be returned for their ids; both queries
  now name the serving carrier (`9b369651`, tested on SQLite and
  PostgreSQL). The forecast upsert's conflict update did not check the
  existing row's carrier either; fixed on `claude/current-work-design`
  (`cc6e53d8`), where that code lives now.
- **Messenger driver work, cold 17 statements.** Measured again
  (`DriverWorkCostTests`): 5 of the handler's own (actor, the driver's
  trucks, the board rows), 12 of the planning capture. Three statements
  repeat word for word - the truck row, the native-work check and the
  load legs. They are not merged: the planning capture is a shared,
  cached and coherent snapshot read in its own transaction, and the
  board rows decide which loads Messenger lists; reading one from the
  other would break the snapshot's coherence or change the list. Warm,
  only the handler's 5 remain. Kept as justified repetition, no change.
- **D6, F16 and F17.** No reaper was needed: Messaging already reads an
  attempt left sending past its two-minute timeout as uncertain.
  - F16: `FuelIssueRecords.RequireUnchangedAsync` refused a truck's fuel
    publications while any attempt was sending, for ever after a stopped
    process. It now refuses only while the attempt may still be in
    flight, by Messaging's own rule.
  - F17: a send of a message the provider already took returned
    `AlreadyTaken` and recorded nothing, so a hand-over accepted before
    a stop stayed unrecorded. Delivery now returns the taken attempt and
    the sender records the hand-over from it, once, without a second
    message.
  - Auditor: `routing.fuel-handover-uncertain` (review: the last attempt
    has no answer) and `routing.fuel-handover-unrecorded` (violation: an
    accepted hand-over with no record for the truck since it was sent).
  - Regressions start from the stopped states (sending left behind; an
    acceptance whose record is gone) and are red on the old code
    (diagnostic-A3DBEV/red.log). No existing rows: WhatsApp hand-over
    has never been used (0 driver messages, 0 visit sends).
  - Root's review of `7db23d82`: the timeout does not prove the provider
    stopped. With the call held past two minutes and a plan published
    meanwhile, a late acceptance recorded the hand-over after the plan
    that dropped the stop, and that plan's withdrawn list could not know
    it: the driver's stop was lost from view (red on `7db23d82`,
    diagnostic-Q7uMxs). `FuelIssueRecords.ApplyAsync` now shows as
    withdrawn a hand-over from an older plan recorded after the viewed
    plan was calculated, for a stop still ahead, that the plan no longer
    has - from the query it already made. A plan that kept the stop shows
    it sent and refuses to send it again; a plain press while the call is
    held is uncertain and sends nothing; the original request found taken
    sends nothing. `AlreadyTaken` is recorded only if the taken attempt
    is the same truck, root leg, assignment and visit set (the key does
    not name the plan's calculation). Limits: an explicit "send again"
    after the uncertain warning is a dispatcher's decision and can send a
    second message if the first was only slow; clocks are assumed shared
    (one instance).
  - Root's review of `292d31aa`: that test published nothing while the
    call was held. `AnAcceptanceHeldOverAPublicationLosesNoStopAndSendsOnce`
    (Routing) now holds a real sender at the provider, lets the attempt
    age past the timeout, and saves the fuel plan through the real owner
    (`FuelPlanningService.EditAsync`) meanwhile - keeping stop A, or
    choosing C - which commits without knowing A went. After the release:
    the prepared summary is no longer current; the plan read through
    `PlanningReadService` shows A sent, or A withdrawn; a plain press
    during and after sends nothing; the provider got one message. Red on
    the `7db23d82` sources (A not withdrawn) and with either the late rule
    or the record's summary notice removed (diagnostic-lLNmFz); green
    diagnostic-bJ0Vun. `PlanningTestServices` now shares one summary
    cache between publication and records, as production does.

- **F23, road requests.** Re-read: retries are already bounded - the
  claim skips waiting rows, and a failure backs off to at most one
  attempt an hour per load - so no work starves; what was missing is
  escalation. A cap would turn endless retries into a silent stop, so
  none was added. `routing.source-road-overdue` (violation, warning)
  reports a request still unfinished 30 minutes after its last demand,
  through `ISourceRoadStore.OverdueAsync`, for the serving carrier only
  (`SourceRoadDemandRule`; tests in `SourceRoadStoreTests`, SQL
  translation in `ConsistencyAuditSqlTests`; diagnostic-3vA9vy, and
  mutations of the carrier, grace, unfinished and cursor conditions all
  fail it, diagnostic-ppPtcs). PostgreSQL behavior not run. The other
  two queues already had rules.
- **Production, read only, 20:45 UTC:** 30 of 168 road requests are
  unfinished, 24 of them past the grace window, and every one is for a
  completed load (execution leg completed, both stops unverified, no
  recorded mileage); one has 50 attempts, eight have 8. The rule would
  report 24 today. Nothing is logged: a wait is silent until D1 is
  released, so which step stops them is not known. Hypothesis, not
  checked: address verification skips completed legs, so their stops
  stay unverified and the base road step fails on them, or reaches a
  provider, on every attempt.

- **F20, fuel import.** Verified by reading: a parse error in one
  attachment threw from the provider for the whole run, and an empty one
  threw in the handler; every later push failed on the same message, and
  newer prices waited until it left the `newer_than:2d` window, which
  also lost any message older than two days after an outage. Now the
  provider marks an attachment it cannot read (`Unreadable`), the
  handler skips only that message and imports the rest, and the skip is
  logged once per process (`FuelImportSkips`, 256 message ids). The
  message is not marked imported. The mailbox is read `after:` two days
  before the last import, at most 30 days back. Limits, explicit: an
  outage longer than 30 days still loses the older messages; a skipped
  message is retried, and a corrected parser imports it, only while it
  stays in the window - about two days once later messages import -
  after which nothing retries it and one warning per process is its
  only trace (stored skips are the open gap below). Red on the old
  handler (diagnostic-td0Jkw); green diagnostic-YWTwxK; mutations of the
  once-only report, the 30-day limit, the window, the provider's catch
  and the query all fail (diagnostic-BBaWUC). Production, read only: 25
  imports since September 18, none on the 19th; no loss seen. No schema
  change: this branch does not contain the released migration 74, and a
  second migration here would have to be ordered at integration.

- **Messaging 503s after the 0e6add5d release.** Twelve 503s on
  `GET /api/messaging/changes` (20:35, 20:49, 20:52 UTC), none logged in
  the three days before. Attribution, proven from the code path and the
  request log (diagnostic-FG2rAH, no IPs or ids): every refused request
  named no mailbox, so each was an open; the waiting bound is checked only
  for a known mailbox and the process bound needs 512 mailboxes, so every
  refusal was the account's share (4). At 20:35:10 four waits with a
  mailbox were in flight from one user agent. Consistent with the log,
  not proven: every one of the 198 waits lasted 20.0 s, none ended early,
  across 13 new mailboxes in 20 minutes - a browser that stops listening
  does not end its request on the server, so each new leader leaves the
  old mailbox held for up to one wait. The 503s come in pairs 0.12 s
  apart and the Client does not retry: an intermediary retrying a 503
  once is the likely reading, unconfirmed. Changes: a refusal now logs
  which bound refused with the counts, never the account
  (`MessagingMailboxes`); a tab that leaves the messaging views and comes
  back asks with its own mailbox under the same account and sign-in
  (`MessagingSignals`, red diagnostic-kgopft, green diagnostic-hPOhI1).
  Tests pin the lifecycle: four leaders restarted within one wait refuse
  the fifth open, which is admitted once the waits end
  (diagnostic-jToIxx); mutations of the scope check and the bound name
  fail (diagnostic-9RkEfZ). Limits: leadership moving to another tab,
  and each separate sign-in of one account, still opens its own
  mailbox; a refusal lasts at most one wait, while the Client backs off
  and polls. Not released.

## Open gaps, owners and completion criteria

- **Which exception holds 1341 and 1355.** Owner: Routing (D1). Done
  when D1 is deployed and a reason is logged for each.
- **A skipped fuel-import message is only logged (F20).** Owner: Fuel.
  Done when, after this branch is integrated with the released
  migrations, the skip is stored with the message and an auditor rule
  reports it, or the Gmail intake is retired.
- **Road requests for completed loads never finish (F23).** Owner:
  Routing. Done when, with D1 released, the step is known for the 30
  requests, and completed legs either get their road or settle without
  retrying hourly - by a decision of the road's owner, not by a cap.
- **Per-statement production counts.** Owner: operations with root.
  Done when `pg_stat_statements` is enabled by owner decision, or a
  sampled statement log exists, and F2 and F5 are re-measured.
- **Hosting proxy chain.** Owner: platform (D5). Done when an Admin-only
  diagnostic has sampled the header shape, with no addresses stored.
- **Key custody for the key ring.** Owner: the owner (D4). Done when
  custody is chosen and D4 steps 1 to 4 pass with equal decrypt counts.
- **Who can read backups and the database.** Owner: the owner and
  operations. Done when roles are listed and backups are classified as
  secret-bearing.
- **Google API key restrictions.** Owner: the owner. Done when the
  referrer and API restrictions are confirmed in the console.
- **Payload sizes of locations, HOS and planning.** Owner: Client (F6).
  Done when measured in a browser trace.
- **Reported findings not re-read (F10 scan, F12, F15, F19, F21,
  F24-F27).** Owner: this audit. Done when each is re-read, or fixed
  with its test.
- **Role model (F18).** Owner: the owner. Done when a limited role is
  chosen or explicitly declined.
- **PostgreSQL fixture.** Owner: tests. Done when an isolated fixture,
  not in Docker, runs the 42 skipped tests in the gate.
- **Liveness of operations without a heartbeat.** Owner: the background
  owners. Done when each has a heartbeat or a documented reason.

The audit is not complete until these are closed or explicitly accepted
by root.
