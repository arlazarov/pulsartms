import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  truckColor,
  truckEngine,
  truckIcon,
  truckMotion,
} from '../../Scripts/fleetMap/rendering/truckAppearance.ts';

const svg = (engine, speed) => decodeURIComponent(truckIcon(engine, speed).url);
const silhouette = 'd="M13 1 L24 23 Q25 26 22 25 L13 22 L4 25 Q1 26 2 23 Z"';

// The map's theme is the page's; without a page the light map stands.
function onDarkMap(check) {
  const outer = globalThis.document;
  globalThis.document = { documentElement: { dataset: { theme: 'dark' } } };
  try {
    check();
  } finally {
    globalThis.document = outer;
  }
}

// Two themes, two sets of marks (the owner, September 27). The light map
// keeps the classic marks: a green arrow moving, a green circle standing
// with the engine on and a grey one with it off. The dark map's marks are
// lit glass coloured by the engine - green running, bright cyan off, grey
// with a dashed edge unknown - an arrow moving and a HUD sight standing.
test('a moving truck is the north-facing arrow with its original anchor', () => {
  const anchored = icon => {
    assert.equal(icon.anchorX, 56);
    assert.equal(icon.anchorY, 56);
    assert.equal(icon.width, 112);
    assert.equal(icon.height, 120);
    assert.equal(icon.mask, false);
  };
  for (const engine of ['on', 'idle', 'off', '', null]) {
    anchored(truckIcon(engine, 45));
    assert.match(svg(engine, 45), /viewBox="-1 -1 28 30"/);
    assert.match(
      svg(engine, 45),
      new RegExp(`${silhouette} fill="#16a34a"`),
      'the light map: a classic green arrow whatever the engine',
    );
    assert.doesNotMatch(svg(engine, 45), /<circle|filter|blur|gradient/);
  }
  onDarkMap(() => {
    for (const engine of ['on', 'idle', 'off', '', null]) {
      anchored(truckIcon(engine, 45));
      assert.match(svg(engine, 45), /viewBox="-1 -1 28 30"/);
      assert.match(
        svg(engine, 45),
        new RegExp(`${silhouette} fill="url\\(#b\\)"`),
      );
      assert.doesNotMatch(svg(engine, 45), /<circle|filter|blur/);
    }
  });
});

test('a standing truck is a circle, whatever its engine', () => {
  for (const [engine, fill] of [
    ['on', '#16a34a'],
    ['idling', '#16a34a'],
    ['off', '#64748b'],
    ['unknown', '#64748b'],
    [undefined, '#64748b'],
  ]) {
    assert.match(
      svg(engine, 0),
      new RegExp(`<circle cx="13" cy="13" r="11" fill="${fill}"`),
    );
    assert.doesNotMatch(svg(engine, 0), /<path/);
  }
  // The dark map's standing mark is a HUD sight: a ring of the engine's
  // colour around a glass core, never the arrow.
  onDarkMap(() => {
    for (const engine of ['on', 'idling', 'off', 'unknown', undefined]) {
      assert.match(
        svg(engine, 0),
        /<circle cx="13" cy="13" r="12.4" fill="none"[^>]*stroke-width="2"\/>/,
      );
      assert.match(
        svg(engine, 0),
        /<circle cx="13" cy="13" r="8.6" fill="url\(#b\)"/,
      );
      assert.doesNotMatch(svg(engine, 0), /<path/);
    }
  });
});

test('the engine accent comes from the engine reading, never from speed', () => {
  onDarkMap(() => {
    for (const speed of [0, 45]) {
      for (const engine of ['on', 'running', 'idle', ' IDLING ']) {
        assert.match(svg(engine, speed), /stroke="#16a34a" stroke-width="2.5"/);
        assert.match(svg(engine, speed), /stop-color="#4ade80"/);
        assert.doesNotMatch(svg(engine, speed), /dasharray/);
      }
      // Off: bright cyan glass with a cyan glow, a solid edge.
      assert.match(svg('off', speed), /stroke="#475569" stroke-width="2"/);
      assert.match(svg('off', speed), /stop-color="#22d3ee"/);
      assert.doesNotMatch(svg('off', speed), /#16a34a|#4ade80|dasharray/);
      // Moving says nothing about the engine: no reading stays unknown,
      // grey glass with a dashed edge.
      for (const engine of ['', 'driving', null, undefined]) {
        assert.match(svg(engine, speed), /stroke-dasharray="3 2"/);
        assert.match(svg(engine, speed), /stop-color="#94a3b8"/);
        assert.doesNotMatch(svg(engine, speed), /#16a34a|#4ade80/);
      }
    }
    assert.equal(truckColor('', 60), '#64748b');
    assert.equal(truckColor('idle', 0), '#16a34a');
  });
  assert.equal(truckEngine('driving'), 'unknown');
  // The light map's ring follows its classic marks: green while a truck
  // moves or idles, grey while it stands with the engine off.
  assert.equal(truckColor('idle', 0), '#16a34a');
  assert.equal(truckColor('off', 0), '#64748b');
  assert.equal(truckColor('off', 60), '#16a34a');
});

test('motion follows finite speed, and each shape and reading is cached once', () => {
  for (const speed of [0, 0.5, -0.5, NaN, Infinity, undefined])
    assert.equal(truckMotion(speed), 'standing');
  assert.equal(truckMotion(1), 'moving');
  // The light map has three marks: one arrow, two circles.
  assert.equal(truckIcon('on', 0), truckIcon('idle', 0));
  assert.equal(truckIcon('on', 5), truckIcon('off', 60));
  assert.notEqual(truckIcon('on', 0), truckIcon('off', 0));
  assert.equal(truckIcon('off', 0), truckIcon(undefined, 0));
  const light = truckIcon('on', 5);
  onDarkMap(() => {
    assert.equal(truckIcon('on', 0), truckIcon('idle', 0));
    assert.equal(truckIcon('on', 5), truckIcon('running', 60));
    assert.notEqual(truckIcon('on', 5), truckIcon('off', 5));
    assert.notEqual(truckIcon('off', 0), truckIcon(undefined, 0));
    assert.notEqual(truckIcon('on', 5), light, 'each theme has its own marks');
  });
});

// The fleet list's Moving / Stopped chips read motion in C# (TruckMotion);
// a truck the list calls moving is the one the map draws as an arrow.
test('the list and the map split moving from standing at one speed', () => {
  const source = readFileSync(
    new URL('../../Shared/Trucks/TruckMotion.cs', import.meta.url),
    'utf8',
  );
  const threshold = Number(
    source.match(/MovingSpeed = ([\d.]+)m;/)?.[1] ?? NaN,
  );
  assert.ok(Number.isFinite(threshold), 'TruckMotion names no threshold');
  assert.equal(truckMotion(threshold), 'moving');
  assert.equal(truckMotion(threshold - 0.01), 'standing');
});
