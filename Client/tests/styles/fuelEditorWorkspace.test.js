import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString(
  "@use 'shared/fuel/plan-editor'; @use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector';",
  { loadPaths },
).css;
const razor = readFileSync(
  new URL(
    '../../Shared/Fuel/FuelPlanEditor/FuelPlanEditor.razor',
    import.meta.url,
  ),
  'utf8',
);

// The editor is the plan's own card in edit: the same place, the same
// width, one list. It used to be a wider card at the foot of the map with
// the route down one side and the chosen stop down the other, and the
// owner could not tell which was which (September 26).
test('the fuel editor takes the plan card place and width as one column over the unchanged map', () => {
  assert.match(
    css,
    /\.fuel-plan-editor\s*\{[^}]*position: absolute;[^}]*top: min\(var\(--space-md\), var\(--map-inspector-side-gap, 0px\)\);[^}]*left: 0;[^}]*right: 0;/,
  );
  assert.match(
    css,
    /\.fuel-plan-editor\s*\{[^}]*width: min\(100%, var\(--size-map-stop-inspector\)\);[^}]*max-height: 55%;[^}]*margin-inline: auto;/,
  );
  assert.match(
    css,
    /grid-template-areas: "header" "errors" "totals" "list" "footer";/,
  );
  assert.doesNotMatch(css, /top: 50%|translate\(-50%, -50%\)|translateX/);
  assert.doesNotMatch(
    css,
    /fuel-editor-wide|fuel-editor-route|fuel-editor-height|"timeline content"/,
  );
  assert.doesNotMatch(
    css,
    /fleet-map-stage--fuel-editor|fuel-plan-editor__map-slot|grid-template-columns: subgrid/,
  );
  assert.doesNotMatch(razor, /map-slot|id="fleet-map"/);
  // No second column and no rings: the chosen stop opens under its own
  // line, and the tank is said in words.
  assert.doesNotMatch(razor, /fuel-plan-editor__content|__views|FuelGauge/);
  assert.match(razor, /fuel-plan-editor__detail/);
  assert.match(razor, /fuel-plan-editor__levels/);
  assert.match(razor, /fuel-plan-editor__stop-meta/);
});

test('the desktop list scrolls while the head, totals and foot stay put', () => {
  assert.match(
    css,
    /\.fuel-plan-editor__stops\s*\{[^}]*overflow: auto;[^}]*overscroll-behavior: contain;/,
  );
  assert.doesNotMatch(
    css,
    /\.fuel-plan-editor__(?:header|totals|footer)\s*\{[^}]*overflow: auto/,
  );
  assert.match(
    css,
    /\.fuel-plan-editor__totals\s*\{[^}]*grid-area: totals;[^}]*display: flex;/,
  );
  assert.match(
    css,
    /\.fuel-plan-editor__grip\s*\{[^}]*min-height: var\(--size-control-touch\);/,
  );
  assert.match(css, /\.fuel-plan-editor__detail\s*\{[^}]*grid-column: 1\/-1;/);
});

test('a phone gives the editor the whole width and half the stage, with reachable actions', () => {
  assert.match(css, /\.fuel-plan-editor\s*\{\s*box-sizing: border-box;/);
  const mobile = css.slice(
    css.indexOf('@media (width < 768px)', css.indexOf('.fuel-plan-editor')),
  );
  assert.match(
    mobile,
    /\.fuel-plan-editor\s*\{[^}]*width: 100%;[^}]*max-height: 50%;/,
  );
  assert.doesNotMatch(mobile, /is-route|is-map|"views"/);
  assert.match(
    mobile,
    /\.fuel-plan-editor__timeline-heading,\s*\.fuel-plan-editor__origin\s*\{\s*display: none;/,
  );
  const footer = mobile.match(/\.fuel-plan-editor__footer\s*\{([^}]+)\}/)?.[1];
  assert.ok(footer);
  assert.match(footer, /display: grid;/);
  assert.match(
    footer,
    /grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, var\(--size-fuel-editor-action\)\), 1fr\)\);/,
  );
  assert.match(
    mobile,
    /\.fuel-plan-editor__footer > div\s*\{\s*display: contents;/,
  );
  const button = mobile.match(
    /\.fuel-plan-editor__footer \.btn\s*\{([^}]+)\}/,
  )?.[1];
  assert.ok(button);
  assert.match(button, /min-width: 0;/);
  assert.match(button, /min-height: var\(--size-control-touch\);/);
  assert.match(button, /padding-inline: var\(--space-xs\);/);
  assert.match(button, /white-space: normal;/);
  assert.match(button, /overflow-wrap: anywhere;/);
  assert.doesNotMatch(button, /font-size|text-overflow|overflow: hidden/);
});
