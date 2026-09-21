# Scripts

Everything the browser runs. Blazor owns page state and requests; these
modules own the map, its interaction and its animation.

```
shared/      used by more than one page: dialogs, popups, storage, appearance
dispatch/    the dispatch board's own behaviour
fleetMap/    the map, by subject:
  provider/    the Google map itself - loading it, mounting it, releasing it
  rendering/   what is drawn: the scene, its layers, markers and labels
  routes/      a truck's road, its stops, the loads it could take next
  stations/    fuel stations, their cards and what the plan buys there
  trucks/      where the trucks are, and the camera that follows one
  geometry/    pure maths: projection, paths, progress along a road
  ui/          cards, labels and the viewport around the map
  lifecycle/   mounting, releasing, yielding to the browser
```

`rendering/gpuScene.ts` is the only vendor entry point; esbuild builds it
and the shared chunks into `wwwroot/js/generated/`, which is never edited by
hand. `rendering/README.md` says what each renderer module owns.

## Types

`tsconfig.json` type-checks every file here: `npm run js:check`, and
`bash test.sh` runs it before the tests. It is not decoration - it has
caught a contract that named two of six fields, positions read as `lat`
that arrive as `latitude`, and a style object that gained half its keys
after it was handed out.

`fleetMap/contracts.d.ts` mirrors the DTOs in `Client/Models/DTO/`. When a
DTO gains a field the map reads, the mirror gains it too; otherwise the
checker says the field does not exist, and it is right.

Describe a callback where the factory is declared:

```js
/**
 * @param {(loadId: string | null, stopIndex: number,
 *   executionLegId?: string) => void} onSelection
 */
export function createNextLoadsLayer(map, Polyline, StopMarker, onSelection) {
```

A default of `() => {}` tells the checker the callback takes nothing, and
every call with arguments then passes unseen. That is how five of these
boundaries were undocumented.

The tree is being moved to TypeScript, module by module. A module may be
`.ts` or `.js` while that is going on; both are checked, both are bundled,
and an import names the file it means - `./stopCardContent.ts` - so that
Node can run the tests without a build step.

## One module, one thing

A module is one thing you can name: the road a truck drives, the card a
station opens, the words on that card. Anything else is two modules - the
trucks on the map and the camera over them, the stations and what fuel
costs at them, the road and the shape of the route it draws.

A few modules are still whole screens written as one closure -
`fleetMap.js`, `rendering/scene.js`, `rendering/sceneLayers.js`,
`routes/routeLayer.ts`, `stations/stationLayer.ts`. They are pinned at
their current length by `tests/architecture/scriptStructure.test.js`: they
may shrink, never grow, and nothing new may start out over 300 lines. A
module leaves that list by getting under 300, as the truck layer did.
