import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];

test('both filter toolbars share aligned controls and restrained native checkbox styling', () => {
  const css = compileString("@use 'components/filter-toolbar';", { loadPaths }).css;
  assert.match(css, /\.filter-toolbar\s*\{[^}]*align-items: center;[^}]*gap: var\(--space-sm\);/s);
  for (const selector of ['.filter-toolbar__views button', '.filter-toolbar__toggle']) {
    const rule = css.slice(css.indexOf(`${selector} {`));
    assert.match(rule, /min-height: var\(--size-control\);/);
    assert.match(rule, /min-height: var\(--size-control-touch\);/);
  }
  assert.match(css, /\.filter-toolbar__toggle input\s*\{[^}]*width: var\(--size-checkbox\);[^}]*accent-color: var\(--ui-link\);/s);
  assert.match(css, /\.filter-toolbar__toggle:has\(input:checked\)\s*\{\s*color: var\(--ui-text\);\s*\}/);
  assert.doesNotMatch(css, /box-shadow|appearance:\s*none/);
});

test('Fleet and Dispatch keep their native bindings and use the same toolbar classes', () => {
  const fleet = readFileSync(new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url), 'utf8');
  const dispatch = readFileSync(new URL('../../Pages/Dispatch/DispatchList.razor', import.meta.url), 'utf8');
  assert.match(fleet, /<fieldset class="fleet-map-toolbar filter-toolbar" disabled="@_disposed">/);
  assert.equal((fleet.match(/class="fleet-map-toggle filter-toolbar__toggle"/g) ?? []).length, 5);
  for (const value of ['UseIfta', 'ShowFuelStations', 'ShowTrucks', 'ShowTraffic', 'ShowNextLoads'])
    assert.match(fleet, new RegExp(`type="checkbox"[\\s\\S]*?@bind="${value}"`));
  assert.match(dispatch, /class="dispatch-page__filters dispatch-board__filters filter-toolbar"/);
  assert.match(dispatch, /class="dispatch-view filter-toolbar__views" role="group" aria-label="Dispatch view"/);
  assert.equal((dispatch.match(/aria-pressed=/g) ?? []).length, 5);
  assert.match(dispatch, /aria-label="Load scope"/);
  assert.match(dispatch, /id="dispatch-active"/);
  assert.match(dispatch, /id="dispatch-completed"/);
});
