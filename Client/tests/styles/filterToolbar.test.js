import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];

test('Fleet heading shares a wrapping row with an elastic icon toolbar', () => {
  const css = compileString("@use 'pages/fleet-map/toolbar';", {
    loadPaths,
  }).css;
  assert.match(css, /__background\s*\{[^}]*display: flex;/);
  assert.match(css, /__background\s*\{[^}]*flex-wrap: wrap;/);
  assert.match(css, /\.fleet-map-search\s*\{[^}]*flex: 1 1 12rem;/);
  assert.match(css, /@media \(min-width: 768px\)/);
  assert.match(
    css,
    /\.fleet-map-layers \.fleet-map-toggle > span\s*\{\s*display: none;/,
  );
  const markup = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  for (const name of ['Fuel Stations', 'Traffic', 'Next loads']) {
    assert.ok(markup.includes(`title="${name}"`));
    assert.ok(markup.includes(`aria-label="${name}"`));
  }
});

test('pending preferences conceal state without collapsing toolbar space', () => {
  const css = compileString("@use 'pages/fleet-map/toolbar';", {
    loadPaths,
  }).css;
  const selector = '.fleet-map-layer-controls[aria-busy=true]';
  const start = css.indexOf(selector);
  assert.ok(start >= 0);
  const rule = css.slice(start).match(/\{([^}]+)\}/)[1];
  assert.match(rule, /visibility: hidden;/);
  assert.doesNotMatch(rule, /display:|height:|width:|transition:/);
});

test('both filter toolbars share aligned controls and restrained native checkbox styling', () => {
  const css = compileString("@use 'components/filter-toolbar';", {
    loadPaths,
  }).css;
  assert.match(
    css,
    /\.filter-toolbar\s*\{[^}]*align-items: center;[^}]*gap: var\(--space-sm\);/s,
  );
  for (const selector of [
    '.filter-toolbar__views button',
    '.filter-toolbar__toggle',
  ]) {
    const rule = css.slice(css.indexOf(`${selector} {`));
    assert.match(rule, /min-height: var\(--size-control\);/);
    assert.match(rule, /min-height: var\(--size-control-touch\);/);
  }
  assert.match(
    css,
    /\.filter-toolbar__toggle input\s*\{[^}]*width: var\(--size-checkbox\);[^}]*accent-color: var\(--ui-link\);/s,
  );
  assert.match(
    css,
    /\.filter-toolbar__toggle:has\(input:checked\)\s*\{\s*color: var\(--ui-text\);\s*\}/,
  );
  assert.doesNotMatch(css, /box-shadow|appearance:\s*none/);
});

test('Fleet and Dispatch keep their native bindings and use the same toolbar classes', () => {
  const fleet = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  const dispatch = readFileSync(
    new URL('../../Pages/Dispatch/DispatchList.razor', import.meta.url),
    'utf8',
  );
  assert.match(
    fleet,
    /<fieldset class="fleet-map-toolbar filter-toolbar"\s+disabled="@\(_disposed \|\| !_preferencesLoaded \|\| _initializing\)">/,
  );
  assert.equal(
    (fleet.match(/class="fleet-map-toggle filter-toolbar__toggle"/g) ?? [])
      .length,
    3,
  );
  assert.doesNotMatch(fleet, /ShowTrucks|OnTrucksToggleChanged/);
  // The fuel price basis is the fleet's setting, not a map switch.
  assert.doesNotMatch(fleet, /UseIfta|IFTA/);
  for (const value of ['ShowFuelStations', 'ShowTraffic', 'ShowNextLoads'])
    assert.match(
      fleet,
      new RegExp(`type="checkbox"[\\s\\S]*?@bind="${value}"`),
    );
  assert.match(
    dispatch,
    /class="dispatch-page__filters dispatch-board__filters filter-toolbar"/,
  );
  assert.match(
    dispatch,
    /class="dispatch-view filter-toolbar__views" role="group" aria-label="Dispatch view"/,
  );
  assert.equal((dispatch.match(/aria-pressed=/g) ?? []).length, 5);
  assert.match(dispatch, /aria-label="Load scope"/);
  assert.match(dispatch, /id="dispatch-active"/);
  assert.match(dispatch, /id="dispatch-completed"/);
});

test('Fleet layer chips use shared control metrics, native keyboard focus and theme state roles', () => {
  const css = compileString("@use 'pages/fleet-map/toolbar';", {
    loadPaths,
  }).css;
  assert.match(
    css,
    /\.fleet-map-toolbar \.fleet-map-toggle:has\(input:checked\)\s*\{[^}]*--button-bg: var\(--ui-action\);[^}]*color: var\(--ui-on-accent\);/s,
  );
  assert.match(
    css,
    /\.fleet-map-layer-controls\s*\{[^}]*border-inline-start: 1px solid var\(--ui-border-subtle\);/s,
  );
  assert.match(
    css,
    /\.fleet-map-filters\.is-open\s*\{[^}]*display: grid;[^}]*position: absolute;/s,
  );
  assert.doesNotMatch(css, /font-size:\s*\d+px|appearance:\s*none/);
  const razor = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  assert.match(
    razor,
    /class="fleet-map-layers" role="group" aria-label="Map layers"/,
  );
  assert.doesNotMatch(razor, /fleet-map-pricing|fleet-map-date/);
  assert.match(
    razor,
    /class="visually-hidden" role="status">@MatchingTrucks.Count found/,
  );
});
