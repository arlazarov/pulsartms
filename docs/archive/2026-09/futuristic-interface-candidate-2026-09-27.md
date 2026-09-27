# Futuristic interface candidate — September 27, 2026

Reviewable candidate for the opt-in Futuristic interface of Fleet Map and
Dispatch on branch `claude/futuristic-pulsar-ui-concept-80ccd4`. Nothing
here is released or pushed. The Dispatch release owner integrates it after
its own frozen release; this branch never deploys.

## Contents

- It contains the current-work candidate `0e6add5d`
  (`claude/current-work-design`), merged; the only conflict was the
  generated stylesheet version in `wwwroot/index.html`, regenerated.
  Integrating the current-work candidate first leaves only the Futuristic
  delta to review.
- Client only. No server code, schema, migration, configuration or
  secret changes. The Fleet trip chain adds one read of an existing
  endpoint (`GET api/dispatch/board?truckId=`), only in the Futuristic
  interface, once per truck chosen and at the board's one-minute cadence.
- Default and fallback are the current interface. The choice lives on the
  device per account (`pulsr.interface.<account>`); clearing it, signing
  out or a new account returns Current. Rollback is choosing Current, or
  reverting the Futuristic commits; no data depends on them.
- `prototypes/futuristic-ui/` is the localhost design concept (its own
  read-only proxy); it is not built or served by the Client and may be left
  out of the integration.
- Scope and parity: `prototypes/futuristic-ui/PARITY.md`; contract:
  `docs/ui-controls.md`, "Futuristic interface".

## Checks run on this tree

- `bash test.sh identity fleet dispatch`: Client.Tests 1161 passed,
  Server.Tests 2388 passed, Node suites passed (architecture included).
- `npm test` (Client, 670), `npm run js:check`, strict Client build
  (`-warnaserror`), CSharpier on changed C# files.
- `futuristicSmoke.mjs` on a Release publish with the real basemap:
  both interfaces and themes at 1440/390/360 px without page overflow;
  list and chain selection; server phases in the chain equal Dispatch's,
  including a stale load; one board read per truck chosen; Follow on
  satellite through position updates, road map after zooming out,
  satellite again on Follow; fuel plan, editor, send window, camera and
  route options on screen at every width; Dispatch views keep search and
  scope, Completed in Table.

## Not run or not covered

- Full `bash verify-release.sh` gate, `uiSmoke`, `messagingTabsSmoke`,
  PostgreSQL fixture tests: for the integration owner on the frozen tree.
- `fuelEditorSmoke` and `routeEditorSmoke` fail on untouched `0e6add5d`
  (card-layout assertions predating these changes); not usable as
  evidence until repaired by the card owner.
- Signed-in live data; the route options content (its preview request was
  refused in the harness); pressing save or send anywhere.
- The interface choice as an account preference (future server change).

## Integration notes

1. Integrate after the current-work candidate is released or with it, in
   one gate; both touch `FleetMap.razor`, `ActionIcon`, tokens and
   `ui-controls.md`, already reconciled here.
2. Frontend only: `deploy-client.sh` from a checkout that has the ignored
   `Client/wwwroot/appsettings.json`.
3. After release: sign in, switch to Futuristic in Personal settings, check
   Fleet Map with a moving truck (Follow, satellite at close zoom) and
   Dispatch, then switch back to Current.
