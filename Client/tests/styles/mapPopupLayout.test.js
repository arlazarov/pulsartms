import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const popups = compileString(`@use 'pages/fleet-map/popup-content';`, {
  loadPaths,
}).css;

// The stop and the station windows are the same card language as the truck
// card: a fact is a muted name and a figure on one label column, sections
// are parted by hairlines, and a number is never taken apart.

test('a word breaks only where it must, so short ones stay whole', () => {
  // "anywhere" broke every word, not only the address that needed it:
  // "Drop Off" came apart one letter per line and 4.019 read as 4.01 over 9.
  assert.doesNotMatch(popups, /overflow-wrap: anywhere;/);
  assert.match(
    popups,
    /\.fleet-route-popup\s*\{[^}]*overflow-wrap: break-word;/,
  );
  assert.match(
    popups,
    /\.fleet-station-popup\s*\{[^}]*overflow-wrap: break-word;/,
  );
});

test('every stop fact is the same two cells on one label column', () => {
  // The appointment used to give its label a line of its own, which read as
  // a different kind of thing from the ETA directly under it.
  // The fuel on arrival is a fact about the stop too, so it is a row here
  // rather than a dial in a block of its own.
  assert.match(
    popups,
    /__appointment,[^{}]*__eta\.fleet-route-popup__field,[^{}]*__distance,[^{}]*__fuel\s*\{[^}]*grid-template-columns: subgrid;/,
  );
  const stops = readFileSync(
    new URL('../../Scripts/fleetMap/routes/routeStops.js', import.meta.url),
    'utf8',
  );
  assert.doesNotMatch(stops, /createFuelGauge/);
  assert.match(stops, /'Fuel on arrival',[\s\S]{0,120}fleet-route-popup__fuel/);
  assert.match(
    popups,
    /__facts\s*\{[^}]*grid-template-columns: max-content minmax\(0, 1fr\);/,
  );
});

// The close control is a character in a fixed box, and the box sits on
// cards that set different type sizes. It was inheriting theirs, so the same
// control read larger on a stop than on a truck.
test('the close control is the same size on every card', () => {
  const details = compileString(`@use 'pages/fleet-map/details';`, {
    loadPaths,
  }).css;
  assert.match(
    details,
    /__close\s*\{[^}]*\}[\s\S]{0,120}?font-size: var\(--type-heading\);[^}]*line-height: 1;/,
  );
});

test('a price stays one number', () => {
  assert.match(
    popups,
    /__prices[\s\S]{0,400}?dd\s*\{[^}]*white-space: nowrap;[^}]*font-variant-numeric: tabular-nums;/,
  );
});

// The purchase is a fact of the visit, beside the fill it pays for, set off
// under a hairline. It was a caption run into its own figure at the foot of
// the card, and then a footer of its own; both are gone.
test('the purchase is a fact of the visit, not a footer of the card', () => {
  assert.match(
    popups,
    /\.fleet-fuel-visit__facts\s*\{[^}]*border-top: 1px solid/,
  );
  assert.doesNotMatch(popups, /fleet-station-popup__(visit-)?cost/);
});

test('the link out of the map keeps its arrow on the last word', () => {
  const stops = readFileSync(
    new URL('../../Scripts/fleetMap/routes/routeStops.js', import.meta.url),
    'utf8',
  );
  assert.match(stops, /'Route & load details ↗'/);
});
