import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const wordmark = read('../../wwwroot/brand/pulsr.svg');
const favicon = read('../../wwwroot/favicon.svg');

function attributes(tag) {
  return Object.fromEntries(
    [...tag.matchAll(/([\w:-]+)\s*=\s*(["'])(.*?)\2/gs)].map(
      ([, name, , value]) => [name, value],
    ),
  );
}

const artworkSafety =
  'brand SVG artwork uses no fonts, scripts, bitmaps or external resources';

test(artworkSafety, () => {
  for (const [name, svg] of [
    ['wordmark', wordmark],
    ['favicon', favicon],
  ]) {
    const root = svg.match(/<svg\b[^>]*>/i)?.[0];
    assert.ok(root, `${name} must be an SVG document`);
    assert.equal(attributes(root).xmlns, 'http://www.w3.org/2000/svg', name);
    assert.ok(
      attributes(root).viewBox,
      `${name} must preserve scalable geometry`,
    );
    assert.doesNotMatch(
      svg,
      /<(?:text|font|script|image|foreignObject|iframe)\b/i,
      name,
    );
    assert.doesNotMatch(
      svg,
      /<!DOCTYPE|<!ENTITY|\bon[a-z]+\s*=|@import|@font-face|font-family\s*:/i,
      name,
    );

    const ids = new Set(
      [...svg.matchAll(/\bid\s*=\s*(["'])(.*?)\1/g)].map(match => match[2]),
    );
    for (const reference of svg.matchAll(
      /\b(?:xlink:)?href\s*=\s*(["'])(.*?)\1/g,
    )) {
      assert.ok(
        reference[2].startsWith('#'),
        `${name} must use local fragment references`,
      );
      assert.ok(
        ids.has(reference[2].slice(1)),
        `${name} contains a missing fragment: ${reference[2]}`,
      );
    }
    for (const reference of svg.matchAll(/url\(\s*(["']?)(.*?)\1\s*\)/g)) {
      assert.ok(
        reference[2].startsWith('#'),
        `${name} must not load a remote resource`,
      );
    }

    const visible = svg
      .replace(/<defs\b[^>]*>[\s\S]*?<\/defs>/gi, '')
      .replace(/<symbol\b[^>]*>[\s\S]*?<\/symbol>/gi, '');
    assert.match(
      visible,
      /<(?:use|path|rect|polygon)\b/,
      `${name} must render when opened on its own`,
    );
  }
  assert.equal(
    attributes(wordmark.match(/<svg\b[^>]*>/)[0]).viewBox,
    '0 0 480 104',
  );
  assert.match(wordmark, /<symbol\b[^>]*\bid=["']wordmark["']/);
});

test('the wordmark and favicon share the five-bar pulse artwork', () => {
  const pulse = svg => {
    const paths = [...svg.matchAll(/<path\b[^>]*>/g)]
      .map(match => attributes(match[0]))
      .filter(path => path.id === 'pulse');
    assert.equal(
      paths.length,
      1,
      'Each asset must identify exactly one canonical pulse',
    );
    assert.equal(paths[0].fill, 'none');
    assert.equal(paths[0].stroke, 'url(#pulse-color)');
    assert.equal(paths[0]['stroke-width'], '10');
    assert.equal(paths[0]['stroke-linecap'], 'round');
    assert.ok(paths[0].d, 'The pulse must remain vector path artwork');
    const bars = [...paths[0].d.matchAll(/M(\d+) (\d+)V(\d+)/g)].map(
      ([, x, top, bottom]) => [Number(x), Number(top), Number(bottom)],
    );
    assert.deepEqual(bars, [
      [5, 43, 61],
      [22, 31, 73],
      [39, 13, 91],
      [56, 31, 73],
      [73, 43, 61],
    ]);
    const gradient = svg.match(
      /<linearGradient\b[^>]*>[\s\S]*?<\/linearGradient>/,
    );
    assert.ok(gradient, 'The pulse must keep its red-to-coral gradient');
    const stops = [...gradient[0].matchAll(/<stop\b[^>]*>/g)].map(
      match => attributes(match[0])['stop-color'],
    );
    assert.deepEqual(stops, ['#ec354b', '#ff6371']);
    assert.doesNotMatch(svg, /signature-r/);
    return paths[0].d
      .match(/[a-z]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:e[-+]?\d+)?/gi)
      .map(token => (/^[a-z]$/i.test(token) ? token : Number(token)));
  };
  assert.deepEqual(pulse(favicon), pulse(wordmark));
  assert.equal([...favicon.matchAll(/<path\b/g)].length, 1);
  const backing = attributes(favicon.match(/<rect\b[^>]*>/)[0]);
  assert.equal(backing.fill, '#ffffff');
  assert.equal(backing.rx, '24');
  const symbol = wordmark.match(
    /<symbol\b[^>]*\bid=["']wordmark["'][^>]*>([\s\S]*?)<\/symbol>/,
  )?.[1];
  assert.ok(symbol, 'The reusable wordmark symbol must exist');
  assert.match(symbol, /<use\b[^>]*\bhref=["']#pulse["']/);
});

test('product surfaces and guide reference the shared artwork', () => {
  const sidebar = read('../../Layout/Sidebar.razor');
  const login = read('../../Pages/Auth/Login.razor');
  const component = read('../../Shared/Brand/BrandLogo/BrandLogo.razor');
  const guide = read('../../wwwroot/brand/index.html');
  const index = read('../../wwwroot/index.html');

  assert.match(sidebar, /<BrandLogo\b[^>]*\bReversed=(?:"true"|true)/);
  assert.match(login, /<BrandLogo\b[^>]*\bProminent=(?:"true"|true)/);
  assert.doesNotMatch(sidebar, /sidebar__monogram|sidebar__wordmark/);
  const brands = [
    sidebar.match(/<div class="sidebar__brand">([\s\S]*?)<\/div>/)?.[1],
    login.match(/<div class="login-page__brand">([\s\S]*?)<\/div>/)?.[1],
  ];
  for (const brand of brands) {
    assert.ok(brand);
    assert.doesNotMatch(brand, /<text\b|<path\b/);
  }
  const artworkUrl = 'pulsr.svg?v=pulse-red-2#wordmark';
  assert.ok(component.includes(`href="brand/${artworkUrl}"`));
  assert.ok(guide.includes(`href="${artworkUrl}"`));
  assert.match(component, /viewBox="0 0 480 104"/);
  const style = read('../../Styles/shared/_brand-logo.scss');
  assert.match(style, /aspect-ratio:\s*480\s*\/\s*104/);
  assert.match(guide, /favicon\.svg/);
  assert.doesNotMatch(
    guide,
    /<script\b|<path\b|fonts\.googleapis\.com|fonts\.gstatic\.com/,
  );

  const icons = [...index.matchAll(/<link\b[^>]*>/g)]
    .map(match => attributes(match[0]))
    .filter(link => link.rel?.split(/\s+/).includes('icon'));
  assert.ok(
    icons.some(
      icon =>
        icon.href === 'favicon.svg?v=pulse-red-2' &&
        icon.type === 'image/svg+xml',
    ),
  );

  const rules = read('../../../docs/design/brand-design.md');
  assert.match(rules, /Client\/wwwroot\/brand\/pulsr\.svg/);
  assert.match(rules, /Client\/Shared\/Brand\/BrandLogo\/BrandLogo\.razor/);
  assert.match(rules, /Client\/wwwroot\/favicon\.svg/);
});
