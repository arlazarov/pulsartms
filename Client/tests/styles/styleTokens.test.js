import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { compileString } from 'sass';
import { fileURLToPath } from 'node:url';

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];

test('popup secondary cycle warnings span both columns without changing the inline arrival status', () => {
  const css = compileString(
    "@use 'shared/driver-status'; @use 'pages/fleet-map/popup';@use 'pages/fleet-map/station';@use 'shared/fuel/visit';",
    { loadPaths },
  ).css;
  assert.match(css, /\.stop-hours__arrival\s*\{\s*display: contents;/);
  // The popup asks the forecast for its inline reading and styles only the
  // rows it builds itself; it used to name the component's elements beside
  // its own and say the same thing twice.
  assert.match(
    css,
    /\.stop-hours--inline\s*\{[^}]*--stop-hours-road-value-display: contents;/,
  );
  assert.match(
    css,
    /\.fleet-route-popup__value--cycle\s*\{\s*display: contents;/,
  );
  assert.match(
    css,
    /\.fleet-route-popup__cycle-status\s*\{\s*grid-column: 1\s*\/\s*-1;/,
  );
  assert.match(css, /\.fleet-route-popup__arrival\s*\{\s*display: flex;/);
  assert.match(
    readFileSync(
      new URL(
        '../../Scripts/fleetMap/routes/stopCardContent.js',
        import.meta.url,
      ),
      'utf8',
    ),
    /'stop-hours stop-hours--inline'/,
  );
});

test('public style API emits no CSS and exposes only the intended controls and tokens', () => {
  assert.equal(compileString("@use 'base' as ui;", { loadPaths }).css, '');
  const css = compileString(
    "@use 'base' as ui; .x { padding: ui.pg(sm); @include ui.button-control; }",
    { loadPaths },
  ).css;
  assert.match(css, /var\(--space-sm\)/);
  assert.throws(
    () =>
      compileString(
        "@use 'base' as ui; .x { width: ui.scale-value(1, 2, 3); }",
        { loadPaths },
      ),
    /Undefined function/,
  );
  for (const folder of ['pages', 'components', 'shared', 'layouts']) {
    const root = new URL(`../../Styles/${folder}/`, import.meta.url);
    for (const file of readdirSync(root, { recursive: true }).filter(x =>
      x.endsWith('.scss'),
    ))
      assert.doesNotMatch(
        readFileSync(new URL(file, root), 'utf8'),
        /@use ['"][^'"]*base\//,
        `${folder}/${file}: use the public base API`,
      );
  }
});

test('theme roles derive all colors from the primitive palette', () => {
  const source = readFileSync(
    new URL('../../Styles/base/_themes.scss', import.meta.url),
    'utf8',
  );
  assert.doesNotMatch(source, /#[\da-f]{3,8}\b|rgba?\(/i);
  assert.doesNotThrow(() =>
    compileString(
      `@use 'sass:map'; @use 'base/colors' as p; @use 'base/themes' as t;
    @if map.get(t.$roles, action) != p.value(primary, 600) { @error 'Action does not use primary'; }
    @if map.get(t.$roles, surface) != p.value(neutral, 0) { @error 'Surface does not use neutral'; }`,
      { loadPaths },
    ),
  );
});

test('token functions validate names and preserve legacy font scales', () => {
  const css = compileString(
    `@use 'base/functions' as fn;
    .sample { padding: fn.pg(sm); font-size: fn.fs(body); width: fn.size(control);
      border-radius: fn.radius(sm); box-shadow: fn.shadow(popup); line-height: fn.fs(125); }`,
    { loadPaths },
  ).css;
  for (const token of [
    '--space-sm',
    '--type-body',
    '--size-control',
    '--radius-sm',
    '--shadow-popup',
  ])
    assert.ok(css.includes(token));
  for (const fn of ['pg', 'fs', 'size', 'theme', 'radius', 'shadow'])
    assert.throws(
      () =>
        compileString(
          `@use 'base/functions' as fn; .x { width: fn.${fn}(unknown-token); }`,
          { loadPaths },
        ),
      /Unknown/,
    );
});

test('spacing rejects the obsolete numeric scale and breakpoints retain exact boundaries', () => {
  assert.throws(
    () =>
      compileString(`@use 'base/functions' as fn; .x { gap: fn.pg(125); }`, {
        loadPaths,
      }),
    /named spacing/,
  );
  for (const retired of [
    'tiny',
    'snug',
    'compact',
    'inset',
    'comfortable',
    'roomy',
  ])
    assert.throws(
      () =>
        compileString(
          `@use 'base/functions' as fn; .x { gap: fn.pg(${retired}); }`,
          { loadPaths },
        ),
      /Unknown spacing/,
    );
  // A boundary is read from one side or the other, by name, and the width
  // it stands for is the one in the map - not a pixel short of it. Asking
  // for that shortened width used to fail outright on the rem boundaries
  // in the same map, because a pixel cannot be taken from a rem.
  const css = compileString(
    `@use 'base' as ui;
    @include ui.below(md) { .x { display:none; } }
    @include ui.above(map-mobile) { .y { display:block; } }
    @include ui.below(map-fuel-halves) { .z { display:none; } }`,
    { loadPaths },
  ).css;
  assert.match(css, /@media \(width < 800px\)/);
  assert.match(css, /@media \(width >= 768px\)/);
  assert.match(css, /@media \(width < 34rem\)/);
  assert.throws(
    () =>
      compileString(`@use 'base' as ui; @include ui.below(nowhere) { .x {} }`, {
        loadPaths,
      }),
    /Unknown breakpoint/,
  );
});

// A role that keeps its light value in dark is a decision, and the decision
// is written above the map. These are the three kinds it may belong to; a
// new one outside them is an oversight until the reasoning is extended.
test('a role that stays light in dark is one we said would', () => {
  const source = readFileSync(
    new URL('../../Styles/base/_themes.scss', import.meta.url),
    'utf8',
  );
  const light = source.slice(0, source.indexOf('$dark-roles'));
  const dark = source.slice(source.indexOf('$dark-roles'));
  const names = body =>
    [...body.matchAll(/^\s+([a-z][a-z0-9-]*):/gm)].map(m => m[1]);
  const unchanged = names(light).filter(role => !names(dark).includes(role));
  const expected =
    /^(?:map-|navigation|brand|pulse-|telemetry-)|^(?:action|action-hover|danger-action|danger-action-hover|shadow|overlay)$/;
  for (const role of unchanged)
    assert.match(role, expected, `${role} keeps its light value unexplained`);
  assert.ok(unchanged.length > 20, 'the light-kept roles were not found');
});

test('both themes export the same role contract', () => {
  assert.doesNotThrow(() =>
    compileString(
      `@use 'sass:map'; @use 'base/themes' as c;
    @each $name, $value in c.$roles {
      @if not map.has-key(c.$dark-roles, $name) { @error 'Missing dark role'; }
    }
    @each $name, $value in c.$dark-roles {
      @if not map.has-key(c.$roles, $name) { @error 'Unknown dark role'; }
    }`,
      { loadPaths },
    ),
  );
});

test('semantic transparency is validated', () => {
  const css = compileString(
    `@use 'base/functions' as fn; .x { color: fn.theme(focus, .12); }`,
    { loadPaths },
  ).css;
  assert.match(css, /color-mix\(in srgb, var\(--ui-focus\) 12%, transparent\)/);
  assert.throws(
    () =>
      compileString(
        `@use 'base/functions' as fn; .x { color: fn.theme(focus, 2); }`,
        { loadPaths },
      ),
    /alpha/,
  );
});

test('UI styles use palette functions instead of raw color literals', () => {
  for (const folder of ['pages', 'components', 'shared', 'layouts']) {
    const root = new URL(`../../Styles/${folder}/`, import.meta.url);
    for (const file of readdirSync(root, { recursive: true }).filter(x =>
      x.endsWith('.scss'),
    )) {
      const source = readFileSync(new URL(file, root), 'utf8');
      assert.doesNotMatch(
        source,
        /#[0-9a-f]{3,8}\b|rgba?\(/i,
        `${folder}/${file}`,
      );
      assert.doesNotMatch(
        source,
        /(?:fn|ui)\.pg\(\d|@media\s*\((?:min|max)-width:\s*\d/,
        `${folder}/${file}`,
      );
      assert.doesNotMatch(
        source,
        /(?:fn|ui)\.fs\(\d|min-height:\s*44px/,
        `${folder}/${file}`,
      );
    }
  }
});

test('all UI partials use semantic colors and named interface dimensions', () => {
  for (const folder of ['pages', 'components', 'shared', 'layouts']) {
    const root = new URL(`../../Styles/${folder}/`, import.meta.url);
    for (const file of readdirSync(root, { recursive: true }).filter(x =>
      x.endsWith('.scss'),
    )) {
      let source = readFileSync(new URL(file, root), 'utf8');
      const illustration = folder === 'pages' && file === 'dispatch/_rig.scss';
      if (!illustration)
        assert.doesNotMatch(
          source,
          /(?:fn|ui)\.clr\(/,
          `${folder}/${file}: use semantic roles`,
        );
      if (illustration) source = source.replace('font-size: 9px;', ''); // SVG view-box label geometry.
      assert.doesNotMatch(
        source,
        /font-size:\s*[\d.]+(?:px|rem)\b/,
        `${folder}/${file}: use font tokens`,
      );
      assert.doesNotMatch(
        source,
        /(?:^|[;{\s])(?:padding|margin)(?:-[a-z]+)?:[^;{}]*\dpx|(?:row-|column-)?gap:[^;{}]*\dpx/,
        `${folder}/${file}: use spacing tokens`,
      );
      assert.doesNotMatch(
        source,
        /border-radius:[^;{}]*\dpx/,
        `${folder}/${file}: use radius tokens`,
      );
    }
  }
});

// An element carrying the attribute is hidden by one rule, in the app's own
// stylesheet. Because that rule is important it already beats any display a
// card sets on its children, whatever the card's selector weighs, so cards
// that repeated it inside themselves were saying nothing - three of them did.
test('hiding an element by attribute is said once, for the whole app', () => {
  const root = new URL('../../Styles/', import.meta.url);
  assert.match(
    readFileSync(new URL('global/_root.scss', root), 'utf8'),
    /\[hidden\]\s*\{\s*display: none !important;/,
  );
  for (const file of readdirSync(root, { recursive: true }).filter(
    x => x.endsWith('.scss') && !x.startsWith('global/'),
  ))
    assert.doesNotMatch(
      readFileSync(new URL(file, root), 'utf8'),
      /\[hidden\]\s*\{\s*display: none/,
      `${file}: the app already hides it`,
    );
});

// What stands in front of what is decided in one map. Picking the next
// number by looking at a neighbour is how two unrelated components both
// came to claim a thousand.
test('standing in front of something is a named layer', () => {
  const root = new URL('../../Styles/', import.meta.url);
  for (const file of readdirSync(root, { recursive: true }).filter(
    x => x.endsWith('.scss') && !x.startsWith('base/'),
  ))
    for (const [declaration] of readFileSync(
      new URL(file, root),
      'utf8',
    ).matchAll(/z-index:[^;]*/g))
      assert.match(
        declaration,
        /ui\.layer\(|var\(/,
        `${file}: ${declaration.trim()} - use a named layer`,
      );
  assert.throws(
    () =>
      compileString("@use 'base' as ui; .x { z-index: ui.layer(above); }", {
        loadPaths,
      }),
    /Unknown layer/,
  );
});

// Every width at which something changes shape is a name in one map, read
// the same way whether the thing measured is the window or a card's own
// box. These were written three ways: through the name, in bare rem, and
// once in bare pixels that the map had never heard of.
test('a layout changes at a named width, never at a number', () => {
  const root = new URL('../../Styles/', import.meta.url);
  for (const file of readdirSync(root, { recursive: true }).filter(x =>
    x.endsWith('.scss'),
  )) {
    const source = readFileSync(new URL(file, root), 'utf8');
    if (file.startsWith('base/')) continue;
    for (const [query] of source.matchAll(/@container(?:[^{#]|#\{[^}]*\})*\{/g))
      assert.match(query, /breakpoint\(/, `${file}: ${query.trim()}`);
    assert.doesNotMatch(
      source,
      /@media \((?:max|min)-width:/,
      `${file}: say below() or above()`,
    );
  }
});

test('all SCSS modules are reachable from the main stylesheet', async () => {
  const { compile } = await import('sass');
  const root = new URL('../../Styles/', import.meta.url);
  const loaded = new Set(
    compile(fileURLToPath(new URL('main.scss', root))).loadedUrls.map(x =>
      fileURLToPath(x),
    ),
  );
  for (const file of readdirSync(root, { recursive: true }).filter(x =>
    x.endsWith('.scss'),
  ))
    assert.ok(
      loaded.has(fileURLToPath(new URL(file, root))),
      `Orphan stylesheet: ${file}`,
    );
});

test('truck motion is owned by its animation module', () => {
  const visual = readFileSync(
    new URL('../../Styles/pages/dispatch/_rig.scss', import.meta.url),
    'utf8',
  );
  assert.doesNotMatch(visual, /@keyframes|animation(?:-\w+)?:/);
  const motion = readFileSync(
    new URL('../../Styles/pages/dispatch/_rig-motion.scss', import.meta.url),
    'utf8',
  );
  assert.match(motion, /prefers-reduced-motion/);
  for (const state of ['is-off', 'is-moving', 'is-idling'])
    assert.ok(motion.includes(state));
  const definitions = [...motion.matchAll(/@keyframes ([\w-]+)/g)].map(
    x => x[1],
  );
  assert.equal(new Set(definitions).size, definitions.length);
  for (const [, name] of motion.matchAll(/animation:\s*([\w-]+)/g))
    assert.ok(definitions.includes(name), name);
});

test('theme text roles meet normal-text contrast on their supported surfaces', () => {
  const css = compileString(
    `@use 'base/themes' as t;
    .light { @each $key, $value in t.$roles { --#{$key}: #{$value}; } }
    .dark { @each $key, $value in t.$dark-roles { --#{$key}: #{$value}; } }`,
    { loadPaths },
  ).css;
  const luminance = hex => {
    const channels = hex
      .match(/\w\w/g)
      .map(x => parseInt(x, 16) / 255)
      .map(x => (x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4));
    return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
  };
  for (const [, theme, body] of css.matchAll(/\.(light|dark)\s*\{([^}]+)\}/g)) {
    const roles = Object.fromEntries(
      [...body.matchAll(/--([\w-]+):\s*#([0-9a-f]{6});/gi)].map(x => [
        x[1],
        x[2],
      ]),
    );
    const pairs = [
      'text',
      'text-secondary',
      'text-muted',
      'link',
      'success-text',
      'warning-text',
      'danger-text',
      'pickup',
      'delivery',
      'transit',
    ].flatMap(text =>
      ['surface', 'surface-soft', 'surface-muted', 'canvas'].map(surface => [
        text,
        surface,
      ]),
    );
    pairs.push(
      ...['text', 'text-secondary', 'text-muted', 'link'].map(text => [
        text,
        'selected',
      ]),
    );
    pairs.push(
      ...['action', 'action-hover', 'danger-action', 'danger-action-hover'].map(
        x => ['on-accent', x],
      ),
    );
    pairs.push(
      ['success-text', 'success-surface'],
      ['warning-text', 'warning-surface'],
      ['text', 'warning-surface'],
      ['on-accent', 'navigation-active'],
    );
    for (const [foreground, background] of pairs) {
      assert.ok(
        roles[foreground] && roles[background],
        `${theme}: missing role`,
      );
      const a = luminance(roles[foreground]),
        b = luminance(roles[background]);
      const ratio = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
      assert.ok(
        ratio >= 4.5,
        `${theme}: ${foreground}/${background} contrast ${ratio.toFixed(2)} < 4.5`,
      );
    }
  }
});
