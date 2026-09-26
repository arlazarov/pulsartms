# Overnight work of September 26

The owner asked for overnight work: finish the messaging latency fix,
review the changed Main code and then the whole application, fix
concrete defects with evidence, and keep this record. Nothing here was
deployed; a Main release waits for Root's review of a concrete candidate
and the owner's go. No production write, replan, message, reset or
secret was made or printed, except the WhatsApp callback restore the
owner authorized (recorded in `release-2026-09-26-night.md`).

Deployed at the start: API `257aeaf1` (revision `d7a80320`), frontend
`eeab5a34` (Hosting version `77a0ab003a21c5d9`).

## Implemented

Each item has a regression that fails without the fix unless marked.

| Commit | What | Evidence |
|---|---|---|
| `6ae6bd0e`, `b070e218`, `52038a20`, `18232d06` | Messaging signals by long poll instead of a server-sent stream | Firebase Hosting buffers streamed Cloud Run responses; an inbound stored 01:37:56.6 was read by the 30 s poll at 01:38:18.3. Mailboxes under one lock, per-account (4) and held-wait (40) caps, the wait not logged as slow, backoff that grows, timer-handshake test |
| `d7a8aeea` | The reason an ETA forecast is taken back, logged and counted | 11006 (leg `2f2cc129`) taken back every few minutes; the reason was not recorded |
| `2e8d63e2` | **Policy change, needs a decision; reverted on `main` by `54c186af` and kept on branch `fuel/first-stop-reserve-policy`:** keep the reserve to the first fuel stop whenever a plan can | 11007: first stop at mile 616 reached with 5.6 gal from 97.5; LOVES #820 at mile 279 kept the reserve for about $7 |
| `b4a332ba` | Fleet route preview cache keyed by company | Constant key over a process-wide cache: code permits one carrier's preview to be served to another for 30 s. No observed leak is claimed |
| `325c6cc2` | Speed icon bands restored on the truck card; unknown speed not shown as normal | `05490091` removed the rules; before/after screenshots in `artifacts/managed/diagnostic-E7Rop4` |
| `327252b9` | Station status sweep reads prices inside each carrier's pass | Production: no station checked since 2026-09-21 16:50 UTC |
| `b044021e` | Every carrier gets its turn when one carrier's pass fails | Code |
| `50795128` | Gmail maintenance failures logged per carrier | Code; no test (concrete lifecycle class) |
| `c732e490` | Fleet and load import memory keys name the carrier | Code; no production effect with one fleet provider |
| `c3ab25ea`, `d8f5aab9` | An account counts only for an existing, active carrier (sign-in, refresh, session) | Code; production: all 3 users' carriers exist and are active |
| `8aa1723e` | An ETA refresh publishes only after its save commits; a failed save publishes nothing and retires the shown forecast only when inputs changed (and only if it is still the one it found); a late earlier result of the same road version never replaces a later one | 11006: published before save, taken back on failure. Two of four new interleaving tests fail on the old code |
| `f9cfdd92` | Root's review of `8aa1723e`: the shown forecast is retired only when a check demonstrates a changed dependency (`RoutePlanningException.DependencyChanged`) or the chain is described otherwise; generic failures and refusals leave it. Equal `CalculatedAt` keeps the published result, as the store does | Generic-failure, refusal and equal-time tests fail on `8aa1723e` |

## Reported, not changed

- **11007 route.** The saved road (1,228.4 mi, continuous, US only) leaves
  I-90 at Erie (mile ~450), runs about 130 miles south on I-79 to near
  Cranberry, PA (40.68 N, mile 578.7), then back north-west on the
  turnpikes past North Lima to near Cleveland (mile ~650): about 200
  miles where I-90 takes about 110. The request is TomTom
  `routeType=fastest&traffic=true&avoid=borderCrossings`; the cause
  (traffic at calculation time, a truck restriction, or the border
  option) is not proven. Proving it needs one provider request, which
  was not made.
- **11007 prices.** Quotes are daily. #504's first-stop price is its
  arrival-date quote (September 26, $5.624). #432's arrival is
  September 28 17:42 CDT, which has no quote, so the plan used the
  pricing date's quote (September 25, $5.639) and marked it estimated;
  a September 26 quote ($5.396) exists. The plan therefore compares
  quotes of different dates. Using the latest published quote before
  arrival would move about 99 gal from #504 to #432 (about $22.6); that
  is a pricing-policy decision, not made here.
- **11006 ETA.** Fixed in `8aa1723e` for passing failures (planning
  busy, cancelled). If its saves fail because its inputs change between
  two descriptions, the shown forecast is still retired, correctly; the
  reason is logged since `d7a8aeea` and will show which after release.
- Audit findings not fixed tonight: map ETA looked up under a non-root
  load; planning settings row with a fixed id (a second carrier cannot
  save, needs a migration); board planning summaries requested without
  the driver-group filter; three "load completed" rules; the Client's own
  Current/Next rule; rate limits keyed by the proxy address (unverified);
  expected webhook write conflicts logged as errors; repeated user and
  credential reads per request.

- **Refuted: "a summary signed with fresh work but calculated from cached
  work".** `PlanningSummaryOperation` does sign with inputs read fresh and
  calculate through the read cache's copy, but
  `PlanningSummaryCache.Complete` stores a result only under the signature
  the reader asked with, and the reader signs with the same cached copy.
  While the copy lags, nothing is stored and the reader sees "updating";
  once it catches up, the copy matches the fresh inputs in everything the
  signature covers. Older work cannot be shown under a newer signature.
  Pinned by `PlanningSummaryCacheTests.AResultSignedWithFresherWorkWaitsForTheReaderToAskForIt`;
  no code changed. A full integration reproduction was attempted on the
  server-composed SQLite harness and stalled (one shared in-memory
  connection under the background loop), so it was not kept. The
  remaining cost - a recalculation that cannot be stored while the copy
  lags, and one cached read per refresh - was not measured.

## Audit coverage

Three read-only audits (Dispatch/Execution/Fleet; Routing/ETA/Fuel;
Messaging/Integrations/tenancy) plus a review of `257aeaf1..b070e218`.
Not covered: Mileage, Border, Documents upload, the Client beyond the
pages named, Infrastructure stores other than those named, migrations,
and anything measured in production beyond the queries above.

## Candidate and checks

- **Verified candidate `ec5662c5`** (includes the policy change
  `2e8d63e2`, so Root has not cleared it): `PULSARTMS_RELEASE_UI=1 bash
  verify-release.sh` on a clean worktree passed in 356 s. The artifact
  is kept at `artifacts/managed/candidate-ec5662c5-release-JCPeOc` with
  `.keep`, `verify.log` and `manifest.sha256` (285 files; manifest
  SHA-256 `e7d525d7427ada73aad77ef1f523547ff6d7a227989d838d13f1a7e438005b39`;
  `index.html` `7544e13b…`, `css/main.css` `920af594…`,
  `appsettings.json` `ca2a6e6c…`).
- **After the candidate:** `8aa1723e` and `d8f5aab9` passed their
  affected categories (Eta, Routing, Dispatch, Architecture 1,883;
  Identity, Architecture 274). A new candidate needs a new gate once
  Root decides whether `2e8d63e2` stays.

| Stage of the candidate gate | Seconds |
|---|---|
| npm ci, format, JavaScript tests (656) | 10 |
| .NET Release build | 9 |
| Client tests (1,225) | 7 |
| Server tests (3,737) | 174 |
| Client publish | 23 |
| Offline UI smoke (all widths, themes, text sizes) | 109 |
| Messaging tabs smoke | 11 |
| Total | 356 |

Server tests are half the gate. A release today also runs them in Cloud
Build for the API and again in `deploy-client.sh`, which cannot deploy
an already verified artifact; letting it deploy a verified, hashed
artifact of the same commit would remove one full run per release. Not
changed tonight (release tooling).

## Test database isolation

The PostgreSQL tests use database `pulsr_core_fixture_…` as role
`pulsr_test_runner` (neither is the application database; the role has
no access to its tables), one schema `t_<time>_<guid>` per run, dropped
afterwards. Advisory locks are per database, so two worktrees running
PostgreSQL tests at the same moment can wait on each other. At the
check after this work (read-only): no run schemas, no other session, no
advisory lock.
