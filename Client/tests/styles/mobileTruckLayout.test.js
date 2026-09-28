import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = name => compileString(`@use '${name}';`, { loadPaths }).css;
const header = compile('pages/fleet-map/inspector');

test('mobile card expands both panels inside the bounded inspector', () => {
  const details =
    compile('pages/fleet-map/stage') + compile('pages/fleet-map/inspector');
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*max-height: 55%;/);
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*overflow: auto;/);
  // Half of a stage that keeps half the screen: the map is what the page
  // is for, and the card scrolls for the rest.
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*max-height: 50%;/);
  // The floating card (below the docked layout) opens closed behind
  // Details; the docked panel is always open, so the button is hidden
  // there (the owner, September 27, replacing the phone's open-whole card
  // of September 26 that a later override had been hiding).
  assert.match(header, /is-mobile-collapsed/);
  assert.match(
    header,
    /fleet-map-mobile-summary__toggle\s*\{[^}]*display: none;/,
  );
  assert.match(
    header,
    /@media \(width < 1100px\)\s*\{\s*\.fleet-map-inspector\[data-inspector-mode=truck\] \.fleet-map-mobile-summary__toggle\s*\{\s*display: inline-flex;/,
  );
  assert.doesNotMatch(header, /fleet-map-reveal/);
  assert.doesNotMatch(header, /inset: 100% 0 auto;/);
  assert.match(header, /__header\s*\{\s*position: static;/);
});
