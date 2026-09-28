import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

// New information arrives as the HUD shows it (the owner, September 28):
// a short settle with one band of light, labels written in, values whole.
// It plays when an element is created, so what keeps an element across a
// poll is its @key; these checks guard the styles and those keys.
const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const css = compileString("@use 'global'; @use 'pages';", { loadPaths }).css;
const read = path =>
  readFileSync(new URL(`../../${path}`, import.meta.url), 'utf8');

const keyframes = name => {
  const start = css.indexOf(`@keyframes ${name} {`);
  assert.ok(start >= 0, `@keyframes ${name}`);
  let depth = 0;
  for (let i = css.indexOf('{', start); i < css.length; i++) {
    if (css[i] === '{') depth++;
    else if (css[i] === '}' && --depth === 0) return css.slice(start, i);
  }
  return '';
};

test('arrival moves only opacity, transform, clip-path, an overlay', () => {
  for (const name of [
    'hud-arrive',
    'hud-scan',
    'hud-type',
    'hud-arrive-still',
  ]) {
    const properties = [...keyframes(name).matchAll(/([a-z-]+)\s*:/g)].map(
      match => match[1],
    );
    assert.ok(properties.length > 0, name);
    for (const property of properties)
      assert.ok(
        ['opacity', 'transform', 'clip-path', 'background-position'].includes(
          property,
        ),
        `${name} animates ${property}`,
      );
  }
  // The scan's overlay never takes a pointer, and nothing loops.
  assert.match(css, /::after\s*\{[^}]*pointer-events: none;[^}]*hud-scan/);
  assert.doesNotMatch(css, /hud-(?:arrive|scan|type)[^;]*infinite/);
});

test('reduced motion keeps a brief fade: no movement, scan, writing', () => {
  const reduced = [
    ...css.matchAll(
      /@media \(prefers-reduced-motion: reduce\)\s*\{([\s\S]*?)\n\}/g,
    ),
  ]
    .map(match => match[1])
    .join('\n');
  assert.match(reduced, /animation: hud-arrive-still 150ms linear both;/);
  assert.match(reduced, /::after\s*\{\s*animation: none;\s*opacity: 0;/);
  assert.match(reduced, /\.fleet-truck-facts dt\s*\{\s*animation: none;/);
  assert.doesNotMatch(reduced, /hud-scan|hud-type /);
  assert.match(keyframes('hud-arrive-still'), /opacity/);
  assert.doesNotMatch(keyframes('hud-arrive-still'), /transform/);
});

test('labels are written in; values always appear whole', () => {
  const typed = [...css.matchAll(/([^{}]+)\{[^}]*animation: hud-type /g)].map(
    match => match[1].trim(),
  );
  assert.deepEqual(typed.sort(), [
    '.fleet-route-popup--stop .fleet-route-popup__label',
    '.fleet-truck-facts dt',
    '.fleet-truck-next__label',
  ]);
  // A label's whole text is in the page from the start; only its drawing
  // is revealed.
  assert.match(
    read('Pages/FleetMap/FleetMap.razor'),
    /<span class="fleet-truck-next__label">Next stop<\/span>/,
  );
  assert.match(keyframes('hud-type'), /clip-path: inset\(0 100% 0 0\)/);
});

test('panels showing new information use the shared mixin', () => {
  for (const selector of [
    '.fleet-trip-chain__card',
    '.fleet-truck-next',
    '.fleet-truck-facts__fact',
    '.fleet-truck-clocks',
    '.fleet-route-popup--stop',
    '.dispatch-load',
    '.dispatch-table__row > td',
  ]) {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const rule = `(?:^|\\n|,\\s*)${escaped}\\s*\\{[^}]*`;
    assert.match(css, new RegExp(`${rule}animation: hud-arrive `), selector);
  }
});

test('an element is kept across a poll by a stable key', () => {
  const chain = read('Pages/FleetMap/FleetTripChain.razor');
  assert.match(chain, /@key="\(load\.Id, load\.ExecutionLegId\)"/);
  assert.match(chain, /style="--hud-index: @index"/);
  const map = read('Pages/FleetMap/FleetMap.razor');
  assert.match(map, /class="fleet-truck-next"[^>]*@key="\(0, upcoming\.Id\)"/);
  assert.match(
    map,
    /class="fleet-truck-facts"[^>]*@key="\(1, panelTruck\.TruckId\)"/,
  );
  assert.match(
    map,
    /class="fleet-truck-clocks" @key="\(2, panelTruck\.TruckId\)"/,
  );
  assert.match(
    read('Pages/Dispatch/DispatchList.razor'),
    /<DispatchLoadCard @key="LoadIdentity\(load\)"/,
  );
  assert.match(
    read('Pages/Dispatch/DispatchTable.razor'),
    /<tr @key="row\.Key" class="dispatch-table__row/,
  );
  // No key that changes with every poll.
  for (const markup of [chain, map])
    assert.doesNotMatch(markup, /@key="[^"]*(?:Eta|Now|Updated|Reading)/);
});
