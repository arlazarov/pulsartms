import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = name => compileString(`@use '${name}';`, { loadPaths }).css;
const mobile = compile('pages/fleet-map/mobile-inspector');
const header = compile('pages/fleet-map/compact-inspector');

test('desktop temperature shares the four-column telemetry row', () => {
  assert.match(
    header,
    /__telemetry\s*\{[^}]*repeat\(4, minmax\(0, max-content\)\)/,
  );
  const outside = header.match(/__outside\s*\{([^}]*)\}/)?.[1];
  assert.match(outside, /display: grid;/);
  assert.doesNotMatch(outside, /grid-column: 1 \/ -1|padding-top:/);
});

test('mobile card expands both panels inside the bounded inspector', () => {
  const details = compile('pages/fleet-map/details');
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*max-height: 55%;/);
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*overflow: auto;/);
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*max-height: 60%;/);
  assert.match(header, /is-mobile-collapsed/);
  assert.match(header, /fleet-map-mobile-summary__toggle/);
  assert.doesNotMatch(header, /fleet-map-reveal/);
  assert.doesNotMatch(header, /inset: 100% 0 auto;/);
  assert.match(header, /__header\s*\{\s*position: static;/);
});

test('mobile truck places two-by-two readings under the header clocks', () => {
  assert.match(mobile, /^@media \(max-width: 767px\)/);
  // The clocks read from the card head, at every width, so their sizing
  // lives with the header rather than in the mobile override.
  assert.match(header, /__hours\s*\{[^}]*--hos-display: grid;/);
  assert.match(header, /--hos-columns: repeat\(4, minmax\(0, 1fr\)\);/);
  assert.match(header, /__hours\s*\{[^}]*container-type: inline-size;/);
  assert.match(header, /100cqi - 3 \* var\(--space-sm\)/);
  assert.doesNotMatch(mobile, /--hos-/);
  assert.match(mobile, /__telemetry\s*\{[^}]*repeat\(2, 6ch\);/);
  assert.match(header, /--hos-dial-size: min\(/);
  assert.match(header, /var\(--size-hos-dial\)/);
  assert.match(
    mobile,
    /--fuel-reading-icon-size: var\(--size-telemetry-icon-compact\);/,
  );
  assert.match(mobile, /--fuel-reading-icon-row: auto;/);
  assert.match(mobile, /--fuel-reading-value-column: 1;/);
  assert.match(mobile, /border-left: 1px solid var\(--ui-border-subtle\);/);
  assert.match(mobile, /@container map-truck-header \(width < 20rem\)/);
  assert.doesNotMatch(
    mobile,
    /\.driver-hours(?!-panel)|font-size:.*(?:px|rem)/,
  );
  assert.match(mobile, /__location\s*\{[^}]*grid-column: 1\s*\/\s*-1;/);
});

test('mobile identity wraps without changing the desktop header', () => {
  assert.match(header, /__crew\s*\{\s*display: contents;/);
  assert.match(header, /__crew\s*\{\s*display: flex;\s*flex-wrap: wrap;/);
  assert.match(header, /__driver-label\s*\{\s*display: none;/);
  const toolbar = compile('pages/fleet-map/toolbar');
  assert.match(toolbar, /__background > \.page-header\s*\{\s*display: none;/);
});

test('mobile route groups override the compact stacked placement', () => {
  const selector = String.raw`\.fleet-map-route-info > `;
  assert.match(
    mobile,
    new RegExp(
      selector +
        String.raw`\.fleet-map-route-info__distances\s*\{` +
        String.raw`[^}]*grid-column: 4\s*/\s*span 3;\s*grid-row: 1;`,
    ),
  );
  assert.match(
    mobile,
    new RegExp(
      selector +
        String.raw`\.fleet-map-route-info__visit\s*\{` +
        String.raw`[^}]*grid-column: 1\s*/\s*span 3;\s*grid-row: 2;`,
    ),
  );
  assert.match(
    mobile,
    new RegExp(
      selector +
        String.raw`\.fleet-map-route-info__timing\s*\{` +
        String.raw`[^}]*grid-column: 4\s*/\s*span 3;\s*grid-row: 2;`,
    ),
  );
});

test('mobile appointments wrap naturally without forced date breaks', () => {
  assert.match(mobile, /__appointment\s*\{\s*display: block;/);
  assert.match(mobile, /__appointment > strong\s*\{\s*overflow-wrap: normal;/);
  assert.doesNotMatch(mobile, /__appointment-separator\s*\{\s*display: none;/);
  assert.doesNotMatch(mobile, /__appointment-time\s*\{\s*display: block;/);
});

test('mobile ETA keeps its timestamp inline and places status below', () => {
  assert.match(mobile, /--stop-hours-road-display: block;/);
  assert.match(mobile, /--stop-hours-road-value-display: inline;/);
  assert.match(mobile, /--stop-hours-road-status-display: block;/);
  assert.doesNotMatch(mobile, /--stop-hours-road-white-space: nowrap;/);
  assert.doesNotMatch(mobile, /--stop-hours-date-break-display: block;/);
  const hours = compile('components/driver-status/stop-hours');
  assert.match(hours, /var\(--stop-hours-date-break-display, none\)/);
  assert.match(hours, /__clock\s*\{\s*white-space: nowrap;/);
});
