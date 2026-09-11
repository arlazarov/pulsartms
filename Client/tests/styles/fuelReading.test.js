import test from 'node:test';
import assert from 'node:assert/strict';
import {compileString} from 'sass';
import {fileURLToPath} from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'components/fuel/reading';", {loadPaths}).css;

test('the shared pump pill uses soft semantic fuel states and compact typography', () => {
  assert.match(css, /\.fuel-reading\s*\{[^}]*min-height: var\(--size-control-compact\);[^}]*border-radius: var\(--radius-pill\);[^}]*background: var\(--ui-success-surface\);[^}]*font-size: var\(--type-body\);/);
  assert.match(css, /\.fuel-reading\.is-low\s*\{[^}]*background: var\(--ui-warning-surface\);[^}]*color: var\(--ui-text\);/);
  assert.match(css, /\.fuel-reading\.is-low \.fuel-reading__icon\s*\{[^}]*color: var\(--ui-telemetry-warning-icon\);/);
  assert.match(css, /\.fuel-reading\.is-critical \.fuel-reading__icon\s*\{[^}]*color: var\(--ui-telemetry-critical-icon\);/);
  assert.match(css, /\.fuel-reading\.is-unknown\s*\{[^}]*background: var\(--ui-surface-soft\);/);
  assert.match(css, /\.fuel-reading__icon\s*\{[^}]*width: var\(--space-lg\);[^}]*height: var\(--space-lg\);/);
});

test('the metric variant stacks its reading below a pump label without a pill surface', () => {
  assert.match(css, /\.fuel-reading--metric\s*\{[^}]*display: grid;[^}]*grid-template-columns: max-content max-content;[^}]*min-height: 0;[^}]*padding: 0;[^}]*border-radius: 0;/);
  assert.match(css, /\.fuel-reading--metric, \.fuel-reading--metric\.is-low, \.fuel-reading--metric\.is-critical, \.fuel-reading--metric\.is-normal, \.fuel-reading--metric\.is-unknown\s*\{[^}]*background: transparent;/);
  assert.match(css, /\.fuel-reading--metric \.fuel-reading__text\s*\{[^}]*display: contents;/);
  assert.match(css, /\.fuel-reading--metric \.fuel-reading__icon\s*\{[^}]*width: var\(--type-heading\);[^}]*height: var\(--type-heading\);/);
  assert.match(css, /\.fuel-reading--metric\s*\{[^}]*gap: var\(--space-sm\) var\(--space-xs\);[^}]*line-height: 1.2;/);
  assert.match(css, /\.fuel-reading--metric \.fuel-reading__label\s*\{[^}]*font-size: var\(--type-small\);/);
  assert.match(css, /\.fuel-reading--metric \.fuel-reading__value\s*\{[^}]*grid-column: 1\s*\/\s*-1;[^}]*font-size: var\(--type-subtitle\);[^}]*font-weight: 700;/);
});
