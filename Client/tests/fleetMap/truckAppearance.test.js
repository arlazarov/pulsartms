import test from 'node:test';
import assert from 'node:assert/strict';
import {
  truckColor,
  truckEngine,
  truckIcon,
  truckMotion,
} from '../../Scripts/fleetMap/rendering/truckAppearance.ts';

const svg = (engine, speed) => decodeURIComponent(truckIcon(engine, speed).url);

test('a moving truck is the north-facing arrow with its original anchor', () => {
  for (const engine of ['on', 'idle', 'off', '', null]) {
    const icon = truckIcon(engine, 45);
    assert.equal(icon.anchorX, 56);
    assert.equal(icon.anchorY, 56);
    assert.equal(icon.width, 112);
    assert.equal(icon.height, 120);
    assert.equal(icon.mask, false);
    assert.match(svg(engine, 45), /viewBox="-1 -1 28 30"/);
    assert.match(
      svg(engine, 45),
      /d="M13 1 L24 23 Q25 26 22 25 L13 22 L4 25 Q1 26 2 23 Z" fill="#1e293b"/,
    );
    assert.doesNotMatch(svg(engine, 45), /<circle|filter|blur|gradient/);
  }
});

test('a standing truck is a circle, whatever its engine', () => {
  for (const engine of ['on', 'idling', 'off', 'unknown', undefined]) {
    assert.match(
      svg(engine, 0),
      /<circle cx="13" cy="13" r="11" fill="#1e293b"/,
    );
    assert.doesNotMatch(svg(engine, 0), /<path/);
  }
});

test('the engine accent comes from the engine reading, never from speed', () => {
  for (const speed of [0, 45]) {
    for (const engine of ['on', 'running', 'idle', ' IDLING '])
      assert.match(svg(engine, speed), /stroke="#16a34a" stroke-width="2.5"/);
    assert.match(svg('off', speed), /stroke="#94a3b8" stroke-width="2"\/?/);
    assert.doesNotMatch(svg('off', speed), /#16a34a|dasharray/);
    // Moving says nothing about the engine: no reading stays unknown.
    for (const engine of ['', 'driving', null, undefined])
      assert.match(svg(engine, speed), /stroke-dasharray="3 2"/);
  }
  assert.equal(truckEngine('driving'), 'unknown');
  assert.equal(truckColor('', 60), '#64748b');
  assert.equal(truckColor('idle', 0), '#16a34a');
});

test('motion follows finite speed, and each shape and reading is cached once', () => {
  for (const speed of [0, 0.5, -0.5, NaN, Infinity, undefined])
    assert.equal(truckMotion(speed), 'standing');
  assert.equal(truckMotion(1), 'moving');
  assert.equal(truckIcon('on', 0), truckIcon('idle', 0));
  assert.equal(truckIcon('on', 5), truckIcon('running', 60));
  assert.notEqual(truckIcon('on', 5), truckIcon('off', 5));
  assert.notEqual(truckIcon('off', 0), truckIcon(undefined, 0));
});
