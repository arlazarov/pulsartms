import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = source => compileString(source, { loadPaths }).css;
const css = compile(
  "@use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector';" +
    "@use 'pages/fleet-map/workspace'; @use 'pages/fleet-map/popup';" +
    "@use 'pages/fleet-map/station';",
);

// The map's card is one box at each width of the screen, whatever it shows
// and whenever its answers arrive, so its edges never move between choices
// (the owner, September 28).
test('the map card is one box per breakpoint and scrolls inside', () => {
  const floating = css.match(/\n\.fleet-map-info-reserved\s*\{([^}]*)\}/)[1];
  assert.match(
    floating,
    /width: min\(100%, var\(--size-map-compact-inspector\)\);/,
  );
  assert.match(floating, /\sheight: 55%;/);
  assert.match(floating, /overflow: auto;/);
  assert.doesNotMatch(floating, /max-height|fit-content/);

  const phone = css.slice(css.indexOf('@media (width < 768px)'));
  assert.match(phone, /\.fleet-map-info-reserved\s*\{\s*height: 50%;\s*\}/);

  const docked = css.match(
    /\.fleet-map-stage > \.fleet-map-info-reserved\s*\{([^}]*)\}/,
  )[1];
  assert.match(docked, /justify-self: stretch;/);
  assert.match(docked, /align-self: start;/);
  assert.match(docked, /height: min\(100%, var\(--size-map-panel-height\)\);/);

  // No mode sizes the box: a truck, a stop, a station quote or a fuel plan.
  assert.doesNotMatch(
    css,
    /\.fleet-map-inspector\[data-inspector-mode=[a-z]+\](?::[a-z]+\([^{]*\))?\s*\{[^}]*(?<![-\w])(?:width|min-width|max-width|height|min-height|max-height):/,
  );
});

test('a copied value says so over its row without moving it', () => {
  const shared = compile("@use 'shared/copy-value';");
  assert.match(shared, /\.copy-value\s*\{[^}]*cursor: copy;/);
  const status = shared.match(/\.copy-value__status\s*\{([^}]*)\}/)[1];
  assert.match(status, /position: absolute;/);
  assert.match(status, /pointer-events: none;/);
  assert.match(css, /\.fleet-truck-next__booking\s*\{[^}]*position: relative;/);
  assert.match(
    css,
    /\.fleet-route-popup__appointment\s*\{[^}]*position: relative;/,
  );
});

// Another card, an editor or the camera parks the truck panel rather than
// hiding it: display: none restarts its arrival on the way back (Dispatch,
// September 28).
test('the truck panel parks without display: none', () => {
  const parked = css.match(/\.fleet-truck-panel\.is-parked\s*\{([^}]*)\}/)[1];
  assert.match(parked, /visibility: hidden;/);
  assert.match(parked, /position: absolute;/);
  assert.match(parked, /height: 0;/);
  assert.doesNotMatch(parked, /display: none/);
  const overlaid = css.match(
    /\.fleet-map-info-reserved\.is-overlaid\s*\{([^}]*)\}/,
  )[1];
  assert.match(overlaid, /visibility: hidden;/);
  assert.doesNotMatch(overlaid, /display: none/);
});
