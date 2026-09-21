import test from 'node:test';
import assert from 'node:assert/strict';
import {
  segmentFraction,
  segmentDistanceSquared,
} from '../../Scripts/fleetMap/geometry/segmentProjection.ts';

test('segment projection clamps to endpoints and handles duplicate points', () => {
  assert.equal(segmentFraction(5, 3, 10, 0), 0.5);
  assert.equal(segmentFraction(-5, 3, 10, 0), 0);
  assert.equal(segmentFraction(15, 3, 10, 0), 1);
  assert.equal(segmentFraction(5, 3, 0, 0), 0);
  assert.equal(segmentDistanceSquared(5, 3, 10, 0, 0.5), 9);
  assert.equal(segmentDistanceSquared(5, 3, 0, 0, 0), 34);
});

test('segment projection preserves the original route calculation', () => {
  for (let i = -50; i <= 50; i++) {
    const x = i / 7,
      y = i % 9,
      dx = i % 4,
      dy = i % 5;
    const length = dx * dx + dy * dy;
    const fraction = length
      ? Math.max(0, Math.min(1, (x * dx + y * dy) / length))
      : 0;
    assert.equal(segmentFraction(x, y, dx, dy), fraction);
    assert.equal(
      segmentDistanceSquared(x, y, dx, dy, fraction),
      (x - fraction * dx) ** 2 + (y - fraction * dy) ** 2,
    );
  }
});
