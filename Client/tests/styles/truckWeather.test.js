import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const { css } = compileString("@use 'shared/trucks/weather';", {
  loadPaths,
});

test('weather colors only its icon with existing light and dark theme roles', () => {
  for (const [condition, role] of [
    ['sun', 'telemetry-warning-icon'],
    ['rain', 'link'],
    ['moon', 'link'],
    ['snow', 'accent'],
    ['thunder', 'telemetry-critical-icon'],
  ]) {
    assert.match(
      css,
      new RegExp(
        `\\.truck-weather--${condition} > small > svg` +
          `[^{}]*\\{\\s*color: var\\(--ui-${role}\\);`,
      ),
    );
  }
  assert.match(
    css,
    /\.truck-weather > small > svg\s*\{\s*color: var\(--ui-text-muted\);/,
  );
  assert.doesNotMatch(css, /font|width|height|strong|#[\da-f]{3,8}\b/i);
  assert.doesNotMatch(css, /\.truck-weather[^{}]*\{[^}]*background:/);
});
