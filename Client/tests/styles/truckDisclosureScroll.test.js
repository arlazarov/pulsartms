import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const { css } = compileString(
  "@use 'pages/fleet-map/stage'; @use 'pages/fleet-map/inspector';",
  { loadPaths },
);
const selector =
  String.raw`\.fleet-map-inspector` + String.raw`\[data-inspector-mode=truck\]`;

test('mobile truck panels scroll normally within the bounded map inspector', () => {
  const mobile = css.slice(css.indexOf('@media (width < 768px)'));
  const rule = mobile.match(/\.fleet-map-info-reserved\s*\{([^}]*)\}/);
  assert.ok(rule);
  assert.match(rule[1], /max-height: 50%;/);
  assert.match(css, /\.fleet-map-info-reserved\s*\{[^}]*overflow: auto;/);
  assert.doesNotMatch(rule[1], /overflow(?:-y)?: (?:hidden|clip);/);
  assert.doesNotMatch(mobile, /scrollbar-width: none;/);
  assert.doesNotMatch(css, /is-expanded|fleet-map-reveal/);
  assert.doesNotMatch(
    css,
    /\.fleet-map-info-content\s*\{[^}]*position: absolute/,
  );
  // No closed state: the card is always open and scrolls inside (the
  // owner, September 28).
  assert.doesNotMatch(css, /is-mobile-collapsed/);
  assert.match(css, /\.fleet-map-info-content > div\s*\{\s*flex-shrink: 0;/);
  assert.match(
    mobile,
    new RegExp(
      `${selector} \\.fleet-map-inspector__header` +
        String.raw`\s*\{\s*position: static;`,
    ),
  );
  assert.doesNotMatch(
    css.slice(0, css.indexOf('@media (width < 768px)')),
    /scrollbar-width: none;/,
  );
});
