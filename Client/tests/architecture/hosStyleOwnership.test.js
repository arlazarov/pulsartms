import test from 'node:test';
import assert from 'node:assert/strict';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = source => compileString(source, { loadPaths }).css;

test('pages compose HOS through custom properties instead of internal selectors', () => {
  const css = compile("@use 'pages';");
  assert.doesNotMatch(css, /\.driver-hours__/);
  assert.doesNotMatch(css, /\.driver-hours(?=[\s.:#[])[^{]*\{/);
  assert.match(css, /--hos-dial-size: min\(\s*var\(--size-hos-dial\)/);
  assert.match(css, /--hos-ring-width: 5/);
});

test('HOS diameter and text use the same component-owned responsive value', () => {
  const css = compile("@use 'components/driver-status';");
  assert.match(
    css,
    /--_hos-dial-size: var\(--hos-dial-size, var\(--size-hos-dial\)\)/,
  );
  assert.match(
    css,
    /@media \(max-width: 550px\)[\s\S]*--_hos-dial-size: var\(--hos-dial-size, var\(--size-hos-dial-compact\)\)/,
  );
  for (const dimension of ['width', 'height'])
    assert.match(css, new RegExp(dimension + ': var\\(--_hos-dial-size,'));
  assert.match(css, /font-size: min\([^;]*var\(--_hos-dial-size\) \* 0\.24\)/);
  assert.doesNotMatch(css, /\b(?:52|58)px\b/);
  assert.match(
    css,
    /\.driver-hours__clock \.driver-hours__dial strong\s*\{[^}]*font-size: min\(/,
  );
});

test('component folders keep fuel module entry points emitting each component once', () => {
  const css = compile("@use 'components';");
  for (const selector of [
    'fuel-plan-editor',
    'fuel-reading',
    'fuel-recalculate',
  ])
    assert.ok(css.includes('.' + selector), selector);
  assert.equal(
    (css.match(/\.driver-hours-panel\s*\{\s*display: grid;/g) ?? []).length,
    1,
  );
});
