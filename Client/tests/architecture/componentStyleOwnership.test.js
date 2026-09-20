import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const compile = source => compileString(source, { loadPaths }).css;

test('pages compose HOS through custom properties instead of internal selectors', () => {
  const css = compile("@use 'pages';");
  assert.doesNotMatch(css, /\.driver-hours__/);
  assert.doesNotMatch(css, /\.driver-hours(?=[\s.:#[])[^{]*\{/);
  // Pages say what they want - flex, wrap, a gap - and never how a clock
  // is drawn.
  assert.match(css, /--hos-display: flex;/);
  assert.match(css, /--hos-wrap: wrap;/);
});

// A component owns what is inside it. A page places it - where it stands in
// the page's own grid, how wide, how close to what is above - and asks for
// one of the readings the component publishes; it does not reach past the
// root to restyle the parts. This was written for the HOS clocks alone, and
// five other components were being rewritten from the outside: the arrival
// forecast from five page stylesheets, the data table from two, the form's
// actions from one.
test('no page rewrites the inside of a component it only places', () => {
  const root = new URL('../../Styles/', import.meta.url);
  const roots = new Set();
  for (const file of readdirSync(new URL('components/', root), {
    recursive: true,
  }).filter(x => x.endsWith('.scss')))
    for (const [, name] of readFileSync(
      new URL(`components/${file}`, root),
      'utf8',
    ).matchAll(/^\.([a-z][a-z0-9-]*)\s*[,{]/gm))
      roots.add(name);
  assert.ok(roots.size > 20, 'component roots were not found');
  for (const folder of ['pages', 'layouts'])
    for (const file of readdirSync(new URL(`${folder}/`, root), {
      recursive: true,
    }).filter(x => x.endsWith('.scss'))) {
      const source = readFileSync(new URL(`${folder}/${file}`, root), 'utf8');
      for (const name of roots)
        assert.doesNotMatch(
          source,
          new RegExp(`\\.${name}__`),
          `${folder}/${file}: .${name}__… belongs to the component`,
        );
    }
});

// A page may write a component's markup - the truck card writes two rows of
// the arrival forecast itself, so that a truck with nothing forecast still
// shows the shape of the answer - but then it must ask for the same reading
// its neighbours were given. One that did not fell back to the component's
// default and stood in bold beside the quiet rows it was standing in for.
// The stylesheet check above cannot see this: the page names the parts in
// Razor, not in a stylesheet.
test('markup that writes a component asks for one of its readings', () => {
  const styles = new URL('../../Styles/components/', import.meta.url);
  const readings = new Map();
  for (const file of readdirSync(styles, { recursive: true }).filter(x =>
    x.endsWith('.scss'),
  ))
    for (const [, root] of readFileSync(new URL(file, styles), 'utf8').matchAll(
      /^\.([a-z][a-z0-9-]*)--[a-z0-9-]+\s*[,{]/gm,
    ))
      readings.set(root, true);
  assert.ok(readings.size > 3, 'components with readings were not found');
  const owner = root =>
    root
      .split('-')
      .map(part => part[0].toUpperCase() + part.slice(1))
      .join('');
  const client = new URL('../../', import.meta.url);
  const sources = readdirSync(client, { recursive: true }).filter(
    x =>
      (x.endsWith('.razor') || x.endsWith('.js')) &&
      (x.startsWith('Pages/') ||
        x.startsWith('Shared/') ||
        x.startsWith('Components/') ||
        x.startsWith('Scripts/')),
  );
  for (const file of sources) {
    const source = readFileSync(new URL(file, client), 'utf8');
    for (const root of readings.keys()) {
      if (file.includes(owner(root))) continue;
      // The root as a whole class name - not stop-hours__row, not
      // stop-hours--compact - with the reading it asks for beside it.
      for (const written of source.matchAll(
        new RegExp(`(?<![\\w-])${root}(?![\\w-])`, 'g'),
      )) {
        const around = source.slice(
          Math.max(0, written.index - 60),
          written.index + 60,
        );
        assert.match(
          around,
          new RegExp(`${root}--[a-z]`),
          `${file}: .${root} written here without asking for a reading`,
        );
      }
    }
  }
});

test('HOS diameter and text use the same component-owned responsive value', () => {
  const css = compile("@use 'components/driver-status';");
  assert.match(
    css,
    /--_hos-dial-size: var\(--hos-dial-size, var\(--size-hos-dial\)\)/,
  );
  assert.match(
    css,
    /@media \(width < 551px\)[\s\S]*--_hos-dial-size: var\(--hos-dial-size, var\(--size-hos-dial-compact\)\)/,
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

// The map's truck card reads the clocks as text: "Break 0:00" on a line.
// Squeezed, "Break" came apart into three rows of single letters, because a
// label under a dial is a column head allowed to break anywhere to fit.
test('a clock read on a line keeps its label whole', () => {
  const css = compile("@use 'components/driver-status';");
  const text = css.slice(css.indexOf('.driver-hours--text'));
  assert.match(
    text,
    /\.driver-hours--text \.driver-hours__label\s*\{[^}]*overflow-wrap: normal;[^}]*white-space: nowrap;/,
  );
  // Under a dial it still may: there it is narrower than the word.
  assert.match(
    css.slice(0, css.indexOf('.driver-hours--text')),
    /\.driver-hours__label\s*\{[^}]*overflow-wrap: anywhere;/,
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
