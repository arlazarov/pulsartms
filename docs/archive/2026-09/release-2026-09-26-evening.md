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

- `c0c55b3e` -> `release-vZssV6`, 17:31 UTC (code in `761c7b4c` and
  `c52b1ea5`): the plan's fuel stops drawn with the station layer off;
  the phone card open whole with no Details button, at most half the
  stage, scrolling; the gap under Current location closed; upcoming
  roads at a layer opacity of 0.45 (deck.gl raises it to 1/2.2, so the
  0.75 before was drawn at 0.88 and read as no step); a parked truck
  ringed into its own next stop only, never the nearest badge. The first
  gate, on `c52b1ea5`, failed the English-only documentation check on a
  Cyrillic quotation in this record and published nothing; `c0c55b3e`
  translated it. Gate: Server 3,773, Client 1,242, JavaScript 656; 285
  files; entry HTML, stylesheet (`v=0fb6df0f45123f1c`), settings and the
  fingerprinted framework files match the artifact. Local renders
  `browser-fleet-design-Sf09as` (390), `browser-map-markers-M4nguA` (real
  basemap) and the markers probe `browser-map-markers-31G5Ih`.

- `18113d1c` -> `release-EPd1dz`, 18:04 UTC (with `6a274a45`): the
  Details button back on wide cards only (a phone's card stays open
  whole, half the stage); what is left of the load over where the truck
  is; the facility name one line, cut by an ellipsis; the planning notice
  chip gone from the map; upcoming roads at 0.3; the layer chips named
  and coloured with the search capped at 28rem and Groups outlined; a
  closed card hides the duty and rest line. Gate: Server 3,773, Client
  1,242, JavaScript 656; 285 files; entry HTML, stylesheet
  (`v=54f49d941461948b`), settings and the fingerprinted framework files
  match the artifact. Local renders `browser-fleet-design-ghpFGq`,
  `-wGDjrU`, `-DCCuno`, `-oCNXg7` and `browser-map-markers-xtuxuW`.

- `46193678` -> `release-32VEGI`, 18:20 UTC (with `b4561e38`): what is
  left of the load on one line, and a fuel reading over 30% in the
  card's own colour rather than green. Gate: Server 3,773, Client 1,242,
  JavaScript 656; 285 files; entry HTML, stylesheet
  (`v=3497ce74fd4ffabd`), settings and the fingerprinted framework files
  match the artifact.

- `9c51711b` -> `release-vAP9ED`, 18:39 UTC: the fuel plan edited in
  its own card - one list in the plan card's place, the chosen stop
  opened under its line, no gauges, no phone tabs, back to the plan card
  on save, reset or cancel - and the driver filter behind Filters on a
  phone. Gate: Server 3,773, Client 1,243, JavaScript 655; 285 files;
  entry HTML, stylesheet (`v=b069f26b712e8e41`), settings and the
  fingerprinted framework files match the artifact. Local renders
  `browser-fleet-design-sNknSP` (1440) and `-PHSqRK` (390), each with a
  `fuel-editor` case. `fuelEditorSmoke.mjs` (not gated) still describes
  the retired two-column editor.

- `e76b377a` -> `release-b0Tku4`, 19:01 UTC (with `1e66face`): a
  picked truck, stop or station brought out from under the card that
  opens for it; "Cycle short by 1h 20m"; a booking over two days on two
  lines; the vehicle line's four readings in four equal cells; the next
  recap off the map's stop cards. Gate: Server 3,773, Client 1,244,
  JavaScript 659; 285 files; entry HTML, stylesheet
  (`v=38c551e7e2374905`), settings and the fingerprinted framework files
  match the artifact. Local render `browser-fleet-design-ezVoQC`; the
  reveal is covered by unit tests (the design probe stubs the map).

- `d205a1b5` -> `release-PRWGmo`, 19:14 UTC: the reveal kept up while
  a card settles (grows, or is replaced by one of another size), measured
  once at the pick; stop, next-stop and station cards at least
  `map-stop-inspector-height` tall. Gate: Server 3,773, Client 1,244,
  JavaScript 661; 285 files; entry HTML, stylesheet
  (`v=9f1acb1fd276511c`), settings and the fingerprinted framework files
  match the artifact. Local render `browser-fleet-design-z8Mu4T`.

- `283ec891` -> `release-pZ5iF7`, 19:58 UTC (with `e76ad220` and
  `a3a7d2ad`): a stop or station opened again is revealed again, and
  the pick is measured at every rest, so a route fitted or a zoom
  meanwhile is taken into account; the Open load button off the truck
  card; the next-load stop card numbered as its badge, naming its crew
  and sized as the route stop card, and both saying "Cycle short by".
  Two earlier gates on this work published nothing: one (`e76ad220`)
  was stopped after 28 minutes in the Server tests, slowed by builds
  beside it; two more stopped at two JavaScript pins and at Prettier.
  Gate: Server 3,773, Client 1,244, JavaScript 662; 285 files; entry
  HTML, stylesheet (`v=75dff988178eeac2`), settings and the fingerprinted
  framework files match the artifact. Local renders
  `browser-fleet-design-C3BnYu` and `-6zwW4C`.

- `16322944` -> `release-Eheq3j`, 20:09 UTC (with `d87045a1`): the pick
  revealed again when its card changes shape, even after a drag; the
  stop's rows kept at the top beside a taller column; what is left of
  the load in the location's shape. Gate: Server 3,773, Client 1,244,
  JavaScript 663; 285 files; entry HTML, stylesheet
  (`v=93f98f01ebe26026`), settings and the fingerprinted framework files
  match the artifact. Local render `browser-fleet-design-MAauDp`.

## Frontend, fifteenth publication (23:03 UTC)

The owner confirmed publication (Root asked "publish to the server?",
the owner answered "yes"), and Root cleared candidate
`8c6c506f6f5e3668b5e0f63cdcc5d2e7c7aec3fc` on
`codex/dispatch-fleet-integration` after its own review: the Dispatch
cards read as the Fleet Map cards (`242ab81a`), the Fleet reading and
fuel-editor fixes (`c493ce23`), the Fleet disclosure and load-link
contrast, the enlarged-clock, load-placeholder and phone visit-grid
fixes the revived hours probe found, and that probe (`3fbd6a5d`). Root
checked `66f8801e..8c6c506f`: Client, tests, styles and documents only;
no server, contract, migration or deployment change.

Client only: the API, the database and Driver Pay were not touched; no
backup or reset was needed for a static, migration-free release.

`PULSARTMS_RELEASE_UI=1 bash deploy-client.sh` from a clean detached
worktree of the candidate, with the local `Client/wwwroot/appsettings.json`
copied in (22:35-23:03 UTC), exit 0. Its gate: JavaScript 663, Client
1,244, Server 3,773; offline UI smoke 12 cases with no failures, browser
errors or unexpected requests (`browser-ui-Tu553J`); messaging tabs
smoke without errors. Verified artifact `release-Ul6JiU`, retained in
the main checkout's `artifacts/managed/release-Ul6JiU` (pinned) with the
gate log and both smoke reports under `gate/`; 285 files. PostgreSQL
execution checks were not run (no suitable isolated fixture is wired to
the gate, and nothing here changes storage).

Hosting release `sites/amftms/releases/1790463827610000`, version
`sites/amftms/versions/ec5a6335623746b0`, 23:03:47 UTC, finalized. The
version it replaced, for rollback, is `601a28e3431f5221` (release
`1790453374943000`, 20:09 UTC, the fourteenth publication).

Served from `tms.amfcarrier.com` after publication, each byte-identical
to the artifact (SHA-256):
- `index.html` `a905d4466498d7d92e4ea628b9a1234fcb272e57326d2423b97e751d620bea39`;
- `css/main.css?v=0106181897024f2f`
  `0106181897024f2f77e8cacc5060544bb0fbd96a21da6a7ab16b82c5ae86c930`
  (SHA-1 `575b876238e238507e2cf6b7cfcb24c0e7433695`, the stylesheet Root
  verified in its staging);
- `appsettings.json` `ca2a6e6c74ce65676cdb76b87f91253e0c4879979d96b9a806fb507057b1380e`,
  parses as JSON with a 39 character Maps key;
- `_framework/Client.5c89t2hl5f.wasm`, `blazor.webassembly.w3qd1tpl0e.js`,
  `dotnet.native.z7sw92kwzx.wasm` and `dotnet.v2nmre6qp6.js`.

Entry HTML, stylesheet and settings answer `Cache-Control: no-cache`;
the fingerprinted framework files `public, max-age=31536000, immutable`.
`/api/health/live` answers `Healthy`. Before publication the site served
`v=93f98f01ebe26026`, the fourteenth publication, as recorded above.

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

## API and frontend, night release (23:49-00:02 UTC)

The owner asked, through Root, to publish Root's Dispatch corrections and
the Papers fix together ("put it on the server"). Candidate
`64c8ebfc6d6b23e973c0d59c2c2ac1e127540ed5`: Root's
`codex/dispatch-fleet-integration` at `48eb706a` (`3557cf1c` Dispatch
truck information always open; `48eb706a` the summary bound to the
visible truck, leg and assignment rather than the first row, Fleet
telemetry wrap, the station day grid; see
[the record](dispatch-summary-and-card-wrap-2026-09-26.md)), this
record's fifteenth publication section (`0c5de609`), and `64c8ebfc`
(from `d302c989`): the board reads an accepted load's empty miles by its
leg, so Cards, Table and Papers show total and Total RPM, and Papers
names the trailer. Root reviewed the Papers diff. No migration (73, the
latest `20260925190516_RecordBaseRoadBorderCheck`, in the database and
the candidate).

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on
  `64c8ebfc`, exit 0: Server 3,773, Client 1,251, JavaScript 663, no
  failure or skip; the PostgreSQL tests ran on the recorded isolated
  development database. Offline UI smoke `browser-ui-e0U0UV` (12 cases,
  no failure, browser error or unexpected request) and
  `browser-messaging-tabs-Dec7iE`; verified artifact `release-i7KlEz`
  with the raw log in its `evidence/`.
- **Backup** (23:48 UTC):
  `local-backups/pulsartms-release-backup.AzEK1i/before-2026-09-26-night-release-64c8ebfc.dump`,
  `pg_dump` 18.6 custom format, 30,798,740 bytes, SHA-256
  `b29c6ca2e9e3109561969f6b81e0baaaea6fdbc5dc767ee863e4e1e1167f6604`,
  720 entries, 104 table data; in the inventory. No restore rehearsed.
- **API:** before, `amftms-api-b-c59a3784-a92c-400d-9989-8648631ab2e2`
  (`e6531949`), generation 262. Cloud Build
  `c073d3c1-2b69-4079-a641-bed83855636a` from a clean worktree; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:f67e61f2e972d32b1d271acfad028791bb16cbb0fac984b0ed0d380661a5c9b8`;
  revision `amftms-api-b-c073d3c1-2b69-4079-a641-bed83855636a`,
  generation 264, spec and status traffic 100%; Ready, Active,
  ContainerHealthy and MinInstancesProvisioned true; 1 GiB, minimum 1,
  maximum 1, scaling automatic, unchanged. The previous revision is
  Retired, Active false and TrafficShutDown true: drained. Health 200 on
  the service and through Hosting; no error-level entry or 5xx in the
  new revision's first minutes.
- **Frontend:** `deploy-client.sh` from the same worktree with the local
  `Client/wwwroot/appsettings.json` copied in; its own gate passed
  (Server 3,773, Client 1,251, JavaScript 663), artifact
  `release-kRy7jd`, 285 files. Served `index.html` (SHA-256
  `20c3a06f8b9937a55bfa6aba060508212a7ac56b7d3b0faa0d689a23021eae50`)
  and `css/main.css?v=9a48f1e1d21eb66e` match the artifact, as do
  `appsettings.json` (JSON, 39-character Maps key), `dotnet.*.js`,
  `dotnet.native.*.wasm`, `Client.*.wasm` and
  `blazor.webassembly.*.js`. Entry HTML and the stylesheet answer
  `no-cache`, fingerprinted framework files one-year `immutable`.
- **Protected data** before and after, read only: migrations 73, users
  3, conversations 5, conversation messages 55, driver messages 0,
  broadcasts 0, fuel visit sends 0, stored credentials 2.

The four artifacts are copied to the main checkout's
`artifacts/managed/` and pinned. The service descriptions kept as
evidence have their environment values redacted: `gcloud run services
describe` prints the service's secrets in plain text.

Not done: the completed list still reads empty miles by load alone, so
completed accepted loads show no total or Total RPM there. Load 1403
(truck 11005) still lacks its final completion confirmation; this
release does not repair it. No message sent; no reset, replan, payment
or Driver Pay change; maximum instances unchanged.

## API release of September 27 (00:50-00:59 UTC)

The owner approved, through Root, publishing the Torque invoice-status
correction and the completed-list mileage read. Candidate
`fc56a7359d2aecf3807efc9f9a591400a2bfe5f8`: Root's
`codex/dispatch-history-import` at `dcf26214`, based on the released
`64c8ebfc` (`a74a6b1b`, `8272aa41` the bounded history import tool and
its record; `41b5d3ef` Torque `sent` read as completed, see
[the record](torque-invoice-completion-2026-09-27.md); `dcf26214`
completed accepted loads read their mileage by leg, see
[the record](completed-native-financials-2026-09-27.md)), plus this
record's night section and its English quotation fix. No migration
(73), no DTO change, no Client source change against `64c8ebfc`.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on
  `fc56a735`, exit 0: Server 3,780, Client 1,251, JavaScript 663, no
  failure or skip (isolated development PostgreSQL). UI smoke
  `browser-ui-QkGj3S` (12 cases, clean) and
  `browser-messaging-tabs-OpqOmN`; artifact `release-XYv2Uq`, raw logs
  in its `evidence/`. The first gate, on `810b441a`, failed one check:
  this record quoted the owner in Cyrillic; that log is kept beside it.
- **Backup** (00:50 UTC):
  `local-backups/pulsartms-release-backup.2lmAQQ/before-2026-09-27-api-release-fc56a735.dump`,
  40,017,680 bytes, SHA-256
  `ad0b226b8672baa48da79529fdc3b9eb0f7fb79eb7a4c681ee0c5880bef6256a`,
  720 entries, 104 table data; in the inventory. No restore rehearsed.
- **API:** before, `amftms-api-b-c073d3c1-2b69-4079-a641-bed83855636a`
  (`64c8ebfc`), generation 264. Cloud Build
  `643f1ba7-66c8-4b70-ab87-850853fbf5f4`; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:e7898d741d8dbaf591ca4fe1ba058905d8e71ef268da6b945ac205176e6d5d79`;
  revision `amftms-api-b-643f1ba7-66c8-4b70-ab87-850853fbf5f4`,
  generation 266, spec and status traffic 100%; Ready, Active,
  ContainerHealthy and MinInstancesProvisioned true; 1 GiB, minimum 1,
  maximum 1, scaling automatic, unchanged. The previous revision is
  Retired, Active false and TrafficShutDown true: its workers, including
  the old Torque adapter, are drained. Health 200 on the service and
  through Hosting; no error-level entry or 5xx in the first minutes.
- **Frontend:** not republished. The Client source is identical to
  `64c8ebfc`, and Hosting still serves `release-kRy7jd` (`index.html`
  `20c3a06f8b9937a55bfa6aba060508212a7ac56b7d3b0faa0d689a23021eae50`,
  `css/main.css?v=9a48f1e1d21eb66e`), checked after the API release. A
  rebuild in another worktree gives a different `Client.*.wasm` and
  therefore `index.html`, not different behaviour.
- **Protected data** before and after, read-only counts: migrations 73,
  users 3, conversations 5, conversation messages 55, driver messages 0,
  broadcasts 0, fuel visit sends 0, stored credentials 2.

Root owns the remaining twelve Torque status reconciliations and the
bounded historical mileage preparation; neither was run here. Completed
loads with several sections keep an unknown total. No message sent; no
reset, replan, payment or Driver Pay change.

## API follow-up of September 27 (01:20-01:29 UTC)

Historical recovery found that background preparation skipped every
completed load, so a completed accepted leg never got its empty miles.
Root fixed the guard (`93fb6e19`, see
[the record](completed-native-preparation-2026-09-27.md)) and cleared
it for this release; only that commit was added. Candidate
`c331aa00530b3600beafd80fb3fdb84f977a5cc2` (the previous candidate, its
record, and `93fb6e19`). No migration (73), no DTO or Client change.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on
  `c331aa00`, exit 0: Server 3,783, Client 1,251, JavaScript 663, no
  failure or skip. UI smoke `browser-ui-c3qzjq` (12 cases, clean) and
  `browser-messaging-tabs-KAWrUr`; artifact `release-i5W4Lc`, raw log in
  its `evidence/`.
- **Backup** (01:20 UTC):
  `local-backups/pulsartms-release-backup.GRE0DF/before-2026-09-27-api-release-c331aa00.dump`,
  40,027,225 bytes, SHA-256
  `9e1d0c267dc7c2df5c793e053bcab1cd5e3f04c019d732f70e08f6be93dc775a`,
  720 entries, 104 table data; in the inventory. No restore rehearsed.
- **API:** before, `amftms-api-b-643f1ba7-66c8-4b70-ab87-850853fbf5f4`
  (`fc56a735`), generation 266. Cloud Build
  `ff6c836e-849b-487d-8a20-b452f7ae1146`; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:8dc8042d0bef92b3a0c0aa68efcd3d3ff71341e6e6a60732c1821f4111f4715b`;
  revision `amftms-api-b-ff6c836e-849b-487d-8a20-b452f7ae1146`,
  generation 268, spec and status traffic 100%; Ready, Active,
  ContainerHealthy and MinInstancesProvisioned true; 1 GiB, minimum 1,
  maximum 1, scaling automatic, unchanged. The previous revision is
  Retired, Active false and TrafficShutDown true: drained. Health 200 on
  the service and through Hosting; no error-level entry or 5xx in the
  first minutes. Hosting still serves `release-kRy7jd`.
- **Protected data** before and after, read-only counts: unchanged
  (73, 3, 5, 55, 0, 0, 0, 2).

The first deploy attempt stopped before any change: the gcloud login had
expired and the owner signed in again. Root alone resumes the historical
recovery; nothing was requeued or replanned here.

## API recovery release of September 27 (02:07-02:16 UTC)

Historical recovery found that a load's captured history differed when
it was read in a batch and when it was read alone: a completed leg that
one load in the batch needed was added to every load on the same truck,
so the recorded dependencies never matched and the requests churned.
Root's fix `392c9bb1` (see
[the record](historical-road-input-isolation-2026-09-27.md)) selects
completed legs per load, as a single read does; the database reads are
the same two batched queries. Reviewed here before release; only that
commit was added. Candidate
`25362c19560b94ee81c8372ea87fb0f79a69ca1e`. No migration (73), no DTO
or Client change.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on
  `25362c19`, exit 0: Server 3,783, Client 1,251, JavaScript 663, no
  failure or skip. UI smoke `browser-ui-tEcQuq` (12 cases, clean) and
  `browser-messaging-tabs-aoOt6t`; artifact `release-pqllMO`, raw log in
  its `evidence/`.
- **Backup** (02:07 UTC):
  `local-backups/pulsartms-release-backup.lPnzmP/before-2026-09-27-api-release-25362c19.dump`,
  40,053,636 bytes, SHA-256
  `3ed581c4d5d8cc20bff79cc54ef3ab790054dfe322127fc5ea8110ff28f1faeb`,
  720 entries, 104 table data; in the inventory. No restore rehearsed.
- **API:** before, `amftms-api-b-ff6c836e-849b-487d-8a20-b452f7ae1146`
  (`c331aa00`), generation 268. Cloud Build
  `7a7f4da3-2fab-4f9a-a470-1c352a4f7288`; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:dc5310d074b7487193fbd2630bdcf67814a4a91eeff9a87bd93e82228aa22f1b`;
  revision `amftms-api-b-7a7f4da3-2fab-4f9a-a470-1c352a4f7288`,
  generation 270, spec and status traffic 100%; Ready, Active,
  ContainerHealthy and MinInstancesProvisioned true; 1 GiB, minimum 1,
  maximum 1, scaling automatic, unchanged. The previous revision is
  Retired, Active false and TrafficShutDown true: drained. Health 200 on
  the service and through Hosting; no error-level entry or 5xx in the
  first minutes. Hosting still serves `release-kRy7jd`.
- **Protected data** before and after, read-only counts: unchanged
  (73, 3, 5, 55, 0, 0, 0, 2).

Root owns the recovery and its validation; a finished queue is not proof
that the historical totals are recovered. Nothing was requeued, reset or
replanned here.

## API release of September 27, completed reviews (02:43-02:51 UTC)

The owner asked for this fix to be published and the work finished.
AMF1309, completed in the source and never accepted into execution,
stood in the active list: a load whose actuals need review was admitted
before the completed and cancelled checks. Root's fix `4ac9871d` (see
[the record](completed-review-active-leak-2026-09-27.md)) moves that
flag into the shared relevance rule after the terminal and execution-leg
checks; the review warning stays on the load, and no assignment or
status changes. Reviewed here before release: unfinished imported loads
needing review still show; the other caller keeps its default. Only that
commit was added. Candidate
`a2dd5b1da6e4d90a9642c01a577e0d6f2a70d7a8`. No migration (73), no DTO
or Client change.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on
  `a2dd5b1d`, exit 0: Server 3,786, Client 1,251, JavaScript 663, no
  failure or skip. UI smoke `browser-ui-pWJerh` (12 cases, clean) and
  `browser-messaging-tabs-iiCODb`; artifact `release-E8E3VK`, raw log in
  its `evidence/`.
- **Backup** (02:43 UTC):
  `local-backups/pulsartms-release-backup.T6UFZI/before-2026-09-27-api-release-a2dd5b1d.dump`,
  41,865,020 bytes, SHA-256
  `050ec762a449c55eca0e231f75a6e2e698d5e77797206d57264636e2322f5ad2`,
  720 entries, 104 table data; in the inventory. No restore rehearsed.
- **API:** before, `amftms-api-b-7a7f4da3-2fab-4f9a-a470-1c352a4f7288`
  (`25362c19`), generation 270. Cloud Build
  `85ebbd68-d093-4c24-be6b-3465a2871d6f`; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:aa9d001114a4c80bcc77ed71fdf268889fd8a0ab44ade741674c2875680a81ae`;
  revision `amftms-api-b-85ebbd68-d093-4c24-be6b-3465a2871d6f`,
  generation 272, spec and status traffic 100%; Ready, Active,
  ContainerHealthy and MinInstancesProvisioned true; 1 GiB, minimum 1,
  maximum 1, scaling automatic, unchanged. The previous revision is
  Retired, Active false and TrafficShutDown true: drained. Health 200 on
  the service and through Hosting; no error-level entry or 5xx in the
  first minutes. Hosting still serves `release-kRy7jd`.
- **Protected data** before and after, read-only counts: unchanged
  (73, 3, 5, 55, 0, 0, 0, 2).

Root continues the financial recovery and its validation; nothing was
requeued, reset or replanned here.

## API and frontend of September 27, Dispatch views (04:25-10:29 UTC)

The owner authorized publishing the Dispatch view corrections, the
current load read from planning in Dispatch and Messenger, and the
money column; Root cleared the exact candidate
`79bd05172e97cf25133b28969e8cf71d54d0c6e8` after reviewing the diff,
the cost evidence and the gate. No migration (73), no DTO change.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on
  `79bd0517`, exit 0: Server 3,791, Client 1,251 + 4, JavaScript 665,
  no failure or skip. UI smoke `browser-ui-moFpZB` (12 cases, clean),
  `browser-messaging-tabs-2kguzB`; artifact `release-sQu6vd` with a
  SHA-256 manifest. The gate on `305e76d2` failed one Client test that
  looked for "1407" in the whole markup, where a random message ID held
  it (`release-lzqcLe`, log kept); `79bd0517` fixes that test only.
- **Backup** (04:25 UTC):
  `local-backups/pulsartms-release-backup.nnfc0V/before-2026-09-27-release-79bd0517.dump`,
  41,967,707 bytes, SHA-256
  `3bc7d53e973bd66a23fd109ff4dd383992c140bdd5dc19a4dee3d642e89305c8`,
  720 entries, 104 table data; in the inventory. No restore rehearsed.
- **API:** before, `amftms-api-b-85ebbd68-d093-4c24-be6b-3465a2871d6f`
  (`a2dd5b1d`), generation 272. Cloud Build
  `fd2d5f25-986e-44ef-9757-6bf84a67e1e3`; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:01b64098801d7a4ff44ae772f7ed6e17e9deaeaed0ceff35887d5f08b588d37a`;
  revision `amftms-api-b-fd2d5f25-986e-44ef-9757-6bf84a67e1e3`,
  generation 274, spec and status traffic 100%; Ready and Active; 1 GiB,
  minimum 1, maximum 1, scaling automatic, unchanged. The previous
  revision is Retired, Active false and TrafficShutDown true. Health 200
  on the service and through Hosting; no error-level entry or 5xx in
  the first minutes. The response contracts are unchanged, so the
  published frontend works against it.
- **Current work, live and read-only (04:34 UTC):** 11005 current 1410
  with 1403 passed at the same revision; 11006 current 1395 with 1385
  passed; 11007 and 54777 current on their first rows, nothing passed.
- **Frontend: not published.** `deploy-client.sh` passed its own gate
  (Server 3,791, Client 1,255; artifact `release-GTsl5s`, 285 files,
  pinned with a manifest) and then stopped: the Firebase login had
  expired. Hosting still serves `release-kRy7jd` (`index.html`
  `20c3a06f8b9937a55bfa6aba060508212a7ac56b7d3b0faa0d689a23021eae50`).
  After the owner signed in again, `release-GTsl5s` was published as
  tested at 10:29 UTC, without another gate: its 284-file manifest (the
  settings file aside) was identical to the pinned one, the hosted
  settings check passed, and the API was still
  `amftms-api-b-fd2d5f25-…` at generation 274. `firebase deploy` found
  285 files and completed. Live: `index.html`
  (`5b52effcd6bf3b29d7b32471d14d6e564df716e4ac1a7f51c00489e8beea0497`),
  `css/main.css?v=c100ad13d28d4c91`, `appsettings.json` (JSON, 39-character
  Maps key) and `dotnet.*.js`, `dotnet.native.*.wasm`, `Client.*.wasm`,
  `blazor.webassembly.*.js` match the artifact; entry HTML, stylesheet
  and settings answer `no-cache`, fingerprinted framework files one-year
  `immutable`; health 200. No error-level entry or 5xx on the API
  revision in its first six hours. Protected data after, read-only
  counts: unchanged (73, 3, 5, 55, 0, 0, 0, 2). The evidence, including
  the Firebase log, is in the main checkout's
  `artifacts/managed/release-GTsl5s/evidence`.
- **Known cost:** Messenger's driver work reads planning inputs; warm 5
  statements (as before), cold 17 (DriverWorkCostTests). Recorded as
  debt for the application audit; the production warm/cold ratio is not
  measured.

## Frontend 76653109 (September 27, 10:47 UTC)

The owner asked for four marks on the released screens to be fixed and
published: the Dispatch clocks folded Cycle onto a second line behind a
hairline; the Table's day headings were nearly invisible; Papers put
the price and both rates per mile on the miles' line; the truck card's
"ETA" and "At stop" ran apart. Commits `3b75f79e` (clocks, days, Papers)
and `76653109` (ETA column), on `dbfec037`; Client styles and style tests
only. No server, contract, migration or deployment change; the API
(`amftms-api-b-fd2d5f25-…`) and the database were not touched, so no
backup was needed.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash deploy-client.sh` from a clean
  detached worktree with the local settings file copied in (10:41-10:47
  UTC), exit 0: JavaScript 665, Client 1,255, Server 3,791; offline UI
  smoke without failures (`browser-ui-YATcyD`); messaging tabs smoke
  without errors. A first run on `3b75f79e` was stopped mid-gate to add
  the ETA fix; nothing from it was published. PostgreSQL execution
  checks were not run.
- **Artifact:** `release-TDTuIV`, 285 files, pinned in the main
  checkout's `artifacts/managed/release-TDTuIV` with the gate log and
  the smoke report under `gate/`.
- **Live:** Hosting live channel released 10:47:35 UTC. `index.html`
  (`c90a085c68496cc8fdf8c9f72c029c04d014d8d1ae518db1f65a359e1eedeec9`)
  and `css/main.css?v=54904b0f7353a2c4` are byte-identical to the
  artifact; entry HTML answers `no-cache`. Rollback target: the
  previous release, `release-GTsl5s`.
- **Seen before release:** each fix in a static page built from the
  compiled stylesheet (clocks at 1100 and 800 px on one line; day
  bands; money under the miles; ETA beside its label without a booking,
  aligned with Appointment when there is one). Not yet seen on live
  data.

## Frontend 2f798c05 (September 27, 11:14 UTC)

Three more of the owner's marks, published on the owner's word: in
Papers the truck and its trailer fold as one unit, and the money line
reads "590.00 USD · RPM 2.60 · Total 1.70" with the full names in its
tooltip; in the Table each distance shows miles and kilometres on one
line. Commit `2f798c05` on `2b92c3ca`; Client, Client tests and style
tests only. API and database untouched; no backup needed.

- **Gate:** `PULSARTMS_RELEASE_UI=1 bash deploy-client.sh` from a clean
  detached worktree with the local settings copied in (11:07-11:14 UTC),
  exit 0: JavaScript 665, Client 1,255, Server 3,791; offline UI smoke
  without errors (`browser-ui-ZITcwj`). PostgreSQL checks not run.
- **Artifact:** `release-OYVBFR`, 285 files, pinned in the main
  checkout's `artifacts/managed/release-OYVBFR` with the gate log and the
  smoke report under `gate/`.
- **Live:** `index.html`
  (`9c718281c8b52138982e2835858e76d78584cff42a5db49a7886dea1f7f8b87f`)
  and `css/main.css?v=24d21b66de4c8b3d` byte-identical to the artifact.
  Rollback target: `release-TDTuIV`.

## Stop-save and Discard incident, 20b6ec91 (September 27, 11:49 UTC)

API and frontend, on root's authorization after root's own fix and
review. Candidate `claude/stop-save-candidate` = `49b0b277` (the released
frontend) plus root's `d04ad9f6` (Discard and leave clears every stop
editor draft), `fe9778ac` (write responses read persisted values; a stop
draft's 409 offers reload instead of an endless retry), `944fa9d6`
(canonical price scale in workspace fingerprints) and `fae9f9cb`
(consecutive stop saves in fresh contexts), cherry-picked as `296c1554`,
`ecc6de31`, `4a2f32fc`, `20b6ec91`. No migration, no guard removed.
The truck 11005 resource conflict (AMF1403 active, trailer 55904 versus
055904) is root's separate repair and is not fixed by this release.

- **Gate, once:** `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on a
  clean detached worktree of `20b6ec91` with the local settings copied in
  (11:33-11:40 UTC), exit 0: JavaScript 665, Client 1,258, Server 3,793,
  none skipped; offline UI smoke without errors (`browser-ui-q8XVBH`).
  Verified artifact `release-H5e12a`, 285 files, pinned in the main
  checkout's `artifacts/managed/release-H5e12a` with both logs and the
  smoke report under `gate/`.
- **Backup:** `local-backups/pulsartms-release-backup.5M7i0m/
  before-2026-09-27-release-20b6ec91.dump`, sha256
  `a1ec2d13a5e90ee80bc36b185d0a284e296b4bb81ac3a47125848820835b930a`,
  pg_dump custom, 720 entries, 104 table data, 11:41 UTC; recorded in
  the inventory; restore not rehearsed.
- **API:** `deploy-server.sh`, build `944ddc8d-982c-4975-95c5-5a8c04a9869c`,
  image `api@sha256:815c0dd3c1d7ff33169d7d703a235b4492bbae1d97b6639a81b6c60af1c23015`,
  revision `amftms-api-b-944ddc8d-…` at 100%, generation 276 (was 274,
  `amftms-api-b-fd2d5f25-…`, the rollback target). 1 GiB, 1 CPU, at most
  one instance, as before. Health 200 on the service and through
  Hosting; no error-level entry or 5xx in its first minutes.
- **Frontend:** the gate's artifact published as tested, without a
  second gate (11:49:53-11:50:00 UTC, 285 files). Live `index.html`
  (`5340dba08ebac8230f701a7a94718a750c3f04b8d74c9f8186184f68848a3809`),
  `css/main.css?v=24d21b66de4c8b3d`, `Client.*.wasm` and `dotnet.*.js`
  match the artifact; entry HTML `no-cache`; settings served.
- **Data:** protected counts before and after, read-only: unchanged
  (73 migrations, 3 and 3 users, 5 conversations, 57 messages, 0 driver
  messages, 0 broadcasts, 0 hand-overs, 2 credentials). No production
  row was written by this release.

## Not done

Cross-instance messaging notifications and the broader module
decoupling remain unimplemented, as the audit conclusion records. No
restore was rehearsed. Production timing of the shared reads was not
measured.
