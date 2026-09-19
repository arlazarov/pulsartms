import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const { css } = compileString("@use 'layouts/main-layout';", {
  loadPaths: [fileURLToPath(new URL('../../Styles/', import.meta.url))],
});

test('page entrances respect reduced motion without moving layout', () => {
  assert.match(css, /@media \(prefers-reduced-motion: no-preference\)/);
  for (const phase of ['a', 'b']) {
    assert.match(
      css,
      new RegExp(
        String.raw`\.main-layout__content\[data-page-transition=${phase}\]` +
          String.raw`\s*\{\s*animation: page-enter-${phase} 180ms ease-out;`,
      ),
    );
    assert.match(
      css,
      new RegExp(
        String.raw`@keyframes page-enter-${phase}\s*\{\s*from\s*\{` +
          String.raw`\s*opacity: 0;\s*\}\s*to\s*\{\s*opacity: 1;`,
      ),
    );
  }
  assert.doesNotMatch(css, /transform:|will-change:|animation-delay:/);
  assert.doesNotMatch(css, /animation-fill-mode:|pointer-events:|filter:/);
});
