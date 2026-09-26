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
