import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString(
  "@use 'shared/driver-status'; @use 'shared/trucks'; @use 'pages/fleet-map/route-info'; @use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector'; @use 'pages/fleet-map/layout'; @use 'pages/fleet-map/popup';@use 'pages/fleet-map/station';@use 'shared/fuel/visit';",
  { loadPaths },
).css;
// The truck card is these two files; the rest of the inspector folder is
// the shell every card shares and the other modes' own rules.
const compact = compileString(
  "@use 'pages/fleet-map/inspector/card';" +
    " @use 'pages/fleet-map/inspector/narrow';",
  { loadPaths },
).css;

test('map inspector content updates without reveal or fade animation', () => {
  for (const selector of ['fleet-map-info-reserved', 'fleet-map-info-content'])
    assert.match(css, new RegExp(`\\.${selector}\\s*\\{[^}]*animation: none;`));
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*transition: none;/);
  assert.doesNotMatch(css, /@keyframes fleet-map-(?:reveal|info-fade)/);
  // The route's messages under the panel, with the padding the panel's
  // own rules used to set in the truck mode's route grid.
  assert.match(
    css,
    /\.fleet-map-route-info\s*\{[^}]*padding: var\(--space-xs\) var\(--space-md\);/,
  );
});

test('map information caps its top gap by actual side clearance rather than viewport breakpoints', () => {
  assert.match(
    css,
    /\.fleet-map-info-reserved\s*\{\s*position: absolute;\s*top: min\(var\(--space-md\),\s*var\(--map-inspector-side-gap,\s*0px\)\);\s*left: 0;\s*right: 0;/,
  );
  assert.match(
    css,
    /\.fleet-map-info-reserved\s*\{[^}]*width: min\(100%,\s*var\(--size-map-inspector\)\);\s*margin-inline: auto;/,
  );
  assert.match(
    css,
    /\.fleet-map-info-reserved\s*\{[^}]*border-radius: var\(--radius-md\);[^}]*box-shadow: var\(--shadow-card\);/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-info-reserved\s*\{[^}]*(?:scrollbar-gutter|backdrop-filter):/,
  );
  assert.match(
    css,
    /\.fleet-map-info-reserved\s*\{[^}]*background: var\(--ui-surface\);/,
  );
  assert.match(css, /\.fleet-map-info-content\s*\{[^}]*gap: 0;/);
  // The messages keep one rule above them and no box of their own; the
  // owner sets that once instead of a reset and a re-border in the stage.
  const routeInfo = css.match(/\n\.fleet-map-route-info\s*\{([^}]*)\}/)[1];
  assert.match(routeInfo, /border-top: 1px solid var\(--ui-border-subtle\);/);
  assert.doesNotMatch(routeInfo, /border(?:-radius)?: |background:/);
  assert.doesNotMatch(css, /\.fleet-map-info-reserved \.fleet-map-route-info/);
  assert.match(
    css,
    /\.fleet-map-info-content\s*\{[^}]*display: flex;[^}]*flex-direction: column;/,
  );
  const mobilePanel = css.match(
    /\.fleet-map-info-reserved\s*\{(\s*max-height: 50%;[^}]+)\}/,
  );
  assert.ok(
    mobilePanel,
    'the mobile inspector inherits continuous shared placement',
  );
  assert.doesNotMatch(mobilePanel[1], /(?:top|left|right):/);
});

test('one map inspector retains hidden content and gives native and future details no popup positioning', () => {
  // Hidden content stays in the card and the app hides it; the card does
  // not repeat that rule - see the style-token checks.
  assert.doesNotMatch(css, /\[hidden\]\s*\{\s*display: none/);
  assert.match(
    css,
    /\.fleet-map-inspector__header\s*\{\s*position: sticky;\s*top: 0;[^}]*display: flex;/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector__next\s*\{\s*position: static;\s*width: auto;\s*max-width: none;\s*border: 0;\s*box-shadow: none;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-inspector__native\s*\{[^}]*(?:position|bottom|right|max-width):/,
  );
  assert.match(
    css,
    /\.fleet-map-inspector__close\s*\{[^}]*width: var\(--size-control-touch\);/,
  );
});

test('desktop and mobile actions wrap without reserving blank reference rows', () => {
  assert.doesNotMatch(css, /fleet-map-truck-info__buttons/);
  assert.doesNotMatch(
    css,
    /min-height: (?:calc\()?var\(--size-map-route-address-stacked-min\)/,
  );
});

// The map carries no key (the owner, September 27); the attribution
// Google asks for while its weather is shown stays, over the map.
test('the map has no key, and keeps the weather attribution', () => {
  assert.doesNotMatch(css, /\.fleet-map-key/);
  assert.match(css, /\.fleet-map-weather-source\s*\{\s*position: absolute;/);
  const razor = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  assert.doesNotMatch(razor, /fleet-map-key/);
  assert.match(razor, /Source: Includes weather data from Google/);
});

test('the planned stop has its own title size, and no dials or illustration are left', () => {
  // The illustration left the map with the panel it stood in.
  assert.doesNotMatch(css, /truck-illustration/);
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-station-popup__title\s*\{[^}]*font-size: var\(--type-subtitle\);/,
  );
  // The dials that used to be sized here are gone from the map.
  assert.doesNotMatch(css, /fleet-fuel-visit__dial/);
});
