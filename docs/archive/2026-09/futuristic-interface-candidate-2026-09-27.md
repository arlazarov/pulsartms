# Fleet workspace candidate — September 27, 2026

Reviewable candidate on branch `claude/futuristic-pulsar-ui-concept-80ccd4`:
the Fleet workspace (fleet rail, map, selected-trip inspector, trip chain)
and the matching Dispatch frame as the application's only interface.
Nothing here is released or pushed. The Dispatch release owner integrates
it after its own frozen release; this branch never deploys.

## The earlier design

By the owner's decision the earlier layout was removed, with the Current /
Futuristic switch in Personal settings and every layout branch that
depended on it. It is not shipped as a fallback. It is kept only in Git:

- tag `ui-legacy-2026-09-27` (annotated) and branch
  `recovery/pre-futuristic-ui-2026-09-27`, both at `0e6add5d`, the
  current-work candidate before any of this work.
- Restoring it is a release of that revision (or a revert of this
  branch's commits after it); no data, schema or server contract depends
  on either design, so nothing else has to change.

## Contents

- It contains the current-work candidate `0e6add5d`
  (`claude/current-work-design`), merged; integrating that candidate first
  leaves only this delta to review.
- Client only. No server code, schema, migration, configuration or
  secret changes. The Fleet trip chain adds one read of an existing
  endpoint (`GET api/dispatch/board?truckId=`), once per truck chosen and
  at the board's one-minute cadence.
- The map's camera no longer appears in `/fleet/map` addresses or in a
  load's `from`; it is kept in the tab's `sessionStorage`, one record per
  signed-in user, and restored only to the same truck (or to none). An
  older link's `view` is used once and dropped without a history entry.
- A later trip's stop opens the existing next-load stop card above that
  trip, so its distance through the preceding loads, fresh ETA and late
  reply guards stay with their owner; Follow stays in the card's head.
- `prototypes/futuristic-ui/` is the localhost design concept (its own
  read-only proxy); it is not built or served by the Client and may be
  left out of the integration.
- Scope and parity: `prototypes/futuristic-ui/PARITY.md`; contract:
  `docs/ui-controls.md`, "Fleet workspace" and "Returning from a load".

## Checks run on this tree

- `bash test.sh identity fleet dispatch`: Client.Tests 1168 passed,
  Server.Tests 2388 passed, Node suites passed (architecture included).
- `npm test` (Client, 680), `npm run js:check`, strict Client build
  (`-warnaserror`), CSharpier on changed C# files.
- `returnNavigationSmoke.mjs` on a Release publish: 35 checks, including
  the camera link cleaned without a history entry, the address never
  carrying the camera, and Back to map / browser Back restoring it. Its
  Dispatch part was repaired for Completed being read in the Table.
- `workspaceSmoke.mjs` (was `futuristicSmoke.mjs`) on the same publish
  with the real basemap: 87 checks, none failed (run browser-ui-YXbBfV,
  pinned): 20 trucks, Follow through GPS reports at constant zoom, drag
  and resume, no motion after GPS stops, satellite at close zoom and road
  map zoomed out, chains of 1/4/9 trips at 1440 and 360 px, editors in
  both themes, Dispatch views. The satellite pixel check now asks for 1.5x
  the road map's distinct colours, not 2x: rural imagery measured 1.9x.
- Removing the interface scope reordered the Fleet phone rules; the
  layout blocks now come last in `_workspace.scss`, verified at 360 px.

## Not run or not covered

- Full `bash verify-release.sh` gate, `uiSmoke`, `messagingTabsSmoke`,
  PostgreSQL fixture tests: for the integration owner on the frozen tree.
- `fuelEditorSmoke` and `routeEditorSmoke` fail on untouched `0e6add5d`
  (card-layout assertions predating these changes); not usable as
  evidence until repaired by the card owner.
- Signed-in live data; the route options content (its preview request was
  refused in the harness); pressing save or send anywhere.
- The page shell is not yet the approved demo's (topbar, page head with
  chips, fleet table, Route/Fuel tabs); that rebuild follows on this
  branch.

## Integration notes

1. Integrate after the current-work candidate is released or with it, in
   one gate; both touch `FleetMap.razor`, `ActionIcon`, tokens and
   `ui-controls.md`, already reconciled here.
2. Frontend only: `deploy-client.sh` from a checkout that has the ignored
   `Client/wwwroot/appsettings.json`.
3. After release: sign in and check Fleet Map with a moving truck (Follow,
   satellite at close zoom, Back from a load keeps the camera, the address
   has no `view`) and Dispatch's Cards, Table and Papers.
