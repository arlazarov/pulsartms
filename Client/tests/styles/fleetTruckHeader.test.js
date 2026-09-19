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
  assert.doesNotMatch(compact, /__reading[^{}]*\{[^}]*display: none/);
  assert.match(compact, /__telemetry\s*\{[^}]*display: grid;/);
  assert.match(
    compact,
    /width: min\(100%,\s*var\(--size-map-compact-inspector\)\);/,
  );
  assert.match(compact, /__actions\s*\{[^}]*margin-left: 0;/);
  assert.match(
    css,
    /__telemetry\s*\{[^}]*grid-template-columns: repeat\(3,\s*minmax\(0,\s*max-content\)\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-inspector\[data-inspector-mode=truck\] \.fleet-map-truck-info__hours\s*\{[^}]*--hos-dial-size: var\(--size-control-touch\);/,
  );
  assert.doesNotMatch(
    compact,
    /__metric > \.fleet-map-route-info__secondary[^{}]*\{[^}]*display: none;/,
  );
  const telemetry = compact.match(/__telemetry\s*\{([^}]*)\}/)?.[1];
  const hours = compact.match(/__hours\s*\{([^}]*)\}/)?.[1];
  assert.match(telemetry, /repeat\(4, minmax\(0, max-content\)\)/);
  assert.match(hours, /width: auto;/);
  assert.doesNotMatch(
    compact,
    /__(?:telemetry|hours)\s*\{[^}]*(?:flex-basis|width): 100%;/,
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

test('Remaining keeps three rows and data loading does not resize the metric column', () => {
  assert.match(compact, /scrollbar-gutter: stable;/);
  assert.match(
    compact,
    /\.fleet-map-route-info__metric\s*\{[^}]*grid-template-columns: minmax\(0,\s*1fr\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__metric > \.fleet-map-route-info__secondary\s*\{[^}]*font-size: var\(--type-body\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__metric small\s*\{[^}]*font-size: inherit;/,
  );
  assert.match(
    compact,
    /grid-template-columns: var\(--size-route-metric\) minmax\(0,\s*1fr\) minmax\(0,\s*1fr\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__load-placeholder\s*\{[^}]*font-size: var\(--type-lead\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__timing > \.arrival-estimate:has\(> \.arrival-estimate\) > \.fleet-map-route-info__eta-placeholder\s*\{[^}]*display: none;/,
  );
});

test('delivery remains in the timing column during preview and reference loading', () => {
  assert.doesNotMatch(
    compact,
    /(?:has-next-delivery|is-awaiting-route)[^{}]*__delivery/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-route-info__delivery\s*\{[^}]*display: none;/,
  );
  assert.match(
    compact,
    /__next-appointment\s*\{[^}]*display: flex;[^}]*align-items: baseline;/,
  );
});

test('load references use assigned grid slots instead of content-dependent flex wrapping', () => {
  assert.match(
    compact,
    /\.fleet-map-route-info__load\s*\{[^}]*display: grid;[^}]*grid-template-columns: max-content minmax\(0,\s*1fr\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__load-reference\s*\{\s*display: contents;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__load-placeholder\s*\{\s*grid-column: 2;\s*grid-row: 1;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__total\s*\{\s*grid-column: 4;\s*grid-row: 1;/,
  );
});

test('the stop facility remains a readable supporting value without widening the inspector', () => {
  assert.match(
    compact,
    /__address-lines > \.fleet-map-route-info__facility\s*\{\s*color: var\(--ui-text\);\s*font-weight: 600;\s*overflow-wrap: anywhere;/,
  );
  assert.doesNotMatch(
    compact,
    /__facility\s*\{[^}]*(?:font-size:|white-space: nowrap|text-overflow: ellipsis)/,
  );
});

test('distance and visit groups remain independent of expanded forecast row heights', () => {
  for (const group of ['distances', 'visit', 'timing'])
    assert.match(
      compact,
      new RegExp(
        `\\.fleet-map-route-info__${group}[^{}]*\\{[^}]*align-content: start;`,
      ),
    );
  assert.doesNotMatch(compact, /grid-row:[^;]*span 2;/);
  assert.doesNotMatch(
    compact,
    /\.fleet-map-route-info__visit[^{}]*\{[^}]*min-height: (?!0;)/,
  );
  assert.match(compact, /--arrival-font-size: var\(--type-body\);/);
  assert.match(compact, /--arrival-detail-font-size: var\(--type-small\);/);
});

test('map inspector content updates without reveal or fade animation', () => {
  for (const selector of ['fleet-map-info-reserved', 'fleet-map-info-content'])
    assert.match(css, new RegExp(`\\.${selector}\\s*\\{[^}]*animation: none;`));
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*transition: none;/);
  assert.doesNotMatch(css, /@keyframes fleet-map-(?:reveal|info-fade)/);
  for (const selector of ['fleet-map-truck-info', 'fleet-map-route-info'])
    assert.match(
      css,
      new RegExp(
        `\\.${selector}\\s*\\{[^}]*padding: var\\(--space-sm\\) var\\(--space-md\\);`,
      ),
    );
});

test('only the selected header truck illustration faces right', () => {
  assert.match(
    css,
    /\.fleet-map-truck-info__illustration > svg\s*\{[^}]*transform: scaleX\(-1\);/,
  );
});

test('selected truck and route panels overlay one stable map with bounded scrolling and no empty reserve', () => {
  assert.match(css, /\.fleet-map-truck-info\s*\{[^}]*min-height: 0;/);
  assert.match(
    css,
    /\.fleet-map-truck-info__duty \.driver-duty\s*\{\s*align-content: start;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info__duty \.driver-duty\s*\{[^}]*min-height:/,
  );
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
        `\\.${selector}\\s*\\{[^}]*[;{]\\s*height:|` +
          `\\.${selector}(?!__street\\b)[^{}]*\\{` +
          `[^}]*overflow(?:-y)?: (?:hidden|clip);`,
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
    /\.fleet-map-info-reserved \.fleet-map-truck-info,\s*\.fleet-map-info-reserved \.fleet-map-route-info\s*\{\s*border: 0;\s*border-radius: 0;\s*background: transparent;/,
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

test('truck action buttons wrap naturally instead of forcing two columns', () => {
  assert.match(
    css,
    /\.fleet-map-truck-info__buttons\s*\{\s*display: flex;\s*flex-wrap: wrap;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info__buttons\s*\{[^}]*grid-template-columns:/,
  );
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
    /\.fleet-map-truck-info\s*\{\s*display: flex;\s*flex-wrap: wrap;\s*justify-content: flex-start;/,
  );
  assert.match(
    css,
    /\.fleet-map-truck-info > \*\s*\{\s*flex: 0 1 auto;\s*max-width: 100%;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info\s*\{[^}]*grid-template-columns:[^;]*max-content[^;]*fr/,
  );
  assert.match(
    css,
    /\.fleet-map-truck-info__actions\s*\{[^}]*align-items: flex-start;/,
  );
  assert.doesNotMatch(
    css,
    /\.fleet-map-truck-info__actions\s*\{[^}]*(?:margin-left: auto|justify-content: space-between)/,
  );
  assert.doesNotMatch(css, /\.fleet-map-truck-info__duty \.driver-next-recap/);
  for (const selector of ['telemetry', 'reading'])
    assert.doesNotMatch(
      css,
      new RegExp(
        `\\.fleet-map-truck-info__${selector}\\s*\\{[^}]*(?:background|border):`,
      ),
    );
  assert.match(css, /\.driver-hours-panel\s*\{\s*display: grid;/);
  assert.doesNotMatch(css, /\.driver-duty[^{}]*\{[^}]*display: none;/);
  assert.match(
    css,
    /\.fleet-map-truck-info__reading\s*\{[^}]*border-left: 1px solid var\(--ui-border-subtle\);/,
  );
  assert.doesNotMatch(css, /--hos-dial-size: var\(--size-map-hos-dial\)/);
});

test('mobile selected-truck header keeps full-width hours and compact labeled readings', () => {
  const mobile = css.slice(css.indexOf('@media (max-width: 767px)'));
  assert.match(
    mobile,
    /grid-template-columns: minmax\(0,\s*1fr\) minmax\(0,\s*1fr\);/,
  );
  assert.match(
    mobile,
    /\.fleet-map-truck-info__identity,\s*\.fleet-map-truck-info__telemetry,\s*\.fleet-map-truck-info__actions,\s*\.fleet-map-truck-info__hours\s*\{\s*grid-column: 1\s*\/\s*-1;/,
  );
  assert.match(
    mobile,
    /\.fleet-map-truck-info__hours,\s*\.fleet-map-truck-info__hours > \.driver-hours-panel\s*\{\s*width: 100%;/,
  );
  assert.match(
    mobile,
    /\.fleet-map-truck-info__hours\s*\{\s*--hos-dial-size: var\(--size-hos-dial-compact\);/,
  );
  assert.match(
    mobile,
    /\.fleet-map-truck-info__telemetry\s*\{\s*grid-template-columns: repeat\(3,\s*minmax\(0,\s*1fr\)\);/,
  );
  assert.match(
    mobile,
    /\.fleet-map-truck-info__actions\s*\{\s*flex-direction: row;\s*flex-wrap: wrap;/,
  );
  assert.match(
    mobile,
    /\.fleet-map-truck-info__buttons\s*\{\s*display: flex;\s*flex-wrap: wrap;\s*min-width: 0;\s*max-width: 100%;/,
  );
  assert.match(
    compact,
    /@container map-truck-inspection \(width < 40rem\)[\s\S]*\.fleet-map-route-info__distances\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;/,
  );
});

test('HOS circles keep the same compact gap instead of stretching across wide or mobile headers', () => {
  assert.match(
    css,
    /\.fleet-map-truck-info\s*\{[^}]*--hos-gap: var\(--space-sm\);/,
  );
  assert.match(css, /\.fleet-map-truck-info__hours\s*\{[^}]*--hos-wrap: wrap;/);
  assert.match(
    css,
    /\.fleet-map-truck-info__hours > \.driver-hours-panel\s*\{\s*grid-template-columns: minmax\(0,\s*1fr\);/,
  );
  assert.match(css, /\.driver-hours\s*\{[^}]*min-width: 0;\s*max-width: 100%;/);
  assert.match(
    css,
    /\.fleet-map-truck-info__hours\s*\{[^}]*--hos-clock-min-width: 5ch;/,
  );
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

test('cramped route details use full rows according to root-font-relative available width', () => {
  assert.match(
    css,
    /\.fleet-map-info-content\s*\{\s*container: map-truck-inspection\s*\/\s*inline-size;/,
  );
  assert.match(
    compact,
    /@container map-truck-inspection \(width < 40rem\)[\s\S]*\.fleet-map-route-info > \.fleet-map-route-info__distances,[^{}]*\.fleet-map-route-info > \.fleet-map-route-info__visit,[^{}]*\.fleet-map-route-info > \.fleet-map-route-info__timing\s*\{\s*grid-column: 1;\s*grid-row: auto;/,
  );
});

test('desktop and mobile actions wrap without reserving blank reference rows', () => {
  assert.match(
    css,
    /\.fleet-map-truck-info__buttons\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;/,
  );
  assert.doesNotMatch(
    css,
    /min-height: (?:calc\()?var\(--size-map-route-address-stacked-min\)/,
  );
});

test('route summary groups load distances next visit and ETA without changing its type scale', () => {
  assert.match(compact, /\.fleet-map-route-info\s*\{[^}]*align-items: start;/);
  assert.match(
    compact,
    /\.fleet-map-route-info > \.fleet-map-route-info__load\s*\{[^}]*display: grid;[^}]*grid-column: 1\s*\/\s*-1;[^}]*grid-row: 1;/,
  );
  assert.match(
    css,
    /\.fleet-map-route-info__load-reference\s*\{\s*display: flex;\s*flex-wrap: wrap;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__metric strong\s*\{[^}]*font-size: var\(--type-body\);[^}]*font-weight: 600;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__metric\s*\{[^}]*grid-template-columns: minmax\(0,\s*1fr\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__load \.fleet-map-route-info__secondary[^{}]*\{\s*font-size: var\(--type-small\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__copy-address\s*\{\s*flex-direction: row;/,
  );
  assert.match(
    css,
    /\.fleet-map-route-info__address-lines\s*\{\s*display: grid;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__address-lines > span\s*\{[^}]*font-size: var\(--type-body\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info__load \.fleet-map-inspector__value\s*\{[^}]*font-size: var\(--type-body\);/,
  );
  assert.match(
    css,
    /\.fleet-map-truck-info__reading strong small\s*\{[^}]*font: inherit;[^}]*font-weight: 400;/,
  );
  assert.match(
    compact,
    /\.stop-hours__road > \.stop-hours__label\s*\{\s*font-size: var\(--type-small\);/,
  );
  assert.match(
    compact,
    /\.stop-hours__road\s*\{\s*display: var\(--stop-hours-road-display, flex\);\s*flex-wrap: wrap;\s*align-items: baseline;/,
  );
  assert.doesNotMatch(
    compact.slice(0, compact.indexOf('@media (max-width: 767px)')),
    /:not\(\.is-expanded\)/,
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
  for (const [selector, column] of [
    ['fleet-map-inspector__title', 1],
    ['fleet-map-mobile-summary__remaining', 2],
    ['fleet-map-inspector__controls', 3],
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

test('next-stop distance stays visible on desktop and centered on phones', () => {
  assert.match(
    compact,
    /\.fleet-map-mobile-summary__remaining\s*\{\s*display: grid;/,
  );
  const mobile = compact.slice(compact.indexOf('@media (max-width: 767px)'));
  assert.match(
    mobile,
    /__remaining\s*\{[^}]*display: grid;[^}]*justify-items: center;[^}]*inline-size: 8ch;[^}]*font-size: var\(--type-body\);/,
  );
  assert.doesNotMatch(mobile, /is-mobile-collapsed[^{}]*__remaining/);
  assert.match(
    mobile,
    /@container map-truck-header \(width < 20rem\)[\s\S]*__remaining\s*\{[^}]*grid-column: 1\s*\/\s*-1;[^}]*justify-self: center;/,
  );
});

test('truck metadata stays aligned and disclosure does not restyle the primary summary', () => {
  assert.match(
    compact,
    /\.fleet-map-inspector__driver,\s*[^{}]*\.fleet-map-inspector__trailer\s*\{[^}]*font-size: var\(--type-small\);/,
  );
  assert.match(
    compact,
    /\.fleet-map-truck-info\s*\{[^}]*align-items: flex-start;/,
  );
  assert.match(compact, /\.fleet-map-truck-info__duty\s*\{[^}]*display: grid;/);
  assert.doesNotMatch(compact, /__duty[^{}]*\{[^}]*display: none/);
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
  assert.ok(
    markup.indexOf(link) < markup.indexOf('fleet-map-inspector__controls'),
  );
  assert.doesNotMatch(markup, /fleet-map-truck-info__load-link/);
  assert.match(
    compact,
    /__actions \.map-action-icon\s*\{[^}]*width: var\(--size-control-compact\);[^}]*padding: 0;/,
  );
  assert.match(
    compact,
    /__actions \.map-action-icon\s*\{[^}]*width: var\(--size-control-touch\);/,
  );
});

test('wide truck details use one row of adjacent groups without shrinking text or clocks', () => {
  const wide = compact.slice(
    compact.indexOf('@container map-truck-inspection (width >= 52rem)'),
    compact.indexOf('@container map-truck-inspection (width < 40rem)'),
  );
  assert.match(
    wide,
    /grid-template-columns: minmax\(0,\s*0\.85fr\) var\(--size-route-metric\) minmax\(0,\s*1\.15fr\) minmax\(0,\s*1\.35fr\);/,
  );
  assert.match(
    wide,
    /__load\s*\{[^}]*grid-column: 1;\s*grid-row: 1;[^}]*border-bottom: 0;/,
  );
  for (const [group, column] of [
    ['distances', 2],
    ['visit', 3],
    ['timing', 4],
  ])
    assert.match(
      wide,
      new RegExp(
        `__${group}\\s*\\{\\s*grid-column: ${column};\\s*grid-row: 1;`,
      ),
    );
  assert.match(compact, /__load-reference\s*\{\s*display: contents;/);
  assert.doesNotMatch(
    wide,
    /__load-reference\s*\{[^}]*display: (?:flex|block);/,
  );
  assert.match(
    compact,
    /\.fleet-map-truck-info\s*\{[^}]*padding: var\(--space-xs\) var\(--space-md\);/,
  );
  assert.match(
    compact,
    /__hours\s*\{[^}]*gap: var\(--space-xs\);[^}]*--hos-dial-size: var\(--size-control-touch\);/,
  );
  assert.doesNotMatch(wide, /font-size:|--hos-dial-size:/);
});

test('selected truck enlarges only its three telemetry icons through a shared size token', () => {
  assert.match(
    compact,
    /__telemetry\s*\{[^}]*--fuel-reading-icon-size: var\(--size-telemetry-icon\);/,
  );
  assert.match(
    compact,
    /__reading > small svg\s*\{\s*width: var\(--size-telemetry-icon\);\s*height: var\(--size-telemetry-icon\);/,
  );
  assert.doesNotMatch(compact, /__status\s*\{/);
  assert.doesNotMatch(css, /__hours-label/);
});

test('route groups have responsive dividers inside existing gaps without consuming content width', () => {
  assert.match(
    compact,
    /__distances::before\s*\{[^}]*position: absolute;[^}]*pointer-events: none;[^}]*inset-inline-start: calc\(0px - var\(--space-sm\)\);[^}]*border-inline-start: 1px solid var\(--ui-border-subtle\);/,
  );
  assert.match(compact, /__distances::before\s*\{\s*display: none;/);
  assert.match(
    compact,
    /@container map-truck-inspection \(width >= 52rem\)[\s\S]*__distances::before\s*\{\s*display: block;/,
  );
  assert.match(
    compact,
    /@container map-truck-inspection \(width < 40rem\)[\s\S]*__timing::before\s*\{\s*inset-inline: 0;\s*inset-block: calc\(0px - var\(--space-xs\)\) auto;\s*border-inline-start: 0;\s*border-block-start: 1px solid var\(--ui-border-subtle\);/,
  );
});

test('truck appointment labels do not reserve empty fixed-width space', () => {
  const rule =
    compact.match(
      /__appointment > \.fleet-map-route-info__label\s*\{([^}]*)\}/,
    )?.[1] ?? '';
  assert.doesNotMatch(rule, /(?:width|inline-size|flex-basis|flex)\s*:/);
  assert.match(compact, /__next-appointment\s*\{[^}]*gap: var\(--space-xs\);/);
  assert.match(compact, /\.stop-hours__road\s*\{[^}]*gap: var\(--space-xs\);/);
});

test('the last telemetry metric has no trailing padding after Outside', () => {
  assert.match(compact, /__reading:last-of-type\s*\{\s*padding-right: 0;/);
  const tight = compact.slice(
    compact.indexOf('@container map-truck-inspection (width < 16rem)'),
  );
  assert.match(tight, /__reading\s*\{\s*padding-inline: var\(--space-xs\);/);
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

test('selected header illustration and planned-popup gauges have their own presentation without resizing editor gauges', () => {
  assert.match(
    css,
    /\.fleet-map-truck-info \.truck-illustration__trailer,\s*\.fleet-map-truck-info \.truck-illustration__cab\s*\{\s*fill: var\(--ui-action\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-fuel-visit__dial\s*\{[^}]*width: var\(--size-map-fuel-dial\);\s*height: var\(--size-map-fuel-dial\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-station-popup__title\s*\{[^}]*font-size: var\(--type-heading\);/,
  );
  assert.match(
    css,
    /\.fleet-fuel-visit__dial\s*\{\s*--hos-dial-size: var\(--size-fuel-dial\);/,
  );
});

test('mobile multi-visit fuel cards tighten only level padding while keeping full-size single and desktop gauges', () => {
  assert.match(
    css,
    /@media \(max-width: 767px\)\s*\{\s*\.fleet-station-popup--planned:not\(\.fleet-station-popup--single\) \.fleet-fuel-visit__levels\s*\{\s*padding-block: var\(--space-xs\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-fuel-visit__levels\s*\{[^}]*padding-block: var\(--space-sm\);/,
  );
  assert.match(
    css,
    /\.fleet-station-popup--planned \.fleet-fuel-visit__dial\s*\{[^}]*width: var\(--size-map-fuel-dial\);\s*height: var\(--size-map-fuel-dial\);/,
  );
});
test('route streets ellipsize without a location icon taking column width', () => {
  assert.match(
    css,
    /__street\s*\{[^}]*overflow: hidden;[^}]*text-overflow: ellipsis;[^}]*white-space: nowrap;/,
  );
  assert.doesNotMatch(css, /__address-icon/);
});
