import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'components/driver-status'; @use 'pages/fleet-map/truck-info'; @use 'pages/fleet-map/route-info'; @use 'pages/fleet-map/details'; @use 'pages/fleet-map/layout'; @use 'pages/fleet-map/popup-content';", {loadPaths}).css;

test('only the selected header truck illustration faces right', () => {
  assert.match(css, /\.fleet-map-truck-info__illustration > svg\s*\{[^}]*transform: scaleX\(-1\);/);
});

test('selected truck and route panels overlay one stable map with bounded scrolling and no empty reserve', () => {
  assert.match(css, /\.fleet-map-truck-info\s*\{[^}]*min-height: var\(--size-map-truck-info-min\);/);
  assert.match(css, /\.fleet-map-truck-info__duty \.driver-duty\s*\{\s*align-content: start;/);
  assert.doesNotMatch(css, /\.fleet-map-truck-info__duty \.driver-duty\s*\{[^}]*min-height:/);
  assert.match(css, /\.fleet-map-route-info\s*\{[^}]*min-height: var\(--size-map-route-info-min\);\s*align-content: start;/);
  assert.match(css, /\.fleet-map-route-info > \.arrival-estimate\s*\{[^}]*min-height: var\(--size-map-route-estimate-min\);\s*align-content: start;/);
  assert.match(css, /\.fleet-map-stage\s*\{\s*position: relative;\s*flex: 1;\s*min-height: 0;/);
  assert.match(css, /\.fleet-map-info-reserved\s*\{\s*position: absolute;[^}]*min-height: 0;\s*max-height: 55%;\s*overflow: auto;/);
  assert.match(css, /\.fleet-map-info-reserved:not\(\.has-selection\):not\(:has\(\.fleet-map-info-reopen\)\)\s*\{\s*display: none;/);
  assert.match(css, /@media \(max-width: 767px\)[\s\S]*\.fleet-map-info-reserved\s*\{[^}]*max-height: 60%;/);
  assert.doesNotMatch(css, /\.fleet-map-info-reserved\.has-selection\s*\{[^}]*min-height:/);
  assert.doesNotMatch(css, /\.fleet-map-info-reserved\.is-expanded \.fleet-map-info-content\s*\{[^}]*position: absolute;/);
  assert.match(css, /\.fleet-map-info-empty\s*\{[^}]*min-height: calc\(var\(--size-control-touch\) \+ var\(--space-sm\)\);/);
  assert.match(css, /\.fleet-map-route-info__metric\s*\{[^}]*min-width: var\(--size-route-metric\);/);
  assert.match(css, /\.fleet-map-route-info__next\s*\{[^}]*min-height: var\(--size-map-route-address-min\);/);
  for (const selector of ['fleet-map-truck-info', 'fleet-map-route-info'])
    assert.doesNotMatch(css, new RegExp(`\\.${selector}\\s*\\{[^}]*[;{]\\s*height:|\\.${selector}[^{}]*\\{[^}]*overflow(?:-y)?: (?:hidden|clip);`));
});

test('map information caps its top gap by actual side clearance rather than viewport breakpoints', () => {
  assert.match(css, /\.fleet-map-info-reserved\s*\{\s*position: absolute;\s*top: min\(var\(--space-md\), var\(--map-inspector-side-gap, 0px\)\);\s*left: 0;\s*right: 0;/);
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*width: min\(100%, var\(--size-map-inspector\)\);\s*margin-inline: auto;/);
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*border-radius: var\(--radius-sm\);[^}]*box-shadow: var\(--shadow-card\);/);
  assert.doesNotMatch(css, /\.fleet-map-info-reserved\s*\{[^}]*(?:scrollbar-gutter|backdrop-filter):/);
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*background: var\(--ui-surface\);/);
  assert.match(css, /\.fleet-map-info-content\s*\{[^}]*gap: 0;/);
  assert.match(css, /\.fleet-map-info-reserved \.fleet-map-truck-info, \.fleet-map-info-reserved \.fleet-map-route-info\s*\{\s*border: 0;\s*border-radius: 0;\s*background: transparent;/);
  assert.match(css, /\.fleet-map-info-reserved \.fleet-map-route-info\s*\{\s*border-top: 1px solid var\(--ui-border-subtle\);/);
  assert.match(css, /\.fleet-map-info-reserved\.is-expanded \.fleet-map-info-content\s*\{\s*display: flex;\s*padding: 0;/);
  const mobilePanel = css.match(/\.fleet-map-info-reserved\s*\{(\s*max-height: 60%;[^}]+)\}/);
  assert.ok(mobilePanel, 'the mobile inspector inherits continuous shared placement');
  assert.doesNotMatch(mobilePanel[1], /(?:top|left|right):/);
});

test('truck action buttons wrap naturally instead of forcing two columns', () => {
  assert.match(css, /\.fleet-map-truck-info__buttons\s*\{\s*display: flex;\s*flex-wrap: wrap;/);
  assert.doesNotMatch(css, /\.fleet-map-truck-info__buttons\s*\{[^}]*grid-template-columns:/);
});

test('one map inspector retains hidden content and gives native and future details no popup positioning', () => {
  assert.match(css, /\.fleet-map-inspector \[hidden\]\s*\{\s*display: none !important;/);
  assert.match(css, /\.fleet-map-inspector__header\s*\{\s*position: sticky;\s*top: 0;[^}]*display: flex;/);
  assert.match(css, /\.fleet-map-inspector__next\s*\{\s*position: static;\s*width: auto;\s*max-width: none;\s*border: 0;\s*box-shadow: none;/);
  assert.doesNotMatch(css, /\.fleet-map-inspector__native\s*\{[^}]*(?:position|bottom|right|max-width):/);
  assert.match(css, /\.fleet-map-inspector__close\s*\{[^}]*width: var\(--size-control-touch\);/);
});

test('selected-truck header left-packs identity readings clocks duty and actions without elastic spacers', () => {
  assert.match(css, /\.fleet-map-truck-info\s*\{\s*display: flex;\s*flex-wrap: wrap;\s*justify-content: flex-start;/);
  assert.match(css, /\.fleet-map-truck-info > \*\s*\{\s*flex: 0 1 auto;\s*max-width: 100%;/);
  assert.doesNotMatch(css, /\.fleet-map-truck-info\s*\{[^}]*grid-template-columns:[^;]*max-content[^;]*fr/);
  assert.match(css, /\.fleet-map-truck-info__hours-label\s*\{[^}]*font-size: var\(--type-small\);/);
  assert.match(css, /\.fleet-map-truck-info__actions\s*\{[^}]*align-items: flex-start;/);
  assert.doesNotMatch(css, /\.fleet-map-truck-info__actions\s*\{[^}]*(?:margin-left: auto|justify-content: space-between)/);
  assert.match(css, /\.fleet-map-truck-info__duty \.driver-next-recap\s*\{[^}]*min-height: 1.4em;/);
  for (const selector of ['telemetry', 'reading'])
    assert.doesNotMatch(css, new RegExp(`\\.fleet-map-truck-info__${selector}\\s*\\{[^}]*(?:background|border):`));
  assert.match(css, /\.driver-hours-panel\s*\{\s*display: grid;/);
  assert.doesNotMatch(css, /\.driver-duty[^{}]*\{[^}]*display: none;/);
  assert.match(css, /\.fleet-map-truck-info__reading\s*\{[^}]*border-left: 1px solid var\(--ui-border-subtle\);/);
  assert.match(css, /@media \(min-width: 1800px\)[\s\S]*--hos-dial-size: var\(--size-map-hos-dial\);/);
});

test('mobile selected-truck header keeps full-width hours and compact labeled readings', () => {
  const mobile = css.slice(css.indexOf('@media (max-width: 767px)'));
  assert.match(mobile, /grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\);/);
  assert.match(mobile, /\.fleet-map-truck-info__identity, \.fleet-map-truck-info__telemetry, \.fleet-map-truck-info__actions, \.fleet-map-truck-info__hours\s*\{\s*grid-column: 1\s*\/\s*-1;/);
  assert.match(mobile, /\.fleet-map-truck-info__hours, \.fleet-map-truck-info__hours > \.driver-hours-panel\s*\{\s*width: 100%;/);
  assert.match(mobile, /\.fleet-map-truck-info__hours\s*\{\s*--hos-dial-size: var\(--size-hos-dial-compact\);/);
  assert.match(mobile, /\.fleet-map-truck-info__telemetry\s*\{\s*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\);/);
  assert.match(mobile, /\.fleet-map-truck-info__actions\s*\{\s*flex-direction: row;\s*flex-wrap: wrap;/);
  assert.match(mobile, /\.fleet-map-truck-info__buttons\s*\{\s*display: flex;\s*flex-wrap: wrap;\s*min-width: 0;\s*max-width: 100%;/);
  assert.match(css, /@media \(max-width: 1100px\)[\s\S]*\.fleet-map-route-info__metric\s*\{\s*grid-row: 1;\s*min-width: 0;/);
});

test('HOS circles keep the same compact gap instead of stretching across wide or mobile headers', () => {
  assert.match(css, /\.fleet-map-truck-info\s*\{[^}]*--hos-gap: var\(--space-sm\);/);
  assert.match(css, /\.fleet-map-truck-info__hours\s*\{[^}]*--hos-wrap: wrap;/);
  assert.match(css, /\.fleet-map-truck-info__hours > \.driver-hours-panel\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/);
  assert.match(css, /\.driver-hours\s*\{[^}]*min-width: 0;\s*max-width: 100%;/);
  assert.match(css, /\.fleet-map-truck-info__hours\s*\{[^}]*--hos-clock-min-width: 5ch;/);
  assert.doesNotMatch(css, /\.fleet-map-truck-info[^{}]*\.driver-hours\s*\{[^}]*justify-content: space-between;/);
  const spacious = css.slice(css.indexOf('@media (min-width: 1800px)'), css.indexOf('@media (max-width: 767px)', css.indexOf('@media (min-width: 1800px)')));
  assert.doesNotMatch(spacious, /\.fleet-map-truck-info__telemetry\s*\{[^}]*grid-template-columns: repeat\(3, minmax/);
});

test('cramped route details use full rows according to root-font-relative available width', () => {
  assert.match(css, /\.fleet-map-info-content\s*\{\s*container: map-truck-inspection\s*\/\s*inline-size;/);
  assert.match(css, /@container map-truck-inspection \(width < 20rem\)\s*\{\s*\.fleet-map-route-info > \.fleet-map-route-info__load,\s*\.fleet-map-route-info > \.fleet-map-route-info__next,\s*\.fleet-map-route-info > \.fleet-map-route-info__appointment,\s*\.fleet-map-route-info > \.arrival-estimate\s*\{\s*grid-column: 1\s*\/\s*-1;\s*grid-row: auto;/);
});

test('intermediate desktop actions leave room for duty text and mobile reserves the wrapped reference row only', () => {
  const intermediate = css.slice(css.indexOf('@media (min-width: 1400px)'), css.indexOf('@media (min-width: 1800px)'));
  assert.doesNotMatch(intermediate, /\.fleet-map-truck-info__buttons\s*\{[^}]*display: flex;/);
  assert.match(css, /\.fleet-map-truck-info__buttons\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;/);
  assert.match(css, /@media \(min-width: 1800px\)[\s\S]*?\.fleet-map-truck-info__buttons\s*\{\s*display: flex;/);
  assert.match(css, /@media \(max-width: 767px\)\s*\{\s*\.fleet-map-route-info > \.fleet-map-route-info__load, \.fleet-map-route-info__next\s*\{\s*min-height: calc\(var\(--size-map-route-address-stacked-min\) \+ var\(--space-xl\)\);/);
});

test('route summary separates six groups while retaining total distance and both address lines', () => {
  assert.match(css, /\.fleet-map-route-info\s*\{\s*display: flex;\s*flex-wrap: wrap;\s*justify-content: flex-start;/);
  assert.match(css, /\.fleet-map-route-info > \*\s*\{\s*flex: 0 1 auto;\s*max-width: 100%;/);
  assert.doesNotMatch(css, /\.fleet-map-route-info\s*\{[^}]*grid-template-columns:[^;]*max-content/);
  assert.match(css, /\.fleet-map-route-info__load-reference\s*\{\s*display: flex;\s*flex-wrap: wrap;/);
  assert.match(css, /\.fleet-map-route-info__metric strong\s*\{\s*font-size: var\(--type-title\);/);
  assert.match(css, /\.fleet-map-route-info__load \.fleet-map-route-info__secondary\s*\{\s*font-size: var\(--type-caption\);/);
  assert.match(css, /\.fleet-map-route-info__copy-address\s*\{\s*display: flex;\s*flex-direction: column;/);
  assert.match(css, /\.fleet-map-route-info > \.fleet-map-route-info__next \.fleet-map-route-info__copy-address\s*\{\s*flex-direction: row;/);
  assert.match(css, /\.fleet-map-route-info__address-lines\s*\{\s*display: grid;/);
  assert.match(css, /@media \(max-width: 1100px\)[\s\S]*\.fleet-map-route-info > \.fleet-map-route-info__load\s*\{[^}]*min-height: var\(--size-map-route-address-stacked-min\);/);
  assert.doesNotMatch(css, /(?:^|\n)\.fleet-map-route-info__load\s*\{[^}]*(?:grid-column|min-height|border-right|flex-direction):/);
  assert.match(css, /@media \(max-width: 1100px\)[\s\S]*\.fleet-map-route-info \.stop-hours__cycle \.stop-hours__value\s*\{\s*white-space: nowrap;/);
});

test('map key stays over the map and uses the actual fixed station comparison palette', () => {
  assert.match(css, /\.fleet-map-key\s*\{\s*position: absolute;/);
  assert.match(css, /\.fleet-map-key__scale > i\s*\{[^}]*background: linear-gradient\(to right, var\(--ui-map-price-low\), var\(--ui-map-price-middle\), var\(--ui-map-price-high\)\);/);
  assert.match(css, /\.fleet-map-key__missing > i\s*\{[^}]*background: var\(--ui-map-price-unavailable\);/);
  assert.match(css, /\.fleet-map-key__planned\s*\{[^}]*color: var\(--ui-navigation-text\);/);
  assert.match(css, /\.fleet-map-key__stop\s*\{[^}]*border-radius: 50%;[^}]*background: var\(--ui-map-route-current\);[^}]*color: var\(--ui-on-accent\);/);
});

test('selected header illustration and planned-popup gauges have their own presentation without resizing editor gauges', () => {
  assert.match(css, /\.fleet-map-truck-info \.truck-illustration__trailer, \.fleet-map-truck-info \.truck-illustration__cab\s*\{\s*fill: var\(--ui-action\);/);
  assert.match(css, /\.fleet-station-popup--planned \.fleet-fuel-visit__dial\s*\{[^}]*width: var\(--size-map-fuel-dial\);\s*height: var\(--size-map-fuel-dial\);/);
  assert.match(css, /\.fleet-station-popup--planned \.fleet-station-popup__title\s*\{[^}]*font-size: var\(--type-heading\);/);
  assert.match(css, /\.fleet-fuel-visit__dial\s*\{\s*--hos-dial-size: var\(--size-fuel-dial\);/);
});

test('mobile multi-visit fuel cards tighten only level padding while keeping full-size single and desktop gauges', () => {
  assert.match(css, /@media \(max-width: 767px\)\s*\{\s*\.fleet-station-popup--planned:not\(\.fleet-station-popup--single\) \.fleet-fuel-visit__levels\s*\{\s*padding-block: var\(--space-xs\);/);
  assert.match(css, /\.fleet-station-popup--planned \.fleet-fuel-visit__levels\s*\{[^}]*padding-block: var\(--space-sm\);/);
  assert.match(css, /\.fleet-station-popup--planned \.fleet-fuel-visit__dial\s*\{[^}]*width: var\(--size-map-fuel-dial\);\s*height: var\(--size-map-fuel-dial\);/);
});
