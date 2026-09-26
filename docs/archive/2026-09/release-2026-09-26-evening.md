# Release of September 26, evening (UTC)

The owner asked, through Root, for both workstreams to be finished,
integrated and published: the approved Fleet/Fuel cards and the one-road
map, and Codex's architecture corrections. Root cleared the exact
candidate `70a8ac984a57cf120c0ea037e7de29a1b0c36c61` after its own
review of the diff, the visual evidence and the gate.

**Status:** complete. API at 15:43 UTC, frontend at 15:47 UTC. No
migration; no message sent; no reset, replan or payment; Driver Pay
untouched; maximum instances unchanged.

## Package

Since the morning release (`3630bbdb`):
- `f75e85e4`: the approved Fleet and fuel cards of September 26;
- `acb35a4d`: a conversation's driver and clocks read once per request;
- `9a72f105`: the Messages reply box grows with its lines, up to four;
- `c1401bae`: finished loads are not drawn as a truck's next ones;
- `a65ab9ad`: Messages opens where the dispatcher left it;
- `ccb0838f`, `c029666d`: a truck's plan drawn as one road, empty miles
  orange, 20 px badges, stations shown by the plan's corridor;
- `0c394317`: merge of `codex/architecture-guardrails` through
  `646454a2` (`3cbe76b8`, `92934a19`, `c6304454`, `2287f0de`,
  `646454a2`): scoped reads, atomic messaging ownership, the module
  cycle, the messaging signal fixes and the audit conclusion, see
  [the correction record](architecture-guardrails-2026-09-26.md) and
  [the conclusion](architecture-audit-conclusion-2026-09-26.md);
- `70a8ac98`: the phone's map stage keeps half the screen and its
  panel 70% of it, so the fuel plan's actions stay in reach at 200%
  text.

The owner's uncommitted rule and retention documents (`AGENTS.md`,
`docs/README.md`, `docs/testing.md`, `docs/development/setup.md`,
`docs/development/artifact-retention.md`) are not part of the candidate.

## Gate

`PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on `70a8ac98`, exit 0:
Server 3,773, Client 1,240, the JavaScript suites, the offline UI smoke
(`browser-ui-SHl6Of`, 12 cases, no errors or unexpected requests) and
the two-tab messaging probe (`browser-messaging-tabs-9bi3d6`); verified
artifact `release-IFYg39`. The earlier gate on the merge alone
(`release-7ScjAC`) is superseded.

Visual evidence, pinned: the fleet design matrix
`browser-fleet-design-YsMLye` (1440, 1024, 390 and dark without a
failure; its 390-text200 case superseded by `browser-fleet-design-dBqFET`)
and `browser-fleet-design-6oPs1J` (390 on `70a8ac98`); the map on the
provider's real basemap `browser-map-markers-lvdvJa` and `-M5qNtQ`;
`browser-map-markers-0iyIEP` and `browser-stop-cards-7PEL4R`.

## Backup

`local-backups/pulsartms-release-backup.YKbzKR/before-2026-09-26-evening-release-70a8ac98.dump`,
taken 15:33 UTC: `pg_dump` 18.6 custom format with `--no-owner
--no-privileges`, 29,589,771 bytes, SHA-256
`81750b1b991f194a24880180f7e48c76f7ab9a81e7bad6b75764e503ca3576be`,
720 entries, 104 table data; in `local-backups/inventory.json`. No
restore rehearsed. No migration in this release (still 73).

Protected data before, read only: migrations 73, users 3, conversations
5, conversation messages 55, driver messages 0, broadcasts 0, fuel visit
sends 0, stored credentials 2.

## API

- **Before:** `amftms-api-b-f169545d-dcb5-49bd-9419-855efa4c5273`
  (`3630bbdb`), generation 258, 100%.
- **Build:** Cloud Build `3ce9aeb4-32b8-458b-8929-ab73463b2bd5` from a
  clean worktree of `70a8ac98` (15:34-15:43 UTC); image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:6def74df05173ddd19097051ec54345b0f827e2fc594fd2e28fd5daea78f8120`.
- **After:** generation 260, spec and status traffic 100% on
  `amftms-api-b-3ce9aeb4-32b8-458b-8929-ab73463b2bd5`; Ready,
  Active, ContainerHealthy and MinInstancesProvisioned true; 1 GiB,
  minimum 1 and maximum 1 instance, unchanged. The previous revision
  `…f169545d…` reports Active false and TrafficShutDown true: its
  workers are drained.
- **Health:** `/api/health/live` 200 on the service and through
  `tms.amfcarrier.com`.

## Frontend

`deploy-client.sh` from the same clean worktree, with the local
`Client/wwwroot/appsettings.json` copied in: its own gate passed
(JavaScript 662, Client 1,240, Server 3,773; UI smoke not requested on
this run, it ran on `release-IFYg39` for the same revision), verified
artifact `release-TWlF7i` (copied to `artifacts/managed/release-TWlF7i`
before the worktree was removed), 285 files published to Hosting.

Served from `tms.amfcarrier.com` after publication: `index.html` and
`css/main.css?v=9650742ab694a5d3` match the artifact's SHA-256;
`appsettings.json` is byte-identical and parses as JSON with a 39
character Maps key; the fingerprinted `dotnet.*.js`, `dotnet.native.*.wasm`,
`Client.*.wasm` and `blazor.webassembly.*.js` match; entry HTML answers
`Cache-Control: no-cache`.

Protected data after, read only: unchanged (migrations 73, users 3,
conversations 5, conversation messages 55, driver messages 0,
broadcasts 0, fuel visit sends 0, stored credentials 2). The new
revision's first 30 minutes show no error-level log entry and no 5xx.

## Frontend follow-ups the same evening

The owner reviewed the published map and card and asked for changes,
published without the design matrix at their request ("deploy without
checks; check later"); each ran `deploy-client.sh`'s own gate (Server
3,773, Client 1,240-1,242, JavaScript 662-663) from a clean worktree and
was checked live the same way (entry HTML, stylesheet and settings
against the artifact). The API is unchanged.

- `14bd23c2` -> `release-3lkCvr`: a colour per upcoming load again on the
  roads and badges, 28 px badges, every truck-card row split at one
  place (`--truck-card-columns`).
- `c7295704` -> `release-7gdpP0`: the planned fuel stop a size smaller.
- `3e39fdd3` -> `release-eCm7iW` (with `b0409300`): upcoming roads at
  full strength, a standing truck's ring 36 px, the fuel on arrival at
  the facts' size.
- `bd398acc` -> `release-ig8H20`, 16:23 UTC: the load's stops listed
  in the fuel plan between the fuel stops; the fuel booked to the next
  loads hidden with the Next loads layer (the toggle used to hide
  nothing); where the truck is now beside the next stop.

Local renders, pinned: `browser-fleet-design-iOd4xT` and `-sVWlox`
(card and fuel plan), `browser-map-markers-XCDDoT` (the map on the real
basemap with the colours back).
- `530f8e6c` -> `release-dccWfF`, 16:47 UTC (with `c9422d85`): the
  cycle remaining and the fuel on arrival off the truck card, the actions
  at the foot of the stop row's facts column; planning review notices by
  kind on the client (`PlanningNotice`), shown on the map as
  "Load N · review in Dispatch" and in full on Dispatch. The gate on
  `c9422d85` alone failed two preview-cache tests (record equality of the
  notice list) and published nothing; `530f8e6c` corrected it.

- `96f9177a` -> `release-dil1Yw`, 17:03 UTC: what is left of the load
  on the NEXT STOP line, the actions in a grid of equal cells, upcoming
  roads at 0.75 (the owner wanted them quieter, not louder), and the
  station corridor removed - every station is drawn whatever truck is
  picked. Local renders `browser-fleet-design-BzMpIt` and
  `browser-map-markers-o8BARy`.

## API, second release (16:51-16:59 UTC)

The owner authorized it ("so fix it") for the review notices by kind
(`e6531949`, code in `c9422d85`). Backup first:
`local-backups/pulsartms-release-backup.RhA2N9/before-2026-09-26-late-api-e6531949.dump`,
29,585,152 bytes, SHA-256
`340dfccfdba044c55b7f9b75b66c1106c8f44e8a3ab0645fd5a6e0fd109dfe67`,
720 entries, 104 table data, in the inventory; no migration (still 73).
Cloud Build `c59a3784-a92c-400d-9989-8648631ab2e2` from a clean
worktree; image
`us-east4-docker.pkg.dev/amftms/amftms/api@sha256:a6c08f2d3b809b07addf84377c4baa5289eb1440853c89467c90d793d35a8766`;
revision `amftms-api-b-c59a3784-a92c-400d-9989-8648631ab2e2`, generation
262, 100% traffic, Ready and Active; the previous `…3ce9aeb4…` Active
false and TrafficShutDown true. `/api/health/live` 200 on the service and
through Hosting; no error-level entry or 5xx in the new revision's first
minutes; protected data unchanged (73/3/5/55/0/2). The frontend that
reads the notices (`530f8e6c`) was already published.

## Not done

Cross-instance messaging notifications and the broader module
decoupling remain unimplemented, as the audit conclusion records. No
restore was rehearsed. Production timing of the shared reads was not
measured.
