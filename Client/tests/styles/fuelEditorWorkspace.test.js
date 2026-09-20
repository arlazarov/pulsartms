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

test('wide fuel editor gives route and station independent adjacent columns over the unchanged map', () => {
  assert.match(
    css,
    /@media \(width >= 1000px\)[\s\S]*\.fuel-plan-editor\s*\{[^}]*left: 50%;[^}]*right: auto;[^}]*transform: translateX\(-50%\);/,
  );
  assert.doesNotMatch(css, /top: 50%|translate\(-50%, -50%\)/);
  assert.match(
    css,
    /\.fuel-plan-editor\s*\{[^}]*position: absolute;[^}]*right: var\(--space-md\);[^}]*bottom: var\(--space-md\);[^}]*width: min\(var\(--size-fuel-editor-wide\),/,
  );
  assert.match(
    css,
    /height: min\(var\(--size-fuel-editor-height\), 100% - var\(--space-md\) \* 2\);/,
  );
  assert.match(
    css,
    /grid-template-columns: minmax\(0, var\(--size-fuel-editor-route\)\) minmax\(0, 1fr\);/,
  );
  assert.match(
    css,
    /grid-template-areas: "header header" "timeline content" "footer footer";/,
  );
  assert.doesNotMatch(
    css,
    /grid-template-rows:[^;]*var\(--size-fuel-editor-timeline/,
  );
  assert.doesNotMatch(
    css,
    /fleet-map-stage--fuel-editor|fuel-plan-editor__map-slot|grid-template-columns: subgrid/,
  );
  const razor = readFileSync(
    new URL(
      '../../Shared/Fuel/FuelPlanEditor/FuelPlanEditor.razor',
      import.meta.url,
    ),
    'utf8',
  );
  assert.doesNotMatch(razor, /map-slot|id="fleet-map"/);
  assert.match(razor, /fuel-plan-editor__stop-meta/);
});

test('the timeline and selected controls scroll independently while header and footer remain outside scroll areas', () => {
  assert.match(
    css,
    /\.fuel-plan-editor__stops\s*\{[^}]*overflow: auto;[^}]*overscroll-behavior: contain;/,
  );
  assert.match(
    css,
    /\.fuel-plan-editor__content\s*\{[^}]*overflow: auto;[^}]*overscroll-behavior: contain;/,
  );
  assert.doesNotMatch(
    css,
    /\.fuel-plan-editor__(?:header|footer)\s*\{[^}]*overflow: auto/,
  );
  assert.match(
    css,
    /@media \(width < 1000px\)[\s\S]*\.fuel-plan-editor\s*\{[^}]*max-height: min\(70dvh, 75%\);[^}]*grid-template-rows: auto minmax\(0, 9fr\) minmax\(0, 11fr\) auto;/,
  );
  assert.match(
    css,
    /grid-template-areas: "header" "timeline" "content" "footer";/,
  );
  assert.match(
    css,
    /\.fuel-plan-editor__grip\s*\{[^}]*min-height: var\(--size-control-touch\);/,
  );
});

test('phone editing uses one full-height pane and can expose the map without closing the draft', () => {
  assert.match(css, /\.fuel-plan-editor\s*\{\s*box-sizing: border-box;/);
  const mobile = css.slice(
    css.indexOf('@media (width < 768px)', css.indexOf('.fuel-plan-editor')),
  );
  assert.match(
    mobile,
    /\.fuel-plan-editor\s*\{[^}]*top: 0;[^}]*width: 100%;[^}]*height: 100%;[^}]*max-height: 100%;/,
  );
  assert.match(mobile, /grid-template-rows: auto auto minmax\(0, 1fr\) auto;/);
  assert.match(
    mobile,
    /grid-template-areas: "header" "views" "content" "footer";/,
  );
  assert.match(
    mobile,
    /\.fuel-plan-editor\.is-route\s*\{[^}]*grid-template-areas: "header" "views" "timeline" "footer";/,
  );
  assert.match(mobile, /\.fuel-plan-editor\.is-map\s*\{[^}]*height: auto;/);
  assert.match(
    mobile,
    /\.fuel-plan-editor\.is-map \.fuel-plan-editor__footer\s*\{\s*display: none;/,
  );
});

test('phone fuel actions share a row without shrinking targets or text', () => {
  const mobile = css.slice(
    css.indexOf('@media (width < 768px)', css.indexOf('.fuel-plan-editor')),
  );
  const footer = mobile.match(/\.fuel-plan-editor__footer\s*\{([^}]+)\}/)?.[1];
  assert.ok(footer);
  assert.match(footer, /display: grid;/);
  assert.match(footer, /grid-template-columns: repeat\(3, minmax\(0, 1fr\)\);/);
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
