import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/dispatch/planning';", { loadPaths }).css;
const truckCss = compileString("@use 'pages/dispatch/truck';", {
  loadPaths,
}).css;
const boardCss = compileString("@use 'pages/dispatch/board';", {
  loadPaths,
}).css;
const planningRazor = readFileSync(
  new URL('../../Pages/Dispatch/DispatchPlanning.razor', import.meta.url),
  'utf8',
);
// The truck's summary on the board (the owner's Dispatch cards of
// September 26): the Fleet Map truck card's head and rows, and behind
// Details the duty and the recap.
const board = planningRazor.slice(0, planningRazor.indexOf('\nelse\n'));

test('dispatch board uses horizontal load lanes and the Fleet truck card head', () => {
  assert.match(
    truckCss,
    /\.dispatch-truck__loads\s*\{[^}]*grid-auto-flow: column;[^}]*grid-auto-columns: min\(100%,\s*max\(var\(--size-dispatch-load-card\),\s*\(100% - var\(--space-section\) \* 2\) \/ 3\)\);[^}]*overflow-x: auto;/s,
  );
  // The unit and its crew, what is left, the map and Details: one line.
  assert.match(
    truckCss,
    /\.dispatch-truck__header\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) auto auto auto;\s*grid-template-areas: "identity left map toggle";/,
  );
  assert.doesNotMatch(
    truckCss,
    /dispatch-truck__icon|dispatch-truck__equipment/,
  );
  assert.match(
    truckCss,
    /@media \(width < 551px\)[\s\S]*\.dispatch-truck__loads\s*\{\s*grid-auto-flow: row;/,
  );
  // A phone keeps the unit and its crew on the head's first line whole.
  assert.match(
    truckCss,
    /@media \(width < 551px\)[\s\S]*\.dispatch-truck__header\s*\{[^}]*display: flex;\s*flex-wrap: wrap;[\s\S]*\.dispatch-truck__header > \.dispatch-truck__identity\s*\{\s*flex-basis: 100%;/,
  );
  assert.match(
    css,
    /\.dispatch-planning--board\s*\{[^}]*padding: 0;[^}]*border: 0;/,
  );
  assert.doesNotMatch(css, /route-disclosure|driver-details/);
});

test('the vehicle line stands beside the clocks while connector arrows disappear in the stacked lane', () => {
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__readings\s*\{[^}]*grid-template-columns: minmax\(0, 11fr\) minmax\(0, 9fr\);[^}]*border-top: 1px solid var\(--ui-border-subtle\);/,
  );
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__clocks\s*\{[^}]*border-inline-start: 1px solid var\(--ui-border-subtle\);[^}]*--hos-display: flex;/,
  );
  assert.match(board, /<TruckReadings Speed="Speed"/);
  assert.match(board, /<DriverHours Clocks="Hos" Dials="false"/);
  assert.match(
    truckCss,
    /\.dispatch-truck__loads > \.dispatch-load \+ \.dispatch-load \.dispatch-load__connector\s*\{\s*display: grid;/,
  );
  const mobile = truckCss.slice(truckCss.indexOf('@media (width < 551px)'));
  assert.match(mobile, /\.dispatch-load__connector\s*\{\s*display: none;/);
});

test('horizontal load cards share their tallest natural height while stacked cards keep independent content heights', () => {
  assert.match(
    truckCss,
    /\.dispatch-truck__loads\s*\{[^}]*align-items: stretch;/,
  );
  assert.doesNotMatch(
    truckCss,
    /\.dispatch-truck__loads\s*\{[^}]*(?:min-height|height|grid-auto-rows):/,
  );
  const mobile = truckCss.slice(truckCss.indexOf('@media (width < 551px)'));
  assert.match(
    mobile,
    /\.dispatch-truck__loads\s*\{[^}]*grid-auto-flow: row;[^}]*align-items: start;/,
  );
});

test('one current load keeps normal lane width and its missing next assignment stays content-sized', () => {
  assert.doesNotMatch(
    truckCss,
    /:not\(:has\(> \.dispatch-load ~ \.dispatch-load\)\)/,
  );
  assert.match(
    truckCss,
    /\.dispatch-truck__available--next\s*\{[^}]*align-self: start;[^}]*border-style: dashed;[^}]*background: transparent;/,
  );
  assert.doesNotMatch(
    truckCss,
    /\.dispatch-truck__available--next\s*\{[^}]*(?:min-height|height):/,
  );
});

test('the speed and engine read in the shared vehicle line, not a moving pill', () => {
  // TruckReadings owns the speed and engine tones for Fleet Map and
  // Dispatch alike; the board no longer draws a Driving pill of its own.
  assert.doesNotMatch(truckCss, /dispatch-truck__status/);
  assert.doesNotMatch(board, /dispatch-truck__status|MotionLabel/);
  assert.match(board, /Engine="@EngineState"/);
});

test('wide Dispatch summary keeps route, driver status and HOS in adjacent content-sized columns', () => {
  assert.match(
    css,
    /grid-template-columns: minmax\(0,\s*max-content\) minmax\(0,\s*max-content\) max-content;\s*justify-content: start;\s*align-items: center;/,
  );
  assert.match(
    css,
    /\.dispatch-planning--compact \.dispatch-planning__driver\s*\{\s*display: grid;\s*gap: var\(--space-sm\);\s*min-width: 0;/,
  );
  assert.match(
    css,
    /\.driver-next-recap strong\s*\{\s*display: inline-flex;[\s\S]*?white-space: nowrap;/,
  );
});

test('narrow Dispatch summaries wrap to one column while preserving clocks and recap', () => {
  const mobile = css.slice(css.indexOf('@media (width < 551px)'));
  assert.match(
    mobile,
    /\.dispatch-planning--compact\s*\{\s*grid-template-columns: minmax\(0,\s*1fr\);/,
  );
  assert.match(
    mobile,
    /\.dispatch-planning--compact > \.driver-hours-panel\s*\{\s*padding: var\(--space-md\) 0 0;/,
  );
  for (const [, selectors, declarations] of css.matchAll(
    /([^{}]+)\{([^{}]*)\}/g,
  )) {
    if (!/display:\s*none\s*;/.test(declarations)) continue;
    for (const selector of selectors.split(',')) {
      const target = selector
        .trim()
        .split(/\s*[>+~]\s*|\s+/)
        .at(-1);
      assert.doesNotMatch(
        target,
        /^\.(?:driver-hours-panel|driver-next-recap|dispatch-planning__driver)(?=[.:[#]|$)/,
        `The visible summary container must not be hidden: ${selector.trim()}`,
      );
    }
  }
  assert.doesNotMatch(css, /driver-details|route-disclosure/);
});

test('the duty and the recap wait behind Details, as on the map card', () => {
  assert.match(
    board,
    /aria-expanded="@\(_detailsOpen \? "true" : "false"\)"\s*aria-controls="@_detailsId"/,
  );
  assert.match(
    board,
    /<div id="@_detailsId" class="dispatch-planning__duty"\s*hidden="@\(!_detailsOpen\)">/,
  );
  const duty = board.slice(board.indexOf('class="dispatch-planning__duty"'));
  assert.match(duty, /<DriverDutySummary Reading="row"/);
  assert.match(duty, /<DriverNextRecap Snapshot="CurrentCycle" \/>/);
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__duty\s*\{[^}]*display: flex;\s*flex-wrap: wrap;[^}]*border-top: 1px solid var\(--ui-border-subtle\);/,
  );
  // The planned total left the board (the owner, September 26).
  assert.doesNotMatch(board, /Total Distance|OriginalPlannedMiles/);
});

test('what is left stands beside the unit, before the map and Details', () => {
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__left\s*\{\s*grid-area: left;\s*justify-self: end;/,
  );
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__toggle\s*\{\s*grid-area: toggle;/,
  );
  assert.match(truckCss, /a\.dispatch-truck__map\s*\{\s*grid-area: map;/);
  assert.match(board, /DistanceLeft\.Miles|LeftMiles/);
});

test('truck readings and clocks fold locally instead of clipping enlarged mobile text', () => {
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__clocks\s*\{[^}]*--hos-wrap: wrap;/,
  );
  assert.match(
    css,
    /@container dispatch-truck \(width < 1050px\)[\s\S]*\.dispatch-planning--board \.dispatch-planning__readings\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  const mobile = css.slice(css.indexOf('@media (width < 551px)'));
  assert.match(mobile, /--truck-readings-divider: 0;/);
  assert.match(mobile, /--hos-divider: 0;/);
});

test('Dispatch view framing cannot move the shared title or toolbar', () => {
  const razor = readFileSync(
    new URL('../../Pages/Dispatch/DispatchList.razor', import.meta.url),
    'utf8',
  );
  assert.match(razor, /<section class="dispatch-page dispatch-board">/);
  assert.ok(
    razor.indexOf('<PageHeader Title="Dispatch"') <
      razor.indexOf('<div class="dispatch-board__body">'),
  );
  assert.ok(
    razor.indexOf('aria-label="Load scope"') <
      razor.indexOf('<div class="dispatch-board__body">'),
  );
  assert.doesNotMatch(razor, /dispatch-board--document/);
  assert.match(
    boardCss,
    /\.dispatch-board__body\s*\{[^}]*display: grid;[^}]*min-width: 0;/,
  );
  assert.doesNotMatch(razor, /dispatch-board__body--document/);
  assert.doesNotMatch(boardCss, /dispatch-board--document|body--document/);
  assert.doesNotMatch(
    boardCss,
    /\.dispatch-board__body\s*\{[^}]*(?:padding|background|border):/,
  );
  assert.match(
    boardCss,
    /\.dispatch-board__message\s*\{[^}]*margin: 0;[^}]*padding: var\(--space-md\) var\(--space-lg\);/,
  );
  assert.match(razor, /class="dispatch-board__message" role="status"/);
  assert.match(
    razor,
    /class="dispatch-board__message dispatch-page__error" role="alert"/,
  );
});

test('duty, both rest countdowns and recap wrap on their row without hiding text', () => {
  assert.match(
    css,
    /\.dispatch-planning--board \.dispatch-planning__duty > \.driver-next-recap\s*\{\s*display: flex;\s*flex-wrap: wrap;/,
  );
  assert.doesNotMatch(
    css.slice(css.indexOf('.dispatch-planning--board')),
    /(?:driver-duty|driver-next-recap|__duty)[^{]*\{[^}]*(?:white-space: nowrap|text-overflow: ellipsis|overflow: hidden|display: none)/,
  );
});
