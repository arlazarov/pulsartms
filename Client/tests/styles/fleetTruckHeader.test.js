import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString(
  "@use 'shared/driver-status'; @use 'shared/trucks'; @use 'pages/fleet-map/truck-info'; @use 'pages/fleet-map/route-info'; @use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector'; @use 'pages/fleet-map/layout'; @use 'pages/fleet-map/popup';@use 'pages/fleet-map/station';@use 'shared/fuel/visit';",
  { loadPaths },
).css;
// The truck card is these four files; the rest of the inspector folder is
// the shell every card shares and the other modes' own rules.
const compact = compileString(
  "@use 'pages/fleet-map/inspector/card';" +
    " @use 'pages/fleet-map/inspector/hours-line';" +
    " @use 'pages/fleet-map/inspector/route-facts';" +
    " @use 'pages/fleet-map/inspector/narrow';",
  { loadPaths },
).css;

test('supporting truck values have semantic contrast without another font size or spacing scale', () => {
  const rule = compact.match(/\.fleet-map-inspector__value\s*\{([^}]*)\}/)?.[1];
  assert.ok(rule);
  assert.match(rule, /color: var\(--ui-text\);/);
  assert.match(rule, /font-weight: 600;/);
  assert.doesNotMatch(rule, /font-size:|line-height:|padding:|margin:/);
});

test('compact truck inspection retains bounded telemetry and HOS', () => {
  // Only the icons the words repeat are hidden on the vehicle line; a
  // reading itself is never dropped.
  assert.doesNotMatch(compact, /__reading\s*\{[^}]*display: none/);
  // The vehicle line has one owner, and it is not the card's stylesheet:
  // the shared TruckReadings, which Dispatch reads a truck with too.
  assert.match(css, /\.truck-readings\s*\{[^}]*display: grid;/);
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
  const telemetry = css.match(/\.truck-readings\s*\{([^}]*)\}/)?.[1];
  // Equal cells retain stable alignment but wrap before values overlap.
  assert.match(telemetry, /display: grid;/);
  assert.match(
    telemetry,
    /grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, var\(--truck-readings-min-width, var\(--size-telemetry-reading\)\)\), 1fr\)\);/,
  );
  assert.match(telemetry, /font-variant-numeric: tabular-nums;/);
  // The head: the truck and what is left beside it, then its rows - the
  // load and its arrival, the vehicle and its clocks, where the truck is -
  // each under a rule that runs the width of the card.
  assert.match(
    compact,
    /grid-template-areas: "identity distance controls" "hours hours hours";/,
  );
  assert.match(
    compact,
    /\.fleet-map-inspector__row\s*\{[^}]*border-top: 1px solid/,
  );
  assert.doesNotMatch(
    compact,
    /__telemetry\s*\{[^}]*(?:flex-basis|width): 100%;/,
  );
});

test('truck inspector keeps one disclosure on wide cards and none on a phone', () => {
  const markup = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  assert.match(markup, /fleet-map-mobile-summary__toggle/);
  assert.match(markup, /aria-controls="fleet-map-details"/);
  assert.doesNotMatch(markup, /fleet-map-truck-info__more/);
  const toggle = compact.match(
    /\] \.fleet-map-mobile-summary__toggle\s*\{([^}]*)\}/,
  )?.[1];
  assert.match(toggle, /display: inline-flex;/);
  // The words are the link colour, which the dark theme lightens; the
  // action colour stays primary 600 there and falls below AA on the card.
  for (const property of [
    'button-text',
    'button-border',
    'button-hover-text',
    'button-hover-border',
  ])
    assert.match(toggle, new RegExp(`--${property}: var\\(--ui-link\\);`));
  assert.doesNotMatch(toggle, /--ui-action\b/);
  // The closed state belongs to a card wide enough for two columns; a
  // phone's card is open whole and hides the toggle. Narrow is narrow
  // wherever the card stands: it reads its own width, not the window's.
  assert.match(
    compact,
    /@container map-truck-card \(width >= 40rem\)[\s\S]*\.is-mobile-collapsed \.fleet-map-info-content,\s*[^{]*\.is-mobile-collapsed \.fleet-map-inspector__duty\s*\{\s*display: none;/,
  );
  const phone = compact.slice(
    compact.indexOf('@container map-truck-card (width < 40rem)'),
  );
  assert.match(
    phone,
    /\.fleet-map-mobile-summary__toggle\s*\{\s*display: none;/,
  );
  assert.doesNotMatch(phone, /is-mobile-collapsed/);
  assert.doesNotMatch(compact, /@media \(width < 768px\)/);
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
  // The duty line came back as the owner's September 26 rest row: the
  // status and its time, then the rest and reset it counts towards. Only
  // the old "Hours are enough" wording stays off the card.
  assert.match(css, /\.fleet-map-inspector__duty\s*\{/);
  assert.doesNotMatch(css, /hours-enough|__duty-enough/);
  assert.match(
    css,
    /\.fleet-map-route-info\s*\{[^}]*min-height: 0;\s*align-content: start;/,
  );
  assert.match(
    compact,
    /\.fleet-map-route-info \.fleet-map-inspector__actions\s*\{[^}]*display: grid;/,
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
    /@media \(width < 768px\)[\s\S]*\.fleet-map-info-reserved\s*\{[^}]*max-height: 50%;/,
  );
  // At a large text size the toolbar left the map a strip: the stage keeps
  // half the screen and the panel half of it, so the fuel plan's actions
  // stay within reach.
  assert.match(
    css,
    /@media \(width < 768px\)[\s\S]*\.fleet-map-stage\s*\{\s*min-height: 50dvh;/,
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
    /\.fleet-map-route-info \.fleet-map-inspector__actions\s*\{[^}]*grid-template-columns: repeat\(auto-fit, minmax\(8rem, 1fr\)\);/,
  );
  assert.match(css, /\.fleet-map-route-info__next\s*\{[^}]*min-height: 0;/);
  for (const selector of ['fleet-map-truck-info', 'fleet-map-route-info'])
    assert.doesNotMatch(
      css,
      new RegExp(
        // Nothing in a panel is clipped, except the street and the
        // facility name, which shorten by ellipsis, and a word kept only
        // for a screen reader, which is hidden by being clipped to nothing.
        `\\.${selector}\\s*\\{[^}]*[;{]\\s*height:|` +
          `\\.${selector}(?!__street\\b)(?![^{}]*__facility\\b)[^{}]*\\{` +
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
  assert.doesNotMatch(css, /\.truck-readings__reading[^{]*\{[^}]*border-left:/);
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
  // A wide screen does not re-column the readings into three towers. This
  // once looked for that rule inside a window no stylesheet opens, so it
  // was reading an empty string and could never have found anything.
  assert.doesNotMatch(
    css,
    /\.truck-readings\s*\{[^}]*grid-template-columns: repeat\(3,\s*minmax/,
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
  const narrow = compact.slice(
    compact.indexOf('@container map-truck-card (width < 40rem)'),
  );
  // Docked or on a phone the top lines are the unit under its eyebrow,
  // with the controls beside it (the workspace design, September 27); the
  // crew and what is left follow.
  assert.match(narrow, /\.fleet-map-inspector__title\s*\{\s*grid-area: title;/);
  assert.match(
    narrow,
    /grid-template-areas: "eyebrow controls" "title controls" "crew crew" "distance distance" "hours hours";/,
  );
  assert.match(narrow, /\.fleet-map-inspector__crew\s*\{\s*grid-area: crew;/);
  assert.doesNotMatch(narrow, /is-mobile-collapsed/);
  assert.match(
    narrow,
    /\.fleet-map-inspector__desktop-title\s*\{\s*display: block;/,
  );
});

test('next-stop distance rides the clocks line at every width', () => {
  assert.match(
    compact,
    /\.fleet-map-mobile-summary__remaining\s*\{\s*display: flex;/,
  );
  const narrow = compact.slice(
    compact.indexOf('@container map-truck-card (width < 40rem)'),
  );
  // The load leads the clocks line rather than sitting in a chip of its
  // own, and its number is labelled at every width.
  assert.doesNotMatch(compact, /__remaining\s*\{[^}]*margin-inline-start/);
  // A phone keeps the same order, and each row stacks its answer under
  // its fact instead of squeezing the two abreast.
  assert.match(
    narrow,
    /\.fleet-map-inspector__row\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  assert.doesNotMatch(compact, /flex-basis: 100%;\s*min-inline-size: 0;/);
  assert.match(compact, /__label\s*\{\s*display: inline;/);
  assert.doesNotMatch(compact, /__label\s*\{\s*display: none;/);
  assert.doesNotMatch(narrow, /is-mobile-collapsed[^{}]*__remaining/);
  // Opening the card must not move it: no width re-columns the header.
  assert.doesNotMatch(narrow, /is-mobile-expanded/);
});

test('truck metadata stays aligned and disclosure does not restyle the primary summary', () => {
  assert.match(
    compact,
    /\.fleet-map-inspector__driver,\s*[^{}]*\.fleet-map-inspector__trailer\s*\{[^}]*color: var\(--ui-text-secondary\);/,
  );
  assert.match(compact, /__trailer \+ [^{]*__driver::before\s*\{\s*content:/);
  assert.match(css, /\.fleet-map-truck-info\s*\{[^}]*align-items: baseline;/);
  assert.doesNotMatch(compact, /hours-enough|__duty-enough/);
});

test('the load opens from its number in the head, with no Open load button in the actions', () => {
  const markup = readFileSync(
    new URL('../../Pages/FleetMap/FleetMap.razor', import.meta.url),
    'utf8',
  );
  const link = markup.match(
    /<a class="fleet-map-inspector__load-link"[\s\S]*?<\/a>/,
  )?.[0];
  assert.ok(link);
  // Only with a load chosen, and with the way back to this map.
  assert.match(link, /href="@OpenLoadHref"/);
  assert.match(
    readFileSync(
      new URL('../../Pages/FleetMap/FleetMap.ReturnPlace.cs', import.meta.url),
      'utf8',
    ),
    /OpenLoadHref =>\s*SelectedDispatchId is \{ \} id\s*\?\s*ReturnNavigation\.Load\(id, ReturnOrigin\)\s*:\s*null;/,
  );
  assert.match(link, /title="Open load"/);
  assert.match(link, /<ActionIcon Kind="external-link"\s*\/>/);
  // The button that repeated the link at the end of the action row is
  // gone (the owner, September 26).
  assert.doesNotMatch(markup, /fleet-map-inspector__open-load/);
  assert.doesNotMatch(markup, /Route & load details/);
  assert.doesNotMatch(markup, /fleet-map-truck-info__load-link/);
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
    /\.fleet-station-popup--planned \.fleet-station-popup__title\s*\{[^}]*font-size: var\(--type-subtitle\);/,
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
