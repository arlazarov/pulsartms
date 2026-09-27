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
audit's two reads). `pg_stat_statements` is not installed, so per-query
counts are not available.

| Table | Rows | Seq scans / min | Index scans / min |
| --- | ---: | ---: | ---: |
| Companies | 2 | 271.8 | 2.0 |
| Dispatches | 418 | 210.3 | 775.8 |
| ExecutionLegs | 98 | 185.5 | 709.3 |
| Trucks | 4 | 176.6 | 160.7 |
| SourceRoadRequests | 168 | 6.0 | 24.8 |

A two-row table read 4.5 times a second, and four trucks' work read
about 15 times a second, is the application's steady cost with at most
a few people using it. Earlier measurement put one round trip at about
66 ms from the API, whatever it asks.

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
show 39 attempts since 01:00 UTC and no completion; both legs' stops
have coordinates but no `AddressVerifiedAt` and no `AddressRetryAfter`;
no deadhead or route plan row exists.

1. `ExecutionStopAddressService.VerifyAsync` returns for a leg that is
   not active or planned (`ExecutionStopAddressService.cs:28`), which
   protects accepted history, so a completed leg's stops are never
   verified.
2. `StopLocation.ReliablePoint` accepts imported coordinates for such an
   address only while `AddressRetryAfter` is in the future
   (`StopLocation.cs:187-199`).
3. `StopLocation.ResolveAsync` geocodes the street address through
   Google (`StopLocation.cs:144-161`); an ambiguous result is a
   `RoutePlanningException` without a retry time, which means permanent
   (`RetryAfter = MaxValue`).
4. `BaseRouteOperation.PrepareAsync` catches it without a log
   (`BaseRouteOperation.Prepare.cs:165-168`); `FinishAsync` takes no
   reason and treats `MaxValue` as "back off" to at most an hour
   (`BaseRouteOperation.cs:248-271`). `SourceRoadRequests` has no reason
   column, and a lost lease on completion goes unnoticed
   (`SourceRoadStore.cs:223-253`).

Inferred, not observed: the exact exception (nothing records it) and
the Google cost, at most about one geocode per distinct address per
hour, since `GoogleAddressGeocoder` caches a failure for up to an hour
in process memory (`GoogleAddressGeocoder.cs:134-148`). The same
reason-dropping pattern is reported in `PlanningRefreshOperation.cs:160`,
`FleetSynchronizationOperation.cs:283-291` and
`FleetSynchronizationOperation.Planning.cs:96-104`.

Proposals: (a) keep the last failure message, time and retry on the
road request, or log it once per dispatch, signature and message;
(b) park a permanent failure until the request's input signature
changes, as the deadhead worker already does; (c) owner decision for
history: accept imported coordinates as the reliable point for a
completed leg's stop that still equals its imported source
(`MatchesImportedSource`), read-only, and list the rest for review. No
forced routing, no edit to accepted stops, no reset of pending work.

### F2 — P1: the carrier list is read several times a second while idle

Verified in code and live. `FleetSynchronizationOperation.RunJobAsync`,
called without a company, reads the active carriers
(`CompanyPasses.ForEachCompanyAsync` → `CompanyRoster.ActiveAsync`, an
uncached query) before it checks whether the job is due
(`FleetSynchronizationOperation.cs:253-268`). Its loops wake every 1 s
and 5 s (`FleetSynchronizationOperation.Feeds.cs:81,110,155,174`,
`...Planning.cs:164`). Live: 271.8 sequential scans a minute of a
two-row `Companies` table.

Proposal: check `NextRun` before reading the roster, or keep the roster
in ReadCache invalidated when a company changes. Expected effect about
270 fewer round trips a minute; measure with the same table-scan sample
and a query-count test.

### F3 — P1 (latent): HOS clocks are cached for all carriers under one key

Verified in code. `SamsaraDriverHosProvider` caches the clock table under
the fixed key `samsara:hos-clocks` with one process-wide lock
(`SamsaraDriverHosProvider.cs:15-16,32,82`), while Samsara credentials
are per company. With one carrier there is no exposure today; with a
second, one carrier's clocks, or an empty table, can be served to the
other for up to a minute. `RoutePreviewService.cs:30-35` fixed the same
pattern. Proposal: company in the key and one lock per company, with a
two-company test.

### F4 — P2: board planning summaries are read for a different page

Verified in code. `PlanningReadService.ReadBoardInputsAsync` asks for the
page with `IncludePlanned = false` (`PlanningReadService.cs:96-110`) and,
as reported, without `InChosenGroup`, while the screen shows
`includePlanned=true` and the dispatcher's groups
(`DispatchBoardRequest.cs:18`, `DispatchController.cs:123`). Page 1
usually agrees; later pages can name other trucks, whose summaries the
Client then drops (`DispatchList.razor.cs:733`), and the different
filters build a second fleet-wide board index. Not reproduced live.
Proposal: the Client sends the truck ids on screen (at most 12), and
the server resolves them through company-scoped planning inputs.

### F5 — P2: work is read two or three times per request or cycle

Messenger (verified, measured): cold 17 statements, warm 5, recorded as
debt at the release. Reported beside it:

- `GetDriverDutyStatus` in the same Messenger request reads the planning
  inputs again with HOS (`GetDriverDutyStatus.cs:63`,
  `PlanningSummaryReader.cs:84`).
- The summary worker reads each running truck's work fresh, cached, and
  fresh again every 30 s (`PlanningSummaryOperation.cs:170-186`), each
  read loading the whole `Trucks` table
  (`ExecutionWorkReader.cs:58-72`), for up to 128 trucks.
- The board ETA step reads the page's work afresh
  (`EtaChainInputsService.cs:94-150`) moments before the summaries read
  it through planning inputs.
- A board page view runs the board handler three times: the board and
  two enrichments, each re-reading execution loads and details
  (`GetDispatchBoardEnrichment.cs:21-29`,
  `DispatchList.Enrichment.cs:76-79`).
- The itinerary re-reads older loads with every stop
  (`TruckItineraryReader.cs:89-103`) already read in the same snapshot.

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

Reported, Client. Fleet Map posts the selected truck's planning every
10 s with no freshness check, inside the location poll
(`FleetMap.razor.cs:359-375,393,727-767`); the longest routes, over the
Client cache's geometry bound, come down in full each time
(`PlanningDisplayCache.cs:12,218-223,467-469`); the board's planning post
stays at 10 s while any truck on the page lacks a plan
(`DispatchList.razor.cs:678-687`); `next-routes` can poll every 10 s
while a leg stays pending (`FleetMap.NextLoads.cs:84-93`); switching
Cards, Table and Papers downloads the same board again
(`DispatchBoardRequest.cs:7,12-18`). Proposal: send the known plan
version on every planning read, back off the pending cadences, and key
the board by URL, not view.

### F7 — P2: road preparation queue loses backoff and starves prewarm

Reported. A next-load request's identity is a revision over all upcoming
loads, and a changed identity resets `Attempts` and `AvailableAt`
(`GetNextLoadRoutes.cs:188,204,265-279`, `SourceRoadStore.cs:107-124`),
so a failing load loses its backoff whenever a later load gets a road;
claims order by priority then time with 10 a minute for every carrier
(`SourceRoadStore.cs:160`, `RoutePreparationOptions.cs:8-11`). With F1,
this is the mechanism behind the historical starvation seen during the
recovery. Proposal: keep attempts per dispatch across identity changes
and give history its own share.

### F8 — P2: manual syncs are open to every signed-in user

Reported. `POST /api/dispatch/sync` and `POST /api/fleet/sync` need only
a signed-in user, the handlers check no role and no cooldown, and both
take the server-wide lock background sync uses
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

Reported. Truck positions are rewritten on every publish because
`ObservedAt` is set to now (`FleetSynchronizationOperation.Publication.cs:90,101`,
`TruckLocationStore.cs:70`); the base-route scan re-reads 100 loads and
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

## Proposed order, for root

1. F2 roster (smallest change, largest measured reduction).
2. F1 (a) and (b): reason and parking; then root decides (c).
3. F3 HOS key, before any second carrier.
4. F4 page identity for summaries.
5. F5 shared work read, in steps, each with a query-count test.
6. F6 and F7 cadence and queue identity.
7. F8, F9, then the P3 items.

Each fix: affected test groups during work, a before/after table-scan
sample for load claims, the full gate only for publication.

## Gaps

- No per-query production counts (`pg_stat_statements` absent) and no
  latency or payload measurement; costs outside the table sample are
  inferred from code.
- Reported findings were not re-read line by line.
- Not covered: Gmail watch and Drive OAuth flows, WhatsApp signature
  internals, migration bodies, git history for past secrets, Client
  provider traffic (tiles), and all tests' content.
