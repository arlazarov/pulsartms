import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/fleet-map/popup';@use 'pages/fleet-map/station';@use 'shared/fuel/visit';", {
  loadPaths,
}).css;

// A planned fuel stop, in the truck card's language: two halves under one
// head - the place on the left, the plan for it on the right, each ending
// where the card does. The halves are two elements; laid out as eight flat
// children in two columns, the buttons could only stand across the foot of
// both, with an empty corner above them.
test('a planned fuel stop is two halves: the place, and the plan', () => {
  assert.match(
    css,
    /\.fleet-station-popup--planned\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\);/,
  );
  // A quote has no plan for the station, so the halves dissolve and the card
  // is the one column of facts it has always been.
  assert.match(
    css,
    /\.fleet-station-popup__place,\s*\.fleet-station-popup__plan,\s*\.fleet-station-popup__plan-head\s*\{\s*display: contents;/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__place,\s*\.fleet-station-popup--planned > \.fleet-station-popup__plan\s*\{[^}]*display: flex;[^}]*flex-direction: column;/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__plan\s*\{[^}]*border-left: 1px solid/,
  );
  // The head of the plan: which stop it is, and what is left to it.
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-station-popup__plan-head\s*\{[^}]*display: flex;[^}]*justify-content: space-between;/,
  );
  // The buttons change the plan, so they stand at the foot of the half they
  // act on - however tall the other half is.
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-station-popup__actions:not\(\[hidden\]\)\s*\{[^}]*margin-top: auto;[^}]*justify-content: flex-start;/,
  );
});

// The tank is said and then drawn: the two levels on a line, the bar under
// them, the gallons under its ends. Bare, the bar stood under "Left" and
// read as the road to the pump. The last dials on the map went before it.
test('the tank is named, then one bar, and the purchase is a fact', () => {
  assert.match(css, /\.fleet-fuel-visit__tank\s*\{[^}]*display: grid;/);
  assert.match(
    css,
    /\.fleet-fuel-visit__levels\s*\{[^}]*display: flex;[^}]*align-items: baseline;/,
  );
  assert.match(
    css,
    /\.fleet-fuel-visit__added\s*\{[^}]*margin-inline-start: auto;/,
  );
  assert.match(
    css,
    /\.fleet-fuel-visit__bar\s*\{[^}]*display: flex;[^}]*overflow: hidden;/,
  );
  assert.match(css, /\.fleet-fuel-visit__bar-add\s*\{[^}]*opacity: 0\.35;/);
  assert.match(
    css,
    /\.fleet-fuel-visit__ends\s*\{[^}]*justify-content: space-between;/,
  );
  assert.match(
    css,
    /\.fleet-fuel-visit__facts\s*\{[^}]*grid-template-columns: max-content minmax\(0, 1fr\);/,
  );
  assert.match(
    css,
    /\.fleet-fuel-visit__fact\s*\{[^}]*grid-template-columns: subgrid;/,
  );
  assert.doesNotMatch(css, /fleet-fuel-visit__(dial|gauge|buy)/);
});

test('too narrow for two halves, the plan goes under the place', () => {
  assert.match(
    css,
    /\.fleet-map-inspector__native\s*\{\s*container: map-inspector\s*\/\s*inline-size;/,
  );
  const narrow = css.slice(
    css.indexOf('@container map-inspector (width < 34rem)'),
  );
  assert.ok(narrow.length > 0 && narrow.length < css.length);
  assert.match(
    narrow,
    /\.fleet-station-popup--planned\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  // Stacked, the hairline between the halves is the line above the plan.
  assert.match(
    narrow,
    /__plan\s*\{[^}]*border-left: 0;[^}]*border-top: 1px solid/,
  );
});
