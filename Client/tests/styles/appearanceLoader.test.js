import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

test('preference loader uses theme tokens and respects reduced motion', () => {
  const css = compileString("@use 'components/appearance-loader';", {
    loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
  }).css;
  assert.match(css, /color: var\(--ui-brand\)/);
  assert.match(css, /animation: appearance-loading-pulse/);
  assert.match(css, /animation-delay: 0.15s/);
  assert.match(css, /animation-delay: 0.3s/);
  assert.match(css, /\.appearance-loader--inline[\s\S]*block-size: 1lh/);
  assert.match(css, /\.loading-slot \{[^}]*visibility: hidden/);
  assert.match(css, /\.loading-slot.is-loading \{[^}]*visibility: visible/);
  assert.match(
    css,
    /@media \(prefers-reduced-motion: reduce\)[\s\S]*animation: none/,
  );
});
