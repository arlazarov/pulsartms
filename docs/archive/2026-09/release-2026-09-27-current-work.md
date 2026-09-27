# Current-work release — September 27, 2026 (UTC)

The owner authorized publication; Root reviewed the stage 4e commits and
their evidence and asked for the final gate, the maintained browser
checks, a fresh backup, the migration before the API, exact identities
and hosted hashes. The candidate is recorded in
[current-work-candidate-2026-09-27.md](current-work-candidate-2026-09-27.md).

**Status:** API at 18:44 UTC, frontend at 18:52 UTC. One additive
migration, applied explicitly before the API took traffic. No message
sent; no reset, replan or payment; Driver Pay untouched; memory (1 GiB)
and instances (minimum 1, maximum 1) unchanged.

## Package

`0e6add5dc6ad84a169f202443592fa2132c471e5`: stages 1 to 4e of
[current-work.md](../../architecture/current-work.md) over `bb5e46f6`,
containing `main` at `66f8801e`. The last commit only gives the offline
UI smoke's board fixture the server's `workPhase` (below).

## Gate

- `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on `fb8bb96d` failed:
  .NET passed (Server 3,875, Client 1,300), but the offline UI smoke
  missed the current and upcoming Dispatch cards at every width. Since
  stage 3a a card is current or next by the server's `workPhase`, which
  the board always sends and the smoke's fixture did not. `0e6add5d`
  added it to the fixture; no product code changed and no check was
  relaxed. The failed run is kept (`release-i4Ooo3`, `FAILED-GATE.txt`).
- The same gate on `0e6add5d`, exit 0: Server 3,875, Client 1,300, the
  JavaScript suites, the UI smoke (`browser-ui-qNBGNJ`) and the two-tab
  messaging probe (`browser-messaging-tabs-qhDZvp`); artifact
  `release-GqjYmS`. PostgreSQL fixture tests ran in it.
- `deploy-client.sh` ran the gate again on the clean worktree: Server
  3,875, Client 1,300, JavaScript 669, UI smoke `browser-ui-RFqki4`,
  messaging `browser-messaging-tabs-c7ZOKu`; artifact `release-yliuKc`.

## Navigation and memory, before release

A bounded observation, not a gate: the staged artifact with synthetic
APIs and a stub map, Dispatch, Fleet Map, Settings and Users in a loop,
a forced GC and a sample after each of 120 cycles (480 page visits).

| Build | JS heap, first to last | WASM memory | JS listeners |
| --- | --- | --- | --- |
| Candidate `release-GqjYmS` | 4.3 to 6.1 MiB, slowing | 66 MiB flat | 23 flat |
| Published before, `release-iCH61Y` | 4.3 to 6.2 MiB, slowing | 66 to 79 MiB | 23 flat |

The JS heap trend is the same before and after the branch. Not covered:
Messenger, Papers and Table with data (the fixture has no messaging
endpoints), the live map, and production browsers. Runs
`diagnostic-oyEy5d` (40 cycles) and the two 120-cycle runs next to it;
the script is kept in each run.

## Backup

`local-backups/pulsartms-release-backup.wl1TQN/before-2026-09-27-release-0e6add5d.dump`,
18:34-18:35 UTC: `pg_dump` 18.6 custom, `--no-owner --no-privileges`,
48,857,766 bytes, SHA-256
`ce1ae8b279e7e9f02be61852d0cb024b65d509dd89b6d897f07d20a263d74194`,
720 entries, 104 table data; in `local-backups/inventory.json`. No
restore rehearsed.

Protected data before, read only: migrations 73, users 3, conversations
5, conversation messages 57, driver messages 0, fuel visit sends 0,
saved ETA forecasts 29, dispatches 418, open legs 14.

## Migration before the API

`20260927172006_RecordEtaForecastWork` adds nullable `WorkKey` and
`RouteKey` (varchar 64) to `DispatchEtaForecasts`. Production applies
pending migrations when an instance starts, but a revision deployed
without traffic reported Ready while not running (`Active` false), so
nothing had been applied. Rather than let the first instance on full
traffic apply it, the EF idempotent script for exactly this migration
was applied in one transaction (lock timeout 5 s) at 18:44:28, while
the previous revision served: 74 migrations, both columns present and
nullable. The previous binary does not name the columns. The new
instance then logged "No migrations were applied. The database is
already up to date."

## API

- **Before:** generation 280, 100% on
  `amftms-api-b-9fb1edab-02d4-4492-b969-8842cd2faa13`
  (`sha256:2707eee5…46b7b63`), scaling automatic, 1 GiB, maximum 1.
- **Build:** Cloud Build `13babfb4-d0f4-4895-a641-d46360b95909` from a
  clean worktree of `0e6add5d` (18:35-18:42), status SUCCESS, image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:83b3caee24a3421a74ca5eea5f10b6e2f97a54a7e82e0244b7bae28d621fa536`.
- **Deploy:** the steps of `deploy-server.sh`, with the migration
  between them: revision `amftms-api-b-13babfb4-d0f4-4895-a641-d46360b95909`
  without traffic (same flags), its identity and digest verified by
  `scripts/cloud-run-release.mjs revision`; traffic moved at 18:44:36
  and verified by `cloud-run-release.mjs traffic`.
- **After:** generation 282, observed 282, 100% on the new revision and
  digest; Ready, Active, ContainerHealthy, MinInstancesProvisioned true.
- **Drain:** the previous revision logged "Shutting down user disabled
  instance" at 18:44:55 and nothing after; `Active` false,
  `TrafficShutDown` true. It remains the rollback target: the migration
  only adds columns it does not use.
- **Health:** `/api/health/live` 200 on the service and through
  `tms.amfcarrier.com`; `/api/health/ready` 401 without sign-in. No
  error and no 5xx on the new revision; the three warnings are 401s (the
  ready probe above and a browser's session refresh after the frontend
  release). The `libgssapi_krb5` start line is known.

## Frontend

`PULSARTMS_RELEASE_UI=1 bash deploy-client.sh` from the same clean
worktree with the local `Client/wwwroot/appsettings.json` copied in (no
setting of the example missing), 18:45-18:52 UTC: 285 files to Hosting.
Artifact `release-yliuKc`, copied with its UI evidence to the main
checkout's `artifacts/managed` and pinned.

Served by `tms.amfcarrier.com`, SHA-256 against the artifact, all
matching: `index.html`; `css/main.css?v=7d647c66a2f55ccc`;
`appsettings.json` (valid JSON, a 39-character Maps key);
`_framework/blazor.webassembly.w3qd1tpl0e.js`,
`_framework/dotnet.716bqp9hym.js`,
`_framework/dotnet.native.z7sw92kwzx.wasm`,
`_framework/Client.h621cqyhfq.wasm`. Entry HTML `no-cache`;
fingerprinted files `public, max-age=31536000, immutable`.

## After

- Protected data, read only: unchanged except migrations 74.
- Saved forecasts: the previous revision wrote none with keys. The new
  one, starting with empty memory, read every active truck's summary as
  no forecast at 18:44:49-51 and as shown between 18:45:00 and 18:45:31,
  as each truck's first committed refresh wrote its keys: the keyless
  window measured under 45 seconds for the 4 active trucks.
- Container memory (p99 of 1 GiB): the previous revision held 67-69%;
  the new one rose from 37% at 18:46 to 61% at 18:52 while warming, then
  held 60-65% to 19:03. No error and no 5xx to 19:03. Twenty minutes
  are not a long-run bound; the daily trend is still to be read.

## Server memory to 21:06 UTC

Read only from Cloud Monitoring and the revision's logs, 5-minute
alignment, no load injected and no limit changed; run
`diagnostic-OG0AUp` (`memory.txt`, `logs.txt`, `5xx.txt`), copied to the
main checkout's `artifacts/managed`. This is the deployed revision
`amftms-api-b-13babfb4-…` (1 GiB, one instance). The ETA count bound
(`3063cf47` and after, on this branch) and the audit branch are not
deployed: their tests measure nothing here, and nothing here measures
them.

- Container memory, p99 of the limit: 37% at 18:46, 57% at 18:51, 62-66%
  from 18:56 to 19:11 while warming; then a plateau of 63-65% from 19:16
  to 20:41 (p50 62.2-63.6%), 65-67% from 20:46 to 21:06 (p50
  64.5-65.5%). Peak p99 67% at 20:46. A step of about 2 points at 20:46,
  not a steady slope; two hours are still not a long-run bound, and the
  daily trend remains to be read.
- The previous revision held 67.5-68% (p99 69%) before the release.
- Instances: one active and none idle in every interval. One start
  (18:44:39, minimum instances), no restart since; no memory-limit, OOM
  or termination line in the revision's logs.
- 5xx: none from failures. Twelve 503s from `GET /api/messaging/changes`
  in bursts at 20:35, 20:49 and 20:52, answered in about 45 ms: the
  mailbox bounds of `MessagingMailboxes` (per account, waiting requests)
  refusing rather than growing, as designed; the Client backs off and
  polls. The bounds shipped before this release, and no 5xx at all is
  logged from September 24 to the release. Which bound refused is not
  logged. Owner: Messaging.

## Open, with owners

- ETA memory has no count bound (pre-existing; `eta-current`
  unmeasured). Owner: the ETA module.
- Auditor rules CW1-CW4 of current-work.md are not implemented. Owner:
  the current-work design.
- Loads 1403 and 1385: source closed, execution leg active. Recovery:
  Root.
- The Client does not name a stale dependency beyond the duty message.
- Not verified after release: a duty change showing its message, and
  the Completed tab against its owner, both need a signed-in read.
