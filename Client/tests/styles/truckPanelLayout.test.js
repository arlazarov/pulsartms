import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

// The truck panel that replaced the old card's head rows (the owner,
// September 27): a next-stop line, eight facts in two columns and the
// driver's clocks dressed through the component's own properties.
const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString(
  "@use 'shared/driver-status'; @use 'pages/fleet-map/workspace';",
  { loadPaths },
).css;
const razor = readFileSync(
  new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
  'utf8',
);

test('the next stop line leads the panel with its distance and forecast', () => {
  assert.match(
    razor,
    /<section class="fleet-truck-next" aria-label="Next stop">/,
  );
  assert.match(razor, /fleet-truck-next__left[\s\S]*?LeftMiles/);
  assert.match(razor, /fleet-truck-next__eta[\s\S]*?Memory="_arrivalMemory"/);
  assert.ok(
    razor.indexOf('fleet-truck-next') < razor.indexOf('fleet-truck-facts"'),
  );
  assert.match(
    css,
    /\.fleet-truck-next__label\s*\{[^}]*text-transform: uppercase;/,
  );
});

test('the facts are two columns with the weather owner in its own cell', () => {
  assert.match(
    css,
    /\.fleet-truck-facts\s*\{[^}]*grid-template-columns: repeat\(2, minmax\(0, 1fr\)\);/,
  );
  assert.match(razor, /TruckWeather\s+Variant="fact"/);
  assert.doesNotMatch(razor, /fleet-map-inspector__hours|TruckLocationLine/);
});

test('the clocks are dressed through the component, not reached into', () => {
  assert.match(css, /\.fleet-truck-clocks\s*\{[^}]*--hos-cell-border:/);
  const page = readFileSync(
    new URL('../../Styles/pages/fleet-map/_workspace.scss', import.meta.url),
    'utf8',
  );
  assert.doesNotMatch(page, /\.driver-hours__/);
});
