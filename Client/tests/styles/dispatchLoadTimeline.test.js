import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'pages/dispatch/load';", { loadPaths }).css;

test('load cards retain a vertical stop timeline at every width instead of reverting to split columns', () => {
  assert.match(
    css,
    /\.dispatch-load__route\s*\{\s*display: grid;\s*grid-template-columns: minmax\(0, 1fr\);/,
  );
  assert.doesNotMatch(
    css,
    /\.dispatch-load__route\s*\{[^}]*repeat\(2, minmax\(0, 1fr\)\)/,
  );
  assert.match(css, /\.dispatch-load__route-stop\s*\{\s*position: relative;/);
  assert.match(
    css,
    /\.dispatch-load__route-stop:not\(:last-child\)::before\s*\{[^}]*content: "";[^}]*top: var\(--size-dispatch-stop-marker\);/,
  );
  assert.match(
    css,
    /\.dispatch-load__stop-number\s*\{[^}]*width: var\(--size-dispatch-stop-marker\);\s*height: var\(--size-dispatch-stop-marker\);/,
  );
  assert.match(
    css,
    /\.dispatch-load__stop--completed \.dispatch-load__stop-number\s*\{[^}]*background: var\(--ui-success-text\);/,
  );
});

test('compact cards keep a readable operational footer without a permanent financial strip', () => {
  assert.match(
    css,
    /\.dispatch-load\s*\{[^}]*display: flex;\s*flex-direction: column;\s*min-width: 0;/,
  );
  assert.doesNotMatch(
    css,
    /\.dispatch-load__(?:summary|metrics|mileage|financial)\b/,
  );
  assert.match(
    css,
    /\.dispatch-load__footer\s*\{[^}]*margin-top: var\(--space-md\);/,
  );
  assert.match(
    css,
    /\.dispatch-load__remaining,[\s\S]*?font-size: var\(--type-body\);/,
  );
  assert.doesNotMatch(css, /(?:max-height|text-overflow):/);
});

test('the decorative next-card arrow has its own gutter without intercepting clicks', () => {
  assert.match(
    css,
    /\.dispatch-load__connector\s*\{[^}]*position: absolute;[^}]*display: none;[^}]*width: var\(--space-section\);[^}]*pointer-events: none;/,
  );
});

test('a stretched card fills unused timeline space before its footer without a fixed card height', () => {
  assert.match(
    css,
    /\.dispatch-load\s*\{[^}]*display: flex;[^}]*flex-direction: column;/,
  );
  assert.match(
    css,
    /\.dispatch-load__overview\s*\{[^}]*display: flex;[^}]*flex-direction: column;[^}]*flex: 1;/,
  );
  assert.match(
    css,
    /\.dispatch-load__footer\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;/,
  );
  assert.doesNotMatch(css, /\.dispatch-load\s*\{[^}]*(?:min-height|height):/);
});

test('stop summaries stay free of inline disclosures while full details retain readable content', () => {
  assert.doesNotMatch(css, /stop-details|more-details|stop--has-details/);
  assert.match(
    css,
    /\.dispatch-load__stop-detail-content\s*\{[^}]*display: grid;[^}]*background: var\(--ui-surface-soft\);/,
  );
  assert.match(
    css,
    /\.dispatch-load__location\s*\{[^}]*font-size: var\(--type-lead\);/,
  );
});

test('stop details and footer shrink within the card instead of overflowing at text zoom', () => {
  assert.match(
    css,
    /\.dispatch-load__stop\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\);/,
  );
  assert.match(
    css,
    /\.dispatch-load__footer \.dispatch-load__details\s*\{\s*margin-left: auto;/,
  );
});

test('wide summary stops put arrival facts beside the address without changing the detailed dialog', () => {
  assert.match(css, /@container dispatch-load \(min-width: 32rem\)/);
  assert.match(
    css,
    /\.dispatch-load__stop--summary\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\);[^}]*column-gap: var\(--space-lg\);/,
  );
  assert.match(
    css,
    /\.dispatch-load__stop--summary > \.dispatch-load__stop-times\s*\{[^}]*grid-column: 2;[^}]*grid-row: 1\s*\/\s*span 3;[^}]*align-content: start;/,
  );
  assert.match(
    css,
    /\.dispatch-load__route-stop\s*\{[^}]*padding: 0 0 var\(--space-sm\)/,
  );
});

test('copy confirmation uses a fixed icon instead of adding a status line to the card', () => {
  assert.match(
    css,
    /\.dispatch-load__copy-icon\s*\{[^}]*width: var\(--space-lg\);[^}]*height: var\(--space-lg\);[^}]*flex-shrink: 0;/,
  );
  assert.match(
    css,
    /\.dispatch-load__copy-icon\.is-copied\s*\{[^}]*color: var\(--ui-success-text\);/,
  );
  assert.doesNotMatch(css, /copy-status/);
});

test('summary cycle warnings start under the ETA label while lateness stays grouped with arrival', () => {
  assert.match(
    css,
    /\.dispatch-load__stop--summary \.stop-hours__road > \.stop-hours__value\s*\{\s*display: contents;/,
  );
  assert.match(
    css,
    /\.dispatch-load__stop--summary \.stop-hours__arrival\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap;[^}]*align-items: baseline;/,
  );
  assert.match(
    css,
    /\.dispatch-load__stop--summary \.stop-hours__cycle-status\s*\{\s*grid-column: 1\s*\/\s*-1;/,
  );
});

test('narrow stop forecasts wrap their label without splitting clocks', () => {
  const hours = compileString("@use 'components/driver-status';", {
    loadPaths,
  }).css;
  assert.match(css, /__stop-times\s*\{[^}]*--stop-hours-road-display: flex;/);
  assert.match(hours, /__road\s*\{[^}]*flex-wrap: wrap;/);
  assert.match(hours, /__clock\s*\{[^}]*white-space: nowrap;/);
  assert.match(hours, /__cycle-status\s*\{[^}]*flex-basis: 100%;/);
});

test('next load presentation uses named purple roles without changing current or completed colors', () => {
  assert.match(
    css,
    /\.dispatch-load--next \.dispatch-load__phase\s*\{[^}]*color: var\(--ui-route-next\);[^}]*background: var\(--ui-route-next-surface\);/,
  );
  assert.match(
    css,
    /\.dispatch-load--next \.dispatch-load__stop-number\s*\{[^}]*background: var\(--ui-route-next\);/,
  );
  assert.match(
    css,
    /\.dispatch-load--current \.dispatch-load__stop-number\s*\{[^}]*background: var\(--ui-action\);/,
  );
  assert.ok(
    css.indexOf('.dispatch-load__stop--completed .dispatch-load__stop-number') >
      css.indexOf('.dispatch-load--next .dispatch-load__stop-number'),
  );
});
