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
  assert.match(details, /\.fleet-map-info-reserved\s*\{[^}]*max-height: 60%;/);
  assert.match(header, /is-mobile-collapsed/);
  assert.match(header, /fleet-map-mobile-summary__toggle/);
  assert.doesNotMatch(header, /fleet-map-reveal/);
  assert.doesNotMatch(header, /inset: 100% 0 auto;/);
  assert.match(header, /__header\s*\{\s*position: static;/);
});

test('mobile identity wraps without changing the desktop header', () => {
  assert.match(header, /__crew\s*\{\s*display: contents;/);
  assert.match(header, /__crew\s*\{\s*display: flex;\s*flex-wrap: wrap;/);
  assert.doesNotMatch(header, /__driver-label/);
  const toolbar = compile('pages/fleet-map/toolbar');
  assert.match(toolbar, /__background > \.page-header\s*\{\s*display: none;/);
});
