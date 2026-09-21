import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { compileString } from 'sass';
import {
  currentRouteColor,
  currentRouteLineColor,
  futureRouteColor,
} from '../../Scripts/fleetMap/rendering/routePalette.ts';
import { defaultStopLabelStyle } from '../../Scripts/fleetMap/rendering/stopLabelStyle.ts';

// The map draws on the GPU, which takes numbers, not custom properties, so
// the colours it cannot read at run time are written into its own modules.
// Nothing held those numbers to the palette they were copied from: a role
// could be changed in one place and the map would keep the old colour
// without a word. These checks are that bridge.

const loadPaths = [fileURLToPath(new URL('../../Styles/', import.meta.url))];
const hex = ([red, green, blue]) =>
  `#${[red, green, blue].map(c => c.toString(16).padStart(2, '0')).join('')}`;

const exported = new Map(
  [
    ...compileString("@use 'global/root';", { loadPaths }).css.matchAll(
      /--ui-([a-z0-9-]+):\s*(#[0-9a-f]{6})/gi,
    ),
  ].map(([, role, value]) => [role, value.toLowerCase()]),
);
const role = name => {
  const value = exported.get(name);
  assert.ok(value, `the theme exports no ${name}`);
  return value;
};

const palette = compileString(
  `@use 'base/colors' as p;
   .x { color: p.value(amber, 700); border-color: p.value('brown', 700); }`,
  { loadPaths },
).css;
const primitive = name => {
  const value = palette.match(new RegExp(`${name}:\\s*(#[0-9a-f]{6})`, 'i'));
  assert.ok(value, `the palette has no ${name}`);
  return value[1].toLowerCase();
};

// Without a mounted scene a stop label falls back to these; mounted, it
// reads the same roles off the probe element the popup stylesheet styles.
test('a stop label falls back to the roles it reads when it is mounted', () => {
  for (const [key, name] of Object.entries({
    color: 'text',
    background: 'surface',
    border: 'border-subtle',
    pickup: 'pickup',
    delivery: 'delivery',
    eta: 'link',
    success: 'success-text',
    danger: 'danger-text',
    muted: 'text-secondary',
  }))
    assert.equal(hex(defaultStopLabelStyle[key]), role(name), key);
});

test('the roads a truck is given are the map route roles, in order', () => {
  assert.equal(hex(currentRouteColor), role('map-route-current'));
  for (const [index, name] of [
    'map-route-option-one',
    'map-route-option-two',
    'map-route-option-three',
  ].entries())
    assert.equal(hex(futureRouteColor(index)), role(name), name);
  // The series runs longer than the roles do, and continues through the
  // palette those roles are chosen from.
  assert.equal(hex(futureRouteColor(3)), primitive('color'));
  assert.equal(hex(futureRouteColor(4)), primitive('border-color'));
  assert.deepEqual(futureRouteColor(5), futureRouteColor(0));
});

// A colour that is neither a role nor a palette entry is the map's own, and
// every one of them is named here. A new number in a rendering module fails
// this until someone says which it is.
const mapsOwn = new Map([
  ['#006aeb', 'the line a truck is driving now'],
  ['#315eea', 'the badge of a stop on that line'],
  ['#1e293b', 'the edge of a stop that is picked'],
  ['#64748b', 'miles with no load on board'],
  ['#9169c9', 'a road still to come'],
  ['#ffffff', 'paper, behind a badge or under a line'],
  ['#000000', 'ink, where a shadow is drawn'],
]);

test('every colour a rendering module draws with is accounted for', () => {
  const roles = new Set(exported.values());
  const directory = new URL(
    '../../Scripts/fleetMap/rendering/',
    import.meta.url,
  );
  const claimed = new Set();
  for (const file of readdirSync(directory).filter(
    name => name.endsWith('.js') || name.endsWith('.ts'),
  )) {
    const source = readFileSync(new URL(file, directory), 'utf8');
    for (const [literal, red, green, blue] of source.matchAll(
      /\[\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})\s*(?:,\s*\d{1,3}\s*)?,?\s*\]/g,
    )) {
      const value = hex([red, green, blue].map(Number));
      claimed.add(value);
      assert.ok(
        roles.has(value) || mapsOwn.has(value),
        `${file}: ${literal} is ${value} - name it as a role or as the map's own`,
      );
    }
  }
  for (const value of mapsOwn.keys())
    if (!claimed.has(value) && value !== '#000000')
      assert.fail(`nothing draws with ${value} any more`);
});
