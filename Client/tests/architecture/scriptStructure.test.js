import test from 'node:test';
import assert from 'node:assert/strict';
import { scanned } from './scanned.js';
import { readFileSync, readdirSync } from 'node:fs';

const root = new URL('../../Scripts/', import.meta.url);
// Both extensions: a module that moved to TypeScript is the same module,
// and until this said so the size rule had stopped reading every file that
// had been converted.
const modules = scanned(
  'browser modules',
  readdirSync(root, { recursive: true }).filter(
    x => typeof x === 'string' && /\.[jt]s$/.test(x) && !x.endsWith('.d.ts'),
  ),
  60,
);
const length = file =>
  readFileSync(new URL(file, root), 'utf8').split('\n').length;

// Seven modules were written as whole screens at once. They are recorded
// here at the length they had when this was written: each may shrink, none
// may grow, and the number in this list only ever comes down. Anything not
// listed is a module that was started after the rule and stays small.
//
// A module leaves this list by getting under 300 lines, where the ordinary
// rule below holds it - the truck layer left when its camera and its
// playback became things of their own.
//
// A module is named without its extension, because moving one to TypeScript
// does not make it a different module. That move is the one thing allowed
// to raise a number here, once, by what the types themselves take: the
// station card went from 309 to 327 that way, and the entry says so.
const written = new Map([
  ['fleetMap/fleetMap', 623], // 788 before its ETAs and its editor focus
  ['fleetMap/routes/routeLayer', 308], // 496 before its road and its shape
  ['fleetMap/stations/stationLayer', 405], // 458 before its prices and its plan
  ['fleetMap/stations/stationPopup', 327], // 309 before its types
]);

test('a module written as a whole screen may only get smaller', () => {
  for (const [name, budget] of written) {
    const file = modules.find(x => x === `${name}.ts` || x === `${name}.js`);
    assert.ok(file, `${name} is gone - drop it from the list`);
    const now = length(file);
    assert.ok(
      now <= budget,
      `${file}: ${now} lines, was ${budget} - it may shrink, not grow`,
    );
  }
});

test('a module started since is one thing you can name', () => {
  for (const file of modules) {
    if (written.has(file.replace(/\.[jt]s$/, ''))) continue;
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
  assert.match(guide, /tsconfig\.json/);
  assert.match(guide, /contracts\.d\.ts/);
});
