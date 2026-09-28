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
  // The card is always open, on a phone too: no Details toggle and no
  // closed state (the owner, September 28); it scrolls inside its half.
  assert.doesNotMatch(header, /is-mobile-collapsed|mobile-summary__toggle/);
  assert.doesNotMatch(header, /fleet-map-reveal/);
  assert.doesNotMatch(header, /inset: 100% 0 auto;/);
  assert.match(header, /__header\s*\{\s*position: static;/);
});
