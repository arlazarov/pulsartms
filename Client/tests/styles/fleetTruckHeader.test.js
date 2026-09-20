import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString(
  "@use 'components/driver-status'; @use 'pages/fleet-map/truck-info'; @use 'pages/fleet-map/route-info'; @use 'pages/fleet-map/details'; @use 'pages/fleet-map/layout'; @use 'pages/fleet-map/popup-content';",
  { loadPaths },
).css;
const compact = compileString("@use 'pages/fleet-map/compact-inspector';", {
  loadPaths,
}).css;

test('supporting truck values have semantic contrast without another font size or spacing scale', () => {
  const rule = compact.match(/\.fleet-map-inspector__value\s*\{([^}]*)\}/)?.[1];
  assert.ok(rule);
  assert.match(rule, /color: var\(--ui-text\);/);
  assert.match(rule, /font-weight: 600;/);
  assert.doesNotMatch(rule, /font-size:|line-height:|padding:|margin:/);
});

test('compact truck inspection retains content-sized telemetry and HOS', () => {
  // Only the icons the words repeat are hidden on the vehicle line; a
  // reading itself is never dropped.
  assert.doesNotMatch(compact, /__reading\s*\{[^}]*display: none/);
  // The vehicle line has one owner, and it is not the card's stylesheet.
  assert.match(css, /__telemetry\s*\{[^}]*display: flex;/);
  assert.doesNotMatch(compact, /fleet-map-truck-info/);
  assert.match(
    compact,
    /width: min\(100%,\s*var\(--size-map-compact-inspector\)\);/,
  );
  // The actions live under the detail they act on and say what they do.
  assert.doesNotMatch(compact, /__actions\s*\{[^}]*margin-left/);
  assert.match(
    compact,
    /__actions \.map-action-icon\s*\{[^}]*white-space: nowrap;/,
  );
  assert.doesNotMatch(
    compact,
    /__metric > \.fleet-map-route-info__secondary[^{}]*\{[^}]*display: none;/,
  );
  const telemetry = css.match(/__telemetry\s*\{([^}]*)\}/)?.[1];
  // The vehicle's readings sit on one line and wrap only if they must.
  assert.match(telemetry, /display: flex;/);
  assert.match(telemetry, /flex-wrap: wrap;/);
  // The clocks take the header's own row, full width, under the identity.
  assert.match(
    compact,
    /\.fleet-map-inspector__hours\s*\{[^}]*flex-basis: 100%;/,
  );
  assert.doesNotMatch(
    compact,
    /__telemetry\s*\{[^}]*(?:flex-basis|width): 100%;/,
  );
});

test('truck inspector uses one disclosure control on every screen size', () => {
  const markup = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  assert.match(markup, /fleet-map-mobile-summary__toggle/);
  assert.match(markup, /aria-controls="fleet-map-details"/);
  assert.doesNotMatch(markup, /fleet-map-truck-info__more/);
  assert.match(
    compact,
    /.fleet-map-mobile-summary__toggle\s*\{\s*display: inline-flex;/,
  );
  assert.match(
    compact,
    /@media \(max-width: 767px\)[\s\S]*\.fleet-map-mobile-summary__toggle\s*\{\s*display: inline-flex;/,
  );
  assert.doesNotMatch(css, /fleet-map-reveal/);
});

test('map inspector content updates without reveal or fade animation', () => {
  for (const selector of ['fleet-map-info-reserved', 'fleet-map-info-content'])
    assert.match(css, new RegExp(`\\.${selector}\\s*\\{[^}]*animation: none;`));
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*transition: none;/);
  assert.doesNotMatch(css, /@keyframes fleet-map-(?:reveal|info-fade)/);
  assert.match(
    css,
    /\.fleet-map-route-info\s*\{[^}]*padding: var\(--space-sm\) var\(--space-md\);/,
  );
});

test('selected truck and route panels overlay one stable map with bounded scrolling and no empty reserve', () => {
  assert.match(css, /\.fleet-map-truck-info\s*\{[^}]*min-height: 0;/);
  // "Hours are enough" is off the card, so nothing styles it any more.
  assert.doesNotMatch(css, /__duty/);
  assert.match(
    css,
    /\.fleet-map-route-info\s*\{[^}]*min-height: 0;\s*align-content: start;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__timing > \.arrival-estimate\s*\{[^}]*min-height: 0;/,
  );
  assert.match(
    css,
    /\.fleet-map-stage\s*\{\s*position: relative;\s*flex: 1;\s*min-height: 0;/,
  );
  assert.match(
    css,
    /\.fleet-map-info-reserved\s*\{\s*position: absolute;[^}]*min-height: 0;\s*max-height: 55%;\s*overflow: auto;/,
  );
  assert.match(
    css,
    /\.fleet-map-info-reserved:not\(\.has-selection\):not\(:has\(\.fleet-map-info-reopen\)\)\s*\{\s*display: none;/,
  );
  assert.match(
    css,
    /@media \(max-width: 767px\)[\s\S]*\.fleet-map-info-reserved\s*\{[^}]*max-height: 60%;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-info-reserved\.has-selection\s*\{[^}]*min-height:/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-info-content\s*\{[^}]*position: absolute;/,
  );
  assert.match(
    css,
    /\.fleet-map-info-empty\s*\{[^}]*min-height: calc\(var\(--size-control-touch\) \+ var\(--space-sm\)\);/,
  );
  assert.match(
    css,
    /\.fleet-map-route-info__metric\s*\{[^}]*min-width: var\(--size-route-metric\);/,
  );
  assert.match(css, /\.fleet-map-route-info__next\s*\{[^}]*min-height: 0;/);
  for (const selector of ['fleet-map-truck-info', 'fleet-map-route-info'])
    assert.doesNotMatch(
      css,
      new RegExp(
        // Nothing in a panel is clipped, except the street, which shortens
        // by ellipsis, and a word kept only for a screen reader, which is
        // hidden by being clipped to nothing.
        `\\.${selector}\\s*\\{[^}]*[;{]\\s*height:|` +
          `\\.${selector}(?!__street\\b)[^{}]*\\{` +
          `(?![^}]*clip-path: inset)[^}]*overflow(?:-y)?: (?:hidden|clip);`,
      ),
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
    /\.fleet-map-info-reserved\s*\{[^}]*border-radius: var\(--radius-sm\);[^}]*box-shadow: var\(--shadow-card\);/,
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
  assert.match(
    css,
    /\.fleet-map-info-reserved \.fleet-map-route-info\s*\{\s*border: 0;\s*border-radius: 0;\s*background: transparent;/,
  );
  assert.match(
    css,
    /\.fleet-map-info-reserved \.fleet-map-route-info\s*\{\s*border-top: 1px solid var\(--ui-border-subtle\);/,
  );
  assert.match(
    css,
    /\.fleet-map-info-content\s*\{[^}]*display: flex;[^}]*flex-direction: column;/,
  );
  const mobilePanel = css.match(
    /\.fleet-map-info-reserved\s*\{(\s*max-height: 60%;[^}]+)\}/,
  );
  assert.ok(
    mobilePanel,
    'the mobile inspector inherits continuous shared placement',
  );
  assert.doesNotMatch(mobilePanel[1], /(?:top|left|right):/);
});

test('one map inspector retains hidden content and gives native and future details no popup positioning', () => {
  assert.match(
    css,
    /\.fleet-map-inspector \[hidden\]\s*\{\s*display: none !important;/,
  );
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

test('selected-truck header left-packs identity readings clocks duty and actions without elastic spacers', () => {
  assert.match(
    css,
    /\.fleet-map-truck-info\s*\{[^}]*display: flex;\s*flex-wrap: wrap;/,
  );
  assert.match(
    css,
    /\.fleet-map-truck-info > \*\s*\{\s*flex: 0 1 auto;\s*max-width: 100%;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info\s*\{[^}]*grid-template-columns:[^;]*max-content[^;]*fr/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info__actions\s*\{[^}]*(?:margin-left: auto|justify-content: space-between)/,
  );
  for (const selector of ['telemetry', 'reading'])
    assert.doesNotMatch(
      css,
      new RegExp(
        `\\.fleet-map-truck-info__${selector}\\s*\\{[^}]*(?:background|border):`,
      ),
    );
  assert.match(css, /\.driver-hours-panel\s*\{\s*display: grid;/);
  // A reading is a word and a value on one baseline - no frame, and no rule
  // between it and the next, which is what the towers had.
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info__reading[^{]*\{[^}]*border-left:/,
  );
  assert.doesNotMatch(css, /--hos-dial-size: var\(--size-map-hos-dial\)/);
});

test('HOS circles keep the same compact gap instead of stretching across wide or mobile headers', () => {
  assert.match(css, /\.driver-hours\s*\{[^}]*min-width: 0;\s*max-width: 100%;/);
  // The clocks read as one line of text, not as a row of dials.
  // Said once, on the group that holds the clocks - not on the line and
  // then again, differently, on the group.
  assert.match(compact, /__clocks\s*\{[^}]*--hos-display: flex;/);
  assert.doesNotMatch(compact, /__hours\s*\{[^}]*--hos-/);
  assert.match(
    compact,
    /\.fleet-map-inspector__clocks\s*\{[^}]*--hos-clock-min-width: 0;/,
  );
  assert.doesNotMatch(compact, /__hours\s*\{[^}]*--hos-dial-size/);
  // How a text clock is drawn belongs to the component, not to this page.
  assert.doesNotMatch(compact, /\.driver-hours__/);
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info[^{}]*\.driver-hours\s*\{[^}]*justify-content: space-between;/,
  );
  const spacious = css.slice(
    css.indexOf('@media (min-width: 1800px)'),
    css.indexOf(
      '@media (max-width: 767px)',
      css.indexOf('@media (min-width: 1800px)'),
    ),
  );
  assert.doesNotMatch(
    spacious,
    /\.fleet-map-truck-info__telemetry\s*\{[^}]*grid-template-columns: repeat\(3,\s*minmax/,
  );
});

test('desktop and mobile actions wrap without reserving blank reference rows', () => {
  assert.doesNotMatch(css, /fleet-map-truck-info__buttons/);
  assert.doesNotMatch(
    css,
    /min-height: (?:calc\()?var\(--size-map-route-address-stacked-min\)/,
  );
});

test('intermediate stop distance is inline with the visit heading instead of another stacked metric', () => {
  assert.match(
    compact,
    /\.fleet-map-route-info__visit-heading\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;[^}]*align-items: baseline;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__distance\s*\{[^}]*display: inline-flex;[^}]*align-items: baseline;[^}]*font-size: var\(--type-body\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__distance small\s*\{[^}]*font: inherit;[^}]*font-weight: 400;/,
  );
  assert.match(
    compact,
    /\.has-final-stop \.fleet-map-route-info__distance,[^{}]*\.is-awaiting-route \.fleet-map-route-info__distance\s*\{\s*display: none;/,
  );
});

test('mobile keeps one compact row until Details is selected', () => {
  const mobile = compact.slice(compact.indexOf('@media (max-width: 767px)'));
  // On a phone the top line is the unit and the controls; the distance
  // rides the clocks line below with the load it belongs to.
  for (const [selector, column] of [
    ['fleet-map-inspector__title', 1],
    ['fleet-map-inspector__controls', 2],
  ])
    assert.match(
      mobile,
      new RegExp(
        `${selector}\\s*\\{\\s*grid-column: ${column};\\s*grid-row: 1;`,
      ),
    );
  assert.match(
    mobile,
    /\.is-mobile-collapsed[\s\S]*\.fleet-map-info-content\s*\{\s*display: none;/,
  );
  assert.match(
    mobile,
    /\.fleet-map-inspector__desktop-title\s*\{\s*display: block;/,
  );
});

test('next-stop distance rides the clocks line at every width', () => {
  assert.match(
    compact,
    /\.fleet-map-mobile-summary__remaining\s*\{\s*display: flex;/,
  );
  const mobile = compact.slice(compact.indexOf('@media (max-width: 767px)'));
  // The load leads the clocks line rather than sitting in a chip of its
  // own, and its number is labelled at every width.
  assert.doesNotMatch(compact, /__remaining\s*\{[^}]*margin-inline-start/);
  // One column on a phone. It asked for this with flex-basis on the children
  // of a grid, which does nothing: the three stayed abreast and the clocks
  // folded into a tower.
  assert.match(
    mobile,
    /\.fleet-map-inspector__hours\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\);/,
  );
  assert.doesNotMatch(compact, /flex-basis: 100%;\s*min-inline-size: 0;/);
  assert.match(compact, /__label\s*\{\s*display: inline;/);
  assert.doesNotMatch(compact, /__label\s*\{\s*display: none;/);
  assert.doesNotMatch(mobile, /is-mobile-collapsed[^{}]*__remaining/);
  // Opening the card must not move it: no width re-columns the header.
  assert.doesNotMatch(mobile, /is-mobile-expanded/);
});

test('truck metadata stays aligned and disclosure does not restyle the primary summary', () => {
  assert.match(
    compact,
    /\.fleet-map-inspector__driver,\s*[^{}]*\.fleet-map-inspector__trailer\s*\{[^}]*color: var\(--ui-text-secondary\);/,
  );
  assert.match(compact, /__trailer \+ [^{]*__driver::before\s*\{\s*content:/);
  assert.match(css, /\.fleet-map-truck-info\s*\{[^}]*align-items: baseline;/);
  assert.doesNotMatch(compact, /__duty/);
});

test('load details uses an accessible header icon with shared action sizing', () => {
  const markup = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  const link = markup.match(/<a class="btn map-action-icon"[\s\S]*?<\/a>/)?.[0];
  assert.ok(link);
  assert.match(link, /href="@\(SelectedDispatchId is/);
  assert.match(link, /title="@\("Route & load details"\)"/);
  assert.match(link, /aria-label="@\("Route & load details"\)"/);
  assert.match(link, /aria-disabled="@\(SelectedDispatchId is null/);
  assert.match(link, /<ActionIcon Kind="external-link"\s*\/>/);
  assert.ok(
    markup.indexOf('fleet-map-inspector__actions') < markup.indexOf(link),
  );
  // The actions sit under the detail they act on, below the header's own
  // controls rather than crowded into them.
  assert.ok(
    markup.indexOf('fleet-map-inspector__controls') < markup.indexOf(link),
  );
  assert.doesNotMatch(markup, /fleet-map-truck-info__load-link/);
  assert.match(link, /<span>Open load<\/span>/);
  assert.match(
    compact,
    /__actions \.map-action-icon\s*\{[^}]*display: inline-flex;[^}]*gap: var\(--space-xs\);/,
  );
  // A labelled action is as wide as its words; only its height is shared
  // with the other controls, and it grows for a finger.
  assert.match(
    compact,
    /__actions \.map-action-icon\s*\{[^}]*min-height: var\(--size-control-touch\);/,
  );
  assert.doesNotMatch(compact, /__actions \.map-action-icon\s*\{[^}]*width:/);
});

test('map key stays over the map and uses the actual fixed station comparison palette', () => {
  assert.match(css, /\.fleet-map-key\s*\{\s*position: absolute;/);
  assert.match(
    css,
    /\.fleet-map-key__scale > i\s*\{[^}]*background: linear-gradient\(to right,\s*var\(--ui-map-price-low\),\s*var\(--ui-map-price-middle\),\s*var\(--ui-map-price-high\)\);/,
  );
  assert.match(
    css,
    /\.fleet-map-key__missing > i\s*\{[^}]*background: var\(--ui-map-price-unavailable\);/,
  );
  assert.match(
    css,
    /\.fleet-map-key__planned\s*\{[^}]*color: var\(--ui-navigation-text\);/,
  );
  assert.match(
    css,
    /\.fleet-map-key__stop\s*\{[^}]*border-radius: 50%;[^}]*background: var\(--ui-map-route-current\);[^}]*color: var\(--ui-on-accent\);/,
  );
});

test('the planned stop has its own title size, and no dials or illustration are left', () => {
  // The illustration left the map with the panel it stood in.
  assert.doesNotMatch(css, /truck-illustration/);
  assert.match(
    css,
    /\.fleet-station-popup--planned > \.fleet-station-popup__title\s*\{[^}]*font-size: var\(--type-subtitle\);/,
  );
  // The dials that used to be sized here are gone from the map.
  assert.doesNotMatch(css, /fleet-fuel-visit__dial/);
});

test('route streets ellipsize without a location icon taking column width', () => {
  assert.match(
    css,
    /__street\s*\{[^}]*overflow: hidden;[^}]*text-overflow: ellipsis;[^}]*white-space: nowrap;/,
  );
  assert.doesNotMatch(css, /__address-icon/);
});
