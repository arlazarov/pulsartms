# Workspace interface candidate — September 27, 2026

Final candidate on branch `claude/futuristic-pulsar-ui-concept-80ccd4`:
the workspace ("HUD") design as the application's only interface -
Fleet Map, Dispatch (Cards, Table, Papers, the load page), settings and
the phone layouts. Nothing here is released. Dispatch is the sole deploy
owner; this branch never deploys.

The exact revision is the commit that adds this note (the branch head
when Dispatch receives it); the design content is fixed at `81614ec8`
plus the merge `a35c2ae5`.

## Server

The candidate contains the live server candidate `9af9a134`
(`claude/server-candidate`), merged cleanly, including the
MessagingSignals mailbox fix (`a8f2126b`). Outside `Client`,
`Client.Tests`, `docs` and `prototypes` the tree is identical to
`9af9a134` (`git diff 9af9a134 HEAD` lists nothing else). No schema,
migration, configuration or secret changes come from the design.

## The earlier design

Removed by the owner's decision, with the Current / Futuristic switch.
It is kept only in Git: tag `ui-legacy-2026-09-27` and branch
`recovery/pre-futuristic-ui-2026-09-27`, both at `0e6add5d`. Restoring
it is a release of that revision; no data or contract depends on either
design.

## Contents

- Every owner request, in order and with its status, is in
  `prototypes/futuristic-ui/FEEDBACK-CHECKLIST.md`; the contract is
  `docs/ui-controls.md`, "Fleet workspace".
- Client reads added: the Fleet trip chain reads the existing
  `GET api/dispatch/board?truckId=` once per chosen truck, at the board's
  one-minute cadence. The truck panel's next stop line, the stop card's
  Route bar and the Temperature fact reuse the route's own forecast,
  distance and readings; nothing else is fetched.
- The map camera is kept in the tab's `sessionStorage`, not in the
  address.

## Checks on this tree

Run on the design tree before the server merge (`ab4ffbc8`):

- `bash test.sh` (all groups): Client.Tests 1325/1325, Server.Tests
  3875/3875, Node suites 658/659; the one failure was a title selector
  from that change, fixed in `ab4ffbc8`.

Run after the merge (`a35c2ae5`), for what the merge and the later
design edits touch:

- Client.Tests (all): 1326/1326, including MessagingSignalsTests.
- `npm test` 659/659, `npm run format:check`, `npm run js:check`.
- Fleet component tests 403/403 after the last Razor change.

Server.Tests were not repeated after the merge: the server tree is
`9af9a134` unchanged, which Dispatch gated and released.

## Not run or not covered

- `verify-release.sh` on the final tree, `uiSmoke`,
  `messagingTabsSmoke`: two runs were stopped by later owner fixes;
  left to the integration owner's single final gate, not repeated here.
- Ungated probes still aimed at the removed truck card
  (`hoursForecastSmoke`, `fuelEditorSmoke`, `mobileTruckScrolling`,
  `truckReadingsLayout`): being ported in a separate session ("Repair
  browser probes for the new truck panel"); `workspaceSmoke` chain and
  trip-detail checks are partly stale.
- Visual checks were on the local read-only proxy (:5180) with live
  read data; Customers, Users, Border and fleet resources had no data
  there. No signed-in check of a released build; nothing saved or sent.
- Design gaps the owner has not closed: the concept's phone shell (pulse
  top bar, bottom nav); passed stops openable from chain and map; the
  light map's sonar ink and unselected later routes seen only in part.
- A never-chosen account's default theme is stored server-side as light;
  the client starts dark. Changing the stored default is Dispatch's.

## Integration notes

1. Frontend only on top of the live `9af9a134`: `deploy-client.sh` from
   a checkout that has the git-ignored `Client/wwwroot/appsettings.json`
   (Maps key).
2. One final gate on the frozen integration tree: `verify-release.sh`
   with `PULSARTMS_RELEASE_UI=1`.
3. After release: sign in and check Fleet Map (truck panel, Follow, stop
   card Route bar, theme switch), Dispatch Cards / Table and a load page,
   Settings, and a phone width.
