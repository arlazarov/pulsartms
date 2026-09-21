import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';

const root = new URL('../../Scripts/', import.meta.url);
const modules = readdirSync(root, { recursive: true }).filter(
  x => typeof x === 'string' && x.endsWith('.js'),
);
const length = file =>
  readFileSync(new URL(file, root), 'utf8').split('\n').length;

// Seven modules were written as whole screens at once. They are recorded
// here at the length they had when this was written: each may shrink, none
// may grow, and the number in this list only ever comes down. Anything not
// listed is a module that was started after the rule and stays small.
const written = new Map([
  ['fleetMap/fleetMap.js', 788],
  ['fleetMap/rendering/scene.js', 592],
  ['fleetMap/rendering/sceneLayers.js', 562],
  ['fleetMap/routes/routeLayer.js', 496],
  ['fleetMap/stations/stationLayer.js', 458],
  ['fleetMap/trucks/truckLayer.js', 439],
  ['fleetMap/stations/stationPopup.js', 309],
]);

test('a module written as a whole screen may only get smaller', () => {
  for (const [file, budget] of written) {
    assert.ok(modules.includes(file), `${file} is gone - drop it from the list`);
    const now = length(file);
    assert.ok(
      now <= budget,
      `${file}: ${now} lines, was ${budget} - it may shrink, not grow`,
    );
  }
});

test('a module started since is one thing you can name', () => {
  for (const file of modules) {
    if (written.has(file)) continue;
    const now = length(file);
    assert.ok(now <= 300, `${file}: ${now} lines - split it`);
  }
});

// The map's own README says what each folder holds; someone opening the
// tree for the first time reads that before any of the modules.
test('the browser sources explain themselves', () => {
  const guide = readFileSync(new URL('README.md', root), 'utf8');
  for (const folder of readdirSync(new URL('fleetMap/', root), {
    withFileTypes: true,
  }).filter(entry => entry.isDirectory()))
    assert.match(guide, new RegExp(`${folder.name}/`), folder.name);
  assert.match(guide, /jsconfig\.json/);
  assert.match(guide, /contracts\.d\.ts/);
});
