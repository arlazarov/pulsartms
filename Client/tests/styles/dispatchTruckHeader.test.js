import test from 'node:test';
import assert from 'node:assert/strict';
import {compileString} from 'sass';
import {fileURLToPath} from 'node:url';
import {readFileSync} from 'node:fs';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/dispatch/planning';", {loadPaths}).css;
const truckCss = compileString("@use 'pages/dispatch/truck';", {loadPaths}).css;
const boardCss = compileString("@use 'pages/dispatch/board';", {loadPaths}).css;

test('dispatch board uses horizontal load lanes and a compact icon-led truck identity', () => {
  assert.match(truckCss, /\.dispatch-truck__loads\s*\{[^}]*grid-auto-flow: column;[^}]*grid-auto-columns: min\(100%, max\(var\(--size-dispatch-load-card\), \(100% - var\(--space-section\) \* 2\) \/ 3\)\);[^}]*overflow-x: auto;/s);
  assert.match(truckCss, /\.dispatch-truck__icon\s*\{[^}]*width: var\(--size-control-compact\);/);
  assert.match(truckCss, /@media \(max-width: 550px\)[\s\S]*\.dispatch-truck__loads\s*\{\s*grid-auto-flow: row;/);
  assert.match(css, /\.dispatch-planning--board\s*\{[^}]*padding: 0;[^}]*border: 0;/);
  assert.match(truckCss, /grid-template-columns: minmax\(0, 1fr\) max-content;/);
  assert.doesNotMatch(css, /route-disclosure|driver-details/);
});

test('moving status and fuel share a wrapping pill group while connector arrows disappear in the stacked lane', () => {
  assert.match(css, /\.dispatch-planning__telemetry\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;[^}]*gap: var\(--space-md\);/);
  assert.match(truckCss, /\.dispatch-truck__status\.is-moving\s*\{[^}]*background: var\(--ui-success-surface\);[^}]*color: var\(--ui-success-text\);/);
  assert.match(truckCss, /\.dispatch-truck__loads > \.dispatch-load \+ \.dispatch-load \.dispatch-load__connector\s*\{\s*display: grid;/);
  const mobile = truckCss.slice(truckCss.indexOf('@media (max-width: 550px)'));
  assert.match(mobile, /\.dispatch-load__connector\s*\{\s*display: none;/);
});

test('horizontal load cards share their tallest natural height while stacked cards keep independent content heights', () => {
  assert.match(truckCss, /\.dispatch-truck__loads\s*\{[^}]*align-items: stretch;/);
  assert.doesNotMatch(truckCss, /\.dispatch-truck__loads\s*\{[^}]*(?:min-height|height|grid-auto-rows):/);
  const mobile = truckCss.slice(truckCss.indexOf('@media (max-width: 550px)'));
  assert.match(mobile, /\.dispatch-truck__loads\s*\{[^}]*grid-auto-flow: row;[^}]*align-items: start;/);
});

test('moving speed warnings override the green pill without recoloring stationary states', () => {
  assert.match(truckCss, /\.dispatch-truck__status\.is-moving\.is-low\s*\{[^}]*background: var\(--ui-warning-surface\);/);
  assert.match(truckCss, /\.dispatch-truck__status\.is-moving\.is-low > span\s*\{[^}]*color: var\(--ui-telemetry-warning-icon\);/);
  assert.match(truckCss, /\.dispatch-truck__status\.is-moving\.is-critical\s*\{[^}]*color: var\(--ui-telemetry-critical-icon\);/);
  assert.doesNotMatch(truckCss, /\.dispatch-truck__status\.is-(?:low|critical)\s*\{/);
});

test('wide Dispatch summary keeps route, driver status and HOS in adjacent content-sized columns', () => {
  assert.match(css, /grid-template-columns: minmax\(0, max-content\) minmax\(0, max-content\) max-content;\s*justify-content: start;\s*align-items: center;/);
  assert.match(css, /\.dispatch-planning--compact \.dispatch-planning__driver\s*\{\s*display: grid;\s*gap: var\(--space-sm\);\s*min-width: 0;/);
  assert.match(css, /\.driver-next-recap strong\s*\{\s*display: inline-flex;[\s\S]*?white-space: nowrap;/);
});

test('narrow Dispatch summaries wrap to one column while preserving clocks and recap', () => {
  const mobile = css.slice(css.indexOf('@media (max-width: 550px)'));
  assert.match(mobile, /\.dispatch-planning--compact\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/);
  assert.match(mobile, /\.dispatch-planning--compact > \.driver-hours-panel\s*\{\s*padding: var\(--space-md\) 0 0;/);
  for (const [, selectors, declarations] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!/display:\s*none\s*;/.test(declarations)) continue;
    for (const selector of selectors.split(',')) {
      const target = selector.trim().split(/\s*[>+~]\s*|\s+/).at(-1);
      assert.doesNotMatch(target, /^\.(?:driver-hours-panel|driver-next-recap|dispatch-planning__driver)(?=[.:[#]|$)/,
        `The visible summary container must not be hidden: ${selector.trim()}`);
    }
  }
  assert.doesNotMatch(css, /driver-details|route-disclosure/);
});

test('active Dispatch puts duty and recap beneath identity and route while clocks span the right column', () => {
  assert.match(truckCss, /@media \(min-width: 1200px\)[\s\S]*\.dispatch-truck:has\(> \.dispatch-truck__equipment\)\s*\{\s*grid-template-columns: repeat\(3, minmax\(0, max-content\)\) minmax\(0, 1fr\);/);
  assert.match(truckCss, /\.dispatch-truck:has\(> \.dispatch-truck__equipment\) > \.dispatch-truck__equipment\s*\{[^}]*grid-template-columns: subgrid;\s*grid-template-rows: subgrid;/);
  assert.match(truckCss, /\.dispatch-truck:has\(> \.dispatch-truck__equipment\) > \.dispatch-truck__equipment > \.dispatch-planning--board \.dispatch-planning__driver\s*\{\s*grid-column: 1\s*\/\s*span 2;\s*grid-row: 2;/);
  assert.match(truckCss, /\.dispatch-truck:has\(> \.dispatch-truck__equipment\) > \.dispatch-truck__equipment > \.dispatch-planning--board > \.driver-hours-panel\s*\{\s*grid-column: 3;\s*grid-row: 1\s*\/\s*span 2;/);
  assert.match(truckCss, /\.dispatch-truck__equipment > \.dispatch-planning--board \.dispatch-planning__driver\s*\{[^}]*display: flex;\s*flex-wrap: wrap;[^}]*max-width: none;/);
  assert.match(truckCss, /\.dispatch-truck__equipment > \.dispatch-planning--board \.driver-next-recap\s*\{\s*display: flex;\s*flex-wrap: wrap;/);
  assert.doesNotMatch(truckCss, /\.dispatch-truck__equipment > \.dispatch-planning--board[^{}]*\{[^}]*(?:display: none|height:|max-height:|overflow: hidden)/);
  const mobile = truckCss.slice(truckCss.indexOf('@media (max-width: 550px)'));
  assert.match(mobile, /\.dispatch-truck__equipment > \.dispatch-planning--board \.dispatch-planning__driver\s*\{\s*grid-column: 1;\s*grid-row: 2;/);
  assert.match(mobile, /\.dispatch-truck__equipment > \.dispatch-planning--board > \.driver-hours-panel\s*\{\s*grid-column: 1;\s*grid-row: 3;/);
});

test('wide truck header left-packs identity, telemetry and hours without pushing its map action away', () => {
  assert.match(truckCss, /\.dispatch-truck__header\s*\{[^}]*justify-content: flex-start;\s*gap: var\(--space-md\);/);
  assert.match(truckCss, /\.dispatch-truck:has\(> \.dispatch-truck__equipment\) > \.dispatch-truck__equipment > \.dispatch-planning--board \.dispatch-planning__content\s*\{\s*grid-column: 2;\s*grid-row: 1;\s*justify-self: start;\s*width: fit-content;\s*max-width: 100%;\s*box-sizing: border-box;/);
  assert.match(truckCss, /\.dispatch-truck:has\(> \.dispatch-truck__equipment\) > \.dispatch-truck__equipment > \.dispatch-planning--board > \.driver-hours-panel\s*\{[^}]*justify-self: start;[^}]*max-width: 100%;/);
});

test('truck header telemetry and clocks wrap locally instead of clipping enlarged mobile text', () => {
  assert.match(truckCss, /\.dispatch-truck__equipment > \.dispatch-planning--board \.dispatch-truck__status\s*\{\s*max-width: 100%;\s*box-sizing: border-box;\s*white-space: normal;/);
  assert.match(truckCss, /\.dispatch-truck__equipment > \.dispatch-planning--board\s*\{[^}]*--hos-wrap: wrap;\s*--hos-clock-min-width: 5ch;/);
  assert.match(truckCss, /@media \(max-width: 550px\)[\s\S]*\.dispatch-truck__equipment > \.dispatch-planning--board\s*\{[^}]*--hos-gap: var\(--space-sm\);/);
});

test('Dispatch view framing cannot move the shared title or toolbar', () => {
  const razor = readFileSync(new URL('../../Pages/Dispatch/DispatchList.razor', import.meta.url), 'utf8');
  assert.match(razor, /<section class="dispatch-page dispatch-board">/);
  assert.ok(razor.indexOf('<PageHeader Title="Dispatch"') < razor.indexOf('<div class="dispatch-board__body">'));
  assert.ok(razor.indexOf('aria-label="Load scope"') < razor.indexOf('<div class="dispatch-board__body">'));
  assert.doesNotMatch(razor, /dispatch-board--document/);
  assert.match(boardCss, /\.dispatch-board__body\s*\{[^}]*display: grid;[^}]*min-width: 0;/);
  assert.doesNotMatch(razor, /dispatch-board__body--document/);
  assert.doesNotMatch(boardCss, /dispatch-board--document|body--document/);
  assert.doesNotMatch(boardCss, /\.dispatch-board__body\s*\{[^}]*(?:padding|background|border):/);
  assert.match(boardCss, /\.dispatch-board__message\s*\{[^}]*margin: 0;[^}]*padding: var\(--space-md\) var\(--space-lg\);/);
  assert.match(razor, /class="dispatch-board__message" role="status"/);
  assert.match(razor, /class="dispatch-board__message dispatch-page__error" role="alert"/);
});

test('duty, both rest countdowns and recap use separate compact groups without hiding text', () => {
  assert.match(truckCss, /\.dispatch-truck__equipment > \.dispatch-planning--board \.driver-duty > \*, \.dispatch-truck__equipment > \.dispatch-planning--board \.driver-next-recap\s*\{\s*padding: var\(--space-micro\) var\(--space-sm\);[^}]*background: var\(--ui-surface-soft\);[^}]*max-width: 100%;/);
  assert.doesNotMatch(truckCss, /(?:driver-duty|driver-next-recap)[^{]*\{[^}]*(?:white-space: nowrap|text-overflow: ellipsis|overflow: hidden|display: none)/);
});
